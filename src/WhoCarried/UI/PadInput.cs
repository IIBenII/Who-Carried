using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Controller (and keyboard) input for the open recap. The panel takes key focus, so every non-mouse press reaches its
/// GuiInput before the game's input manager, hotkeys and focus navigation. Each is translated through the game's own
/// bindings into a <see cref="PadCommand"/>, acted on, and swallowed whether it meant anything or not. Mouse input and
/// F8 (polled) are untouched. docs/design/specs/2026-09-13-controller-support-design.md.
/// </summary>
internal sealed class PadInput
{
    /// <summary>The game's actions the recap listens for, and what each means here.</summary>
    private static readonly (StringName Action, PadCommand Command)[] Actions =
    {
        (MegaInput.viewDeckAndTabLeft, PadCommand.TabPrevious),
        (MegaInput.viewExhaustPileAndTabRight, PadCommand.TabNext),
        (MegaInput.cancel, PadCommand.Close),
        (MegaInput.pauseAndBack, PadCommand.Close),
        (MegaInput.confirm, PadCommand.Save),
        (MegaInput.up, PadCommand.Up),
        (MegaInput.down, PadCommand.Down),
        (MegaInput.left, PadCommand.Left),
        (MegaInput.right, PadCommand.Right),
        (MegaInput.altUp, PadCommand.ScrollUp),
        (MegaInput.altDown, PadCommand.ScrollDown),
    };

    /// <summary>The left stick moves the selection too; the game keeps it out of its rebindable map.</summary>
    private static readonly (StringName Action, PadCommand Command)[] Stick =
    {
        (Controller.lStickUp, PadCommand.Up), (Controller.lStickDown, PadCommand.Down),
        (Controller.lStickLeft, PadCommand.Left), (Controller.lStickRight, PadCommand.Right),
    };

    private static bool _failed;

    /// <summary>Logs every command (dev preview).</summary>
    public static bool Diagnostics { get; set; }

    /// <summary>The last command acted on (dev preview checks).</summary>
    public static PadCommand LastCommand { get; private set; }

    /// <summary>Whether the game is in controller mode (it switches on the first controller press, back on mouse use).</summary>
    public static bool ControllerMode => NControllerManager.Instance?.IsUsingDirectionalNavigation ?? false;

    private readonly PanelHandle _panel;
    private readonly Dictionary<StringName, StringName> _controllerMap;
    private readonly PadCursor _cursor = new();
    private readonly Dictionary<PadCommand, (ulong Since, ulong Done)> _held = new();
    private int _cursorTab = -1;
    private Control? _previousFocus;

    private PadInput(PanelHandle panel, Dictionary<StringName, StringName> controllerMap)
    {
        _panel = panel;
        _controllerMap = controllerMap;
    }

    /// <summary>
    /// Starts listening on a newly opened panel. If the game's controller bindings can't be read, controller support is
    /// off for the session (one log line) and the recap works as before with the mouse and F8.
    /// </summary>
    public static void Attach(PanelHandle panel)
    {
        if (_failed) return;
        try
        {
            var map = NInputManager.Instance is NInputManager input
                ? AccessTools.Field(typeof(NInputManager), "_controllerInputMap")?.GetValue(input) as Dictionary<StringName, StringName>
                : null;
            if (map == null || map.Count == 0) throw new InvalidOperationException("the game's controller bindings couldn't be read");
            new PadInput(panel, new Dictionary<StringName, StringName>(map)).Start();
        }
        catch (Exception e)
        {
            _failed = true;
            Tracker.LogError("controller support (off; mouse and F8 still work)", e);
        }
    }

