using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The game's controller button icons on the recap (LB and RB by the tabs, B on Close, Y on Save image), shown only in
/// controller mode, in the controller's own style, and following rebinding. A missing glyph is left out.
/// </summary>
internal sealed class PadHints
{
    private readonly List<(TextureRect Rect, StringName Action)> _glyphs = new();
    private readonly List<(Button Button, StringName Action, Texture2D? MouseIcon)> _buttons = new();

    public void Glyph(TextureRect rect, StringName action) => _glyphs.Add((rect, action));

    /// <summary>In controller mode the button's icon becomes the glyph; in mouse mode it's its own again.</summary>
    public void OnButton(Button button, StringName action) => _buttons.Add((button, action, button.Icon));

    /// <summary>Shows the right hints now and follows the game's input mode and rebinding until the panel closes.</summary>
    public void Attach(Control root)
    {
        Update();
        Callable update = Callable.From(Update);
        var sources = new List<(GodotObject Source, StringName Signal)>();
        if (NControllerManager.Instance is NControllerManager controllers)
        {
            sources.Add((controllers, NControllerManager.SignalName.ControllerDetected));
            sources.Add((controllers, NControllerManager.SignalName.MouseDetected));
        }
        if (NInputManager.Instance is NInputManager input) sources.Add((input, NInputManager.SignalName.InputRebound));
        foreach ((GodotObject source, StringName signal) in sources) source.Connect(signal, update);
        root.TreeExiting += () =>
        {
            foreach ((GodotObject source, StringName signal) in sources)
                if (GodotObject.IsInstanceValid(source) && source.IsConnected(signal, update)) source.Disconnect(signal, update);
        };
    }

    private void Update()
    {
        try
        {
            bool pad = PadInput.ControllerMode;
            foreach ((TextureRect rect, StringName action) in _glyphs)
            {
                if (!GodotObject.IsInstanceValid(rect)) continue;
                rect.Texture = pad ? Icon(action) : null;
                rect.Visible = rect.Texture != null;
            }
            foreach ((Button button, StringName action, Texture2D? mouseIcon) in _buttons)
                if (GodotObject.IsInstanceValid(button)) button.Icon = (pad ? Icon(action) : null) ?? mouseIcon;
        }
        catch (Exception e)
        {
            Tracker.LogError("controller hints", e);
        }
    }

    private static Texture2D? Icon(StringName action) => NInputManager.Instance?.GetHotkeyIcon(action);
}
