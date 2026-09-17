using Godot;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The press-a-key prompt behind the recap's hotkey button. It owns its own GUI listener so it still works if
/// controller support cannot attach to the panel.
/// </summary>
internal static class HotkeyRebind
{
    private static PanelHandle? _panel;
    private static bool _listening;
    private static Key _swallowedKey = Key.None;

    /// <summary>Whether capture owns this event, including echoes and release of the captured key.</summary>
    public static bool Consumes(InputEvent input) => _listening ||
        input is InputEventKey key && key.Keycode == _swallowedKey;

    /// <summary>Attaches the prompt to a newly opened recap.</summary>
    public static void Attach(PanelHandle panel)
    {
        Forget();
        _panel = panel;
        try
        {
            panel.Hotkey.Cap.Pressed += Start;
            panel.Root.GuiInput += OnKey;
            panel.Root.FocusExited += () => Stop(panel);
            panel.Root.TreeExiting += () => Forget(panel);
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind (the key still works)", e);
            Forget();
        }
    }

    /// <summary>Starts listening for the next key press.</summary>
    public static void Start()
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel.Root)) return;
        _listening = true;
        ShowListening();
        _panel.Root.FocusMode = Control.FocusModeEnum.All;
        if (_panel.Root.IsInsideTree()) _panel.Root.GrabFocus();
    }

    private static void OnKey(InputEvent input)
    {
        try
        {
            if (input is InputEventKey swallowed && swallowed.Keycode == _swallowedKey)
            {
                Accept();
                if (!swallowed.Pressed) _swallowedKey = Key.None;
                return;
            }
            if (!_listening || input is not InputEventKey { Pressed: true, Echo: false } key) return;

            _swallowedKey = key.Keycode;
            Accept();
            switch (key.Keycode)
            {
                case Key.Escape:
                    Stop();
                    return;
                case Key.Delete or Key.Backspace:
                    HotkeyBinding.Set(Key.None);
                    Stop();
                    return;
                case Key.Shift or Key.Ctrl or Key.Alt or Key.Meta:
                    return;
                default:
                    HotkeyBinding.Set(key.Keycode);
                    Stop();
                    return;
            }
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind", e);
            Stop();
        }
    }

    private static void Stop(PanelHandle? expected = null)
    {
        if (expected != null && !ReferenceEquals(_panel, expected)) return;
        _listening = false;
        Idle();
    }

    /// <summary>The cap empties and the line asks for a key, with the two ways out spelled out beside it.</summary>
    private static void ShowListening()
    {
        if (!Ready()) return;
        _panel!.Hotkey.Look(HewnStone.CapLook.Listening);
        _panel.Hotkey.Cap.Text = "?";
        _panel.Hotkey.Text.Text = "press a key…";
        _panel.Hotkey.Hint.Text = "esc cancels · del clears";
    }

    /// <summary>The cap carries the key again — or says there isn't one, which the podium button makes survivable.</summary>
    private static void Idle()
    {
        if (!Ready()) return;
        string? name = HotkeyBinding.Name;
        _panel!.Hotkey.Look(name == null ? HewnStone.CapLook.Unbound : HewnStone.CapLook.Bound);
        _panel.Hotkey.Cap.Text = name ?? "—";
        _panel.Hotkey.Text.Text = name == null ? "no key opens the recap" : "toggles the recap";
        _panel.Hotkey.Hint.Text = "";
    }

    /// <summary>A panel whose line is still alive to write to.</summary>
    private static bool Ready() => _panel != null && GodotObject.IsInstanceValid(_panel.Hotkey.Cap)
                                                  && GodotObject.IsInstanceValid(_panel.Hotkey.Text);

    private static void Forget(PanelHandle? expected = null)
    {
        if (expected != null && !ReferenceEquals(_panel, expected)) return;
        _listening = false;
        _swallowedKey = Key.None;
        _panel = null;
    }

    private static void Accept()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel.Root)) _panel.Root.AcceptEvent();
    }
}