    private void Start()
    {
        Control root = _panel.Root;
        var tree = (SceneTree)Engine.GetMainLoop();
        _previousFocus = tree.Root.GuiGetFocusOwner();
        root.FocusMode = Control.FocusModeEnum.All;
        root.GuiInput += OnInput;
        root.FocusExited += OnFocusLost;
        _panel.Tabs.TabChanged += _ => ClearSelection();
        _panel.Live.On(_ => Refit());
        tree.ProcessFrame += OnFrame;

        // Switching to controller mode moves focus to the game's default control: take it back at once, so a press in
        // the same frame can't reach the game.
        var signals = new[]
        {
            (Signal: NControllerManager.SignalName.MouseDetected, Handler: Callable.From(OnMouseMode)),
            (Signal: NControllerManager.SignalName.ControllerDetected, Handler: Callable.From(Grab)),
        };
        NControllerManager? controllers = NControllerManager.Instance;
        if (controllers != null)
            foreach ((StringName signal, Callable handler) in signals) controllers.Connect(signal, handler);
        root.TreeExiting += () =>
        {
            tree.ProcessFrame -= OnFrame;
            if (controllers != null && GodotObject.IsInstanceValid(controllers))
                foreach ((StringName signal, Callable handler) in signals)
                    if (controllers.IsConnected(signal, handler)) controllers.Disconnect(signal, handler);
            GiveFocusBack();
        };
        Callable.From(Grab).CallDeferred(); // the panel's layer can still be on its way into the tree
    }

    // ------------------------------------------------------------------ focus

    private void Grab()
    {
        Control root = _panel.Root;
        if (GodotObject.IsInstanceValid(root) && root.IsInsideTree() && root.IsVisibleInTree() && !root.HasFocus()) root.GrabFocus();
    }

    /// <summary>
    /// The game takes focus away when it switches input mode (to its default control, or to nothing): take it back, so
    /// presses keep coming here and never reach the game underneath.
    /// </summary>
    private void OnFocusLost()
    {
        _held.Clear();
        if (GodotObject.IsInstanceValid(_panel.Root) && _panel.Root.IsInsideTree()) Callable.From(Grab).CallDeferred();
    }

