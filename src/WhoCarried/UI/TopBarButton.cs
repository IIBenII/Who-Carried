using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The podium button in the game's top bar, just left of the Map button: click to open the recap (or reach it with the
/// controller's top-bar navigation and press A), hover or select it for the game's tooltip. It grows and brightens like
/// the game's own top-bar buttons, but stays straight.
/// </summary>
internal static class TopBarButton
{
    public const string NodeName = "WhoCarriedTopBarButton";
    public static string Title => HotkeyBinding.Name is string key ? $"{RecapTexts.ModName} ({key})" : RecapTexts.ModName;
    public const string Description = "View everyone's damage, defense and awards for this run.";

    private static Texture2D? _icon;
    private static bool _iconFailed, _tipFailed;

    /// <summary>
    /// Adds the button to this top bar, just before its Map button, once. Returns the button (or the one already there),
    /// or null if the icon can't be drawn.
    /// </summary>
    public static Control? AddTo(NTopBar bar)
    {
        Control map = bar.Map;
        if (map.GetParent() is not Node row) return null;
        if (row.GetNodeOrNull<Control>(NodeName) is Control existing) return existing;
        if (Icon() is not Texture2D icon) return null;
        Control button = Build(icon);
        row.AddChild(button);
        row.MoveChild(button, map.GetIndex());
        Tracker.Note("top bar: recap button added");
        JoinNavigation(bar);
        return button;
    }

    private static bool _navigationFailed;

    /// <summary>
    /// Puts the podium at the right-hand end of the top bar's controller navigation, after the game has linked the
    /// rest (it relinks whenever potion slots, modifiers or the screen change). Left goes back to where the chain
    /// ended, down to the relics like the other top-bar items.
    /// </summary>
    public static void JoinNavigation(NTopBar bar)
    {
        if (_navigationFailed) return;
        try
        {
            if (bar.Map.GetParent()?.GetNodeOrNull<Control>(NodeName) is not Control podium) return;
            NodePath below = bar.Hp.FocusNeighborBottom;
            if (below.IsEmpty) return; // not linked yet (no relics on screen)
            Control end = ChainEnd(bar);
            end.FocusNeighborRight = podium.GetPath();
            podium.FocusNeighborLeft = end.GetPath();
            podium.FocusNeighborRight = podium.GetPath();
            podium.FocusNeighborTop = podium.GetPath();
            podium.FocusNeighborBottom = below;
        }
        catch (Exception e)
        {
            _navigationFailed = true;
            Tracker.LogError("top bar navigation (the podium stays mouse-only)", e);
        }
    }

    /// <summary>The last item in the game's chain: the last modifier, else the boss icon if it can take focus, else the floor.</summary>
    private static Control ChainEnd(NTopBar bar)
    {
        if (Traverse.Create(bar).Field<Control>("_modifiersContainer").Value is Control modifiers && modifiers.Visible
            && modifiers.GetChildren().OfType<Control>().LastOrDefault() is Control last)
            return last;
        return bar.BossIcon.IsVisible() && bar.BossIcon.FocusMode != Control.FocusModeEnum.None ? bar.BossIcon : bar.FloorIcon;
    }

    private static Control Build(Texture2D icon)
    {
        var button = new Control
        {
            Name = NodeName,
            CustomMinimumSize = new Vector2(TopBarIconArt.Slot, TopBarIconArt.Slot),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
        };
        // Keep-aspect in the Map icon's box; the texture has no mipmaps, so the game shrinks it like its own icons.
        var art = new TextureRect
        {
            Texture = icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2((TopBarIconArt.Slot - TopBarIconArt.Width) / 2 - TopBarIconArt.NudgeLeft, TopBarIconArt.BoxTop),
            Size = new Vector2(TopBarIconArt.Width, TopBarIconArt.Height),
            PivotOffset = new Vector2(TopBarIconArt.Width / 2f, TopBarIconArt.Height / 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        button.AddChild(art);
        var behaviour = new Behaviour(button, art);
        button.MouseEntered += behaviour.Enter;
        button.MouseExited += behaviour.Leave;
        button.GuiInput += behaviour.Input;
        button.TreeExiting += behaviour.Gone;
        // Controller: the top bar's d-pad navigation reaches it (JoinNavigation); focused looks like hovered.
        button.FocusEntered += () => { if (PadInput.ControllerMode) behaviour.Enter(); };
        button.FocusExited += behaviour.Leave;
        return button;
    }

    /// <summary>Hover, press and click for one button.</summary>
    private sealed class Behaviour(Control button, TextureRect art)
    {
        private Tween? _tween;
        private bool _pressed;

        public void Enter()
        {
            Set(TopBarIconArt.HoverGrow, _pressed ? TopBarIconArt.PressDim : TopBarIconArt.HoverBright);
            ShowTip(button);
        }

        public void Leave()
        {
            Settle();
            HideTip(button);
        }

        public void Input(InputEvent input)
        {
            if (input.IsActionPressed(MegaInput.select))
            {
                button.AcceptEvent();
                Leave();
                RecapUi.Show();
                return;
            }
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
            button.AcceptEvent();
            if (click.Pressed)
            {
                _pressed = true;
                Set(TopBarIconArt.HoverGrow, TopBarIconArt.PressDim);
                return;
            }
            if (!_pressed) return;
            _pressed = false;
            Leave();
            // The release comes here even off the button; only a release over it counts as a click.
            if (new Rect2(Vector2.Zero, button.Size).HasPoint(click.Position)) RecapUi.Show();
        }

        public void Gone()
        {
            _tween?.Kill();
            HideTip(button);
        }

        private void Set(float scale, float bright)
        {
            _tween?.Kill();
            art.Scale = Vector2.One * scale;
            art.Modulate = new Color(bright, bright, bright);
        }

        /// <summary>Back to rest, easing out over a second like the game's buttons.</summary>
        private void Settle()
        {
            _tween?.Kill();
            if (!button.IsInsideTree()) return;
            _tween = button.CreateTween().SetParallel();
            _tween.TweenProperty(art, "scale", Vector2.One, TopBarIconArt.SettleSeconds)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
            _tween.TweenProperty(art, "modulate", Colors.White, TopBarIconArt.SettleSeconds)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        }
    }

    // ------------------------------------------------------------------ tooltip

    private const string OwnTipName = "WhoCarriedTopBarTip";

    /// <summary>The game's hover tip, under the button with right edges aligned like the Deck's.</summary>
    private static void ShowTip(Control owner)
    {
        HideTip(owner);
        if (!_tipFailed)
        {
            try
            {
                // Null means the game is holding tips back right now; that's not a failure.
                if (NHoverTipSet.CreateAndShow(owner, GameTip()) is NHoverTipSet set)
                    set.GlobalPosition = owner.GlobalPosition + new Vector2(owner.Size.X - set.Size.X, owner.Size.Y + 20f);
                return;
            }
            catch (Exception e)
            {
                _tipFailed = true;
                Tracker.LogError("top bar tooltip", e);
            }
        }
        ShowOwnTip(owner);
    }

    private static void HideTip(Control owner)
    {
        try { NHoverTipSet.Remove(owner); }
        catch (Exception) { }
        owner.GetNodeOrNull(OwnTipName)?.QueueFree();
    }

    /// <summary>
    /// The game's tips take their title from its own text tables, which don't have ours: start from one of its plain
    /// lines and put our words in.
    /// </summary>
    private static IHoverTip GameTip()
    {
        object tip = new HoverTip(new LocString("static_hover_tips", "DECK.description"));
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(tip, Title);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(tip, Description);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Id)).SetValue(tip, NodeName);
        return (IHoverTip)tip;
    }

    /// <summary>Fallback when the game's tip can't be made: the same words in a small panel of our own.</summary>
    private static void ShowOwnTip(Control owner)
    {
        var k = new Kit(1f, _ => null);
        var panel = new PanelContainer { Name = OwnTipName, TopLevel = true, ZIndex = 100, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = RecapTheme.Tip, BorderColor = RecapTheme.TipEdge,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddChild(k.Strong(Title, 24));
        lines.AddChild(k.Text(Description, 20, RecapTheme.Text));
        panel.AddChild(lines);
        owner.AddChild(panel);
        panel.ResetSize();
        panel.GlobalPosition = owner.GlobalPosition + new Vector2(owner.Size.X - panel.Size.X, owner.Size.Y + 20f);
    }

    // ------------------------------------------------------------------ icon

    /// <summary>The podium, built once: drawn big by the game's SVG renderer, then made hard-edged like the game's art.</summary>
    private static Texture2D? Icon()
    {
        if (_icon != null && GodotObject.IsInstanceValid(_icon)) return _icon;
        if (_iconFailed) return null;
        try
        {
            var big = new Image();
            Error error = big.LoadSvgFromString(ReadSvg(), TopBarIconArt.RenderScale);
            if (error != Error.Ok) throw new InvalidOperationException($"the SVG didn't draw: {error}");
            big.Convert(Image.Format.Rgba8);
            HardPixels.Result art = HardPixels.Majority(big.GetData(), big.GetWidth(), big.GetHeight(), TopBarIconArt.Block);
            _icon = ImageTexture.CreateFromImage(Image.CreateFromData(art.Width, art.Height, false, Image.Format.Rgba8, art.Pixels));
            return _icon;
        }
        catch (Exception e)
        {
            _iconFailed = true;
            Tracker.LogError("top bar icon (F8 still works)", e);
            return null;
        }
    }

    private static string ReadSvg()
    {
        using Stream stream = typeof(TopBarButton).Assembly.GetManifestResourceStream(TopBarIconArt.SvgResource)
            ?? throw new InvalidOperationException($"missing embedded {TopBarIconArt.SvgResource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