    /// <summary>On close, focus goes back to whatever had it (the podium, or the victory screen's button).</summary>
    private void GiveFocusBack()
    {
        Control? previous = _previousFocus;
        if (previous != null && GodotObject.IsInstanceValid(previous) && previous.IsInsideTree() && previous.IsVisibleInTree())
            previous.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void OnMouseMode()
    {
        _held.Clear();
        ClearSelection();
    }

    // ------------------------------------------------------------------ input

    private void OnInput(InputEvent input)
    {
        if (input is InputEventMouse or InputEventScreenTouch or InputEventScreenDrag or InputEventGesture) return;
        _panel.Root.AcceptEvent();
        try
        {
            if (Translate(input) is not (PadCommand command, bool pressed)) return;
            if (!pressed)
            {
                // Repeats due since the last frame still count: a slow frame rate mustn't shorten a hold.
                Repeat(command, Time.GetTicksMsec());
                _held.Remove(command);
                return;
            }
            if (_held.ContainsKey(command)) return; // a stick still pushed sends a stream of presses
            if (PadCommands.Repeats(command)) _held[command] = (Time.GetTicksMsec(), 0);
            Do(command);
        }
        catch (Exception e)
        {
            Tracker.LogError("controller input", e);
        }
    }

    /// <summary>What this event means in the recap and whether it's a press (true) or a release; null if nothing.</summary>
    private (PadCommand Command, bool Pressed)? Translate(InputEvent input)
    {
        // Game actions: keyboard shortcuts the game parsed, and anything already translated.
        foreach ((StringName action, PadCommand command) in Actions)
            if (Match(input, action) is bool pressed) return (command, pressed);
        // Raw controller buttons and sticks, through the player's own bindings.
        foreach ((StringName gameAction, StringName controllerAction) in _controllerMap)
            if (Match(input, controllerAction) is bool pressed && CommandFor(gameAction) is PadCommand command) return (command, pressed);
        foreach ((StringName action, PadCommand command) in Stick)
            if (Match(input, action) is bool pressed) return (command, pressed);
        // Keys the game binds to those actions (the view-deck key switches tabs, as on the game's own screens).
        if (input is InputEventKey key && !key.Echo && NInputManager.Instance is NInputManager manager)
            foreach ((StringName action, PadCommand command) in Actions)
                if (manager.GetCurrentHotkey(action) is Key hotkey && hotkey != Key.None && hotkey == key.Keycode) return (command, key.Pressed);
        return null;
    }

    private static PadCommand? CommandFor(StringName gameAction)
    {
        foreach ((StringName action, PadCommand command) in Actions)
            if (action == gameAction) return command;
        return null;
    }

    private static bool? Match(InputEvent input, StringName action)
    {
        if (input is InputEventAction named) return named.Action == action ? named.Pressed : null;
        if (!InputMap.HasAction(action)) return null;
        if (input.IsActionPressed(action)) return true;
        if (input.IsActionReleased(action)) return false;
        return null;
    }

    /// <summary>Held directions and scrolling repeat: after 400 ms, then every 80 ms.</summary>
    private void OnFrame()
    {
        if (_held.Count == 0) return;
        try
        {
            ulong now = Time.GetTicksMsec();
            foreach (PadCommand command in _held.Keys.ToList()) Repeat(command, now);
        }
        catch (Exception e)
        {
            _held.Clear();
            Tracker.LogError("controller repeat", e);
        }
    }

    /// <summary>Carries out the repeats of a held command that have come due by <paramref name="now"/>.</summary>
    private void Repeat(PadCommand command, ulong now)
    {
        if (!_held.TryGetValue(command, out (ulong Since, ulong Done) held)) return;
        ulong heldFor = now - held.Since;
        _held[command] = (held.Since, heldFor);
        for (int i = PadRepeat.Steps(held.Done, heldFor); i > 0 && _held.ContainsKey(command); i--) Do(command);
    }

    // ------------------------------------------------------------------ commands

    private void Do(PadCommand command)
    {
        LastCommand = command;
        if (Diagnostics) Tracker.Note($"controller: {command}");
        switch (command)
        {
            case PadCommand.TabPrevious:
            case PadCommand.TabNext:
                _panel.Tabs.CurrentTab = PadCursor.NextTab(_panel.Tabs.CurrentTab, command == PadCommand.TabNext ? 1 : -1, _panel.Tabs.GetTabCount());
                break;
            case PadCommand.Close:
                _held.Clear();
                _panel.Close();
                break;
            case PadCommand.Save:
                _panel.Save();
                break;
            case PadCommand.ScrollUp:
            case PadCommand.ScrollDown:
                CurrentPad()?.Scroll?.Invoke(command == PadCommand.ScrollDown ? 1 : -1);
                break;
            default:
                if (PadCommands.Direction(command) is PadDirection direction) Step(direction);
                break;
        }
    }

    private PadTab? CurrentPad()
    {
        int index = _panel.Tabs.CurrentTab;
        return index >= 0 && index < _panel.Pads.Count ? _panel.Pads[index] : null;
    }

    private void Step(PadDirection direction)
    {
        if (CurrentPad() is not PadTab pad) return;
        // Up and down switch rows on a tab with two; on the others they scroll its list.
        if (direction is PadDirection.Up or PadDirection.Down && pad.Rows.Count < 2)
        {
            pad.Scroll?.Invoke(direction == PadDirection.Down ? 1 : -1);
            return;
        }
        if (_cursorTab != _panel.Tabs.CurrentTab) ClearSelection();
        _cursorTab = _panel.Tabs.CurrentTab;
        int oldRow = _cursor.Row;
        if (!_cursor.Move(direction, pad.Shapes())) return;
        Show(pad, oldRow);
    }

    /// <summary>A live update changed the tab: keep the selection on the same item, or the nearest, and show it again.</summary>
    private void Refit()
    {
        if (!_cursor.Selected || _cursorTab < 0 || _cursorTab >= _panel.Pads.Count) return;
        PadTab pad = _panel.Pads[_cursorTab];
        int oldRow = _cursor.Row;
        _cursor.Fit(pad.Shapes());
        Show(pad, oldRow);
    }

    private void Show(PadTab pad, int oldRow)
    {
        if (oldRow >= 0 && oldRow != _cursor.Row && oldRow < pad.Rows.Count) pad.Rows[oldRow].Clear();
        if (_cursor.Selected && _cursor.Row < pad.Rows.Count) pad.Rows[_cursor.Row].Show(_cursor.Item);
    }

    private void ClearSelection()
    {
        if (_cursor.Selected && _cursorTab >= 0 && _cursorTab < _panel.Pads.Count && _cursor.Row < _panel.Pads[_cursorTab].Rows.Count)
            _panel.Pads[_cursorTab].Rows[_cursor.Row].Clear();
        _cursor.Clear();
        _cursorTab = -1;
    }
}
