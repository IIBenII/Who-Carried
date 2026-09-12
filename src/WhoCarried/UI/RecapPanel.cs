using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>What the caller needs to drive an open recap: switch views, show a status message, push live updates.</summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status, Live Live);

/// <summary>
/// The full-screen recap in the "Dealt" style: the card table, the game's top bar with the result and the run's
/// numbers, brush-underlined tabs, and seven views that update in place while it's open. Laid out at 1600×900 design
/// pixels and scaled to the screen. The dev preview captures the views by index, in this order.
/// </summary>
internal static class RecapPanel
{
    public const string IdleHint = "F8 toggles";
    public const float DesignW = 1600, DesignH = 900;
    private static readonly string[] Views = { "Scoreboard", "Awards", "Sources", "Debuffs", "Timeline", "Defense", "Decks" };

    public static PanelHandle Create(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards,
                                     Action onClose, Action<PanelHandle> onSave)
    {
        Vector2 screen = ScreenSize();
        float scale = Math.Min(screen.X / DesignW, screen.Y / DesignH);
        var k = new Kit(scale, icons);
        var live = new Live();

        var root = new Control { Name = "WhoCarried", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(Table.Backdrop(k));

        var stage = new Control
        {
            Position = new Vector2((screen.X - DesignW * scale) / 2, (screen.Y - DesignH * scale) / 2),
            Size = k.V(DesignW, DesignH), MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(stage);

        Label status = k.Text(IdleHint, 15, RecapTheme.Faint);
        var tabs = new TabContainer { TabsVisible = false, Size = stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        tabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        stage.AddChild(tabs);
        var handle = new PanelHandle(root, tabs, status, live);

        tabs.AddChild(Safe(k, 0, () => ScoreboardTab.Create(k, view, live, deal: true)));
        tabs.AddChild(Safe(k, 1, () => AwardsTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 2, () => SourcesTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 3, () => DebuffsTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 4, () => TimelineTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 5, () => DefenseTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 6, () => DecksTab.Create(k, view, cards, live)));

        root.AddChild(TopBar(k, view, status, onClose, () => onSave(handle), live, screen.X, stage.Position));
        stage.AddChild(Nav(k, tabs));
        return handle;
    }

    private static Control Named(Control view, int index)
    {
        view.Name = Views[index];
        return view;
    }

    /// <summary>One view; if it fails to build, a note in its place so the rest of the recap still opens.</summary>
    private static Control Safe(Kit k, int index, Func<Control> build)
    {
        try
        {
            return Named(build(), index);
        }
        catch (Exception e)
        {
            Tracker.LogError($"recap view {Views[index]}", e);
            Control failed = k.Box(DesignW, DesignH);
            failed.AddChild(k.At(k.Text("This view couldn't be drawn this time. The details are in the mod's log.", 18, RecapTheme.Muted), 40, 160));
            return Named(failed, index);
        }
    }

    /// <summary>The visible screen in UI units (the game scales its canvas; this is the size our layout sees).</summary>
    private static Vector2 ScreenSize()
    {
        try { return ((SceneTree)Engine.GetMainLoop()).Root.GetVisibleRect().Size; }
        catch (Exception) { return new Vector2(1920, 1080); }
    }

    /// <summary>
    /// The game's top bar across the screen: "Who Carried? · Victory", floor, time, ascension and team damage with the
    /// game's icons, the party, then status, Save image and Close.
    /// </summary>
    private static Control TopBar(Kit k, RecapView view, Label status, Action onClose, Action onSave, Live live, float screenWidth,
                                  Vector2 stage)
    {
        var bar = new Control { Size = new Vector2(screenWidth, k.U(74) + stage.Y), MouseFilter = Control.MouseFilterEnum.Ignore };
        TextureRect art = k.Stretch(GameArt.Get(GameArt.TopBar), 0, 0);
        art.Position = Vector2.Zero;
        art.Size = new Vector2(screenWidth, stage.Y + k.U(74));
        if (art.Texture == null)
        {
            var plain = new ColorRect { Color = new Color("1b2635"), Size = art.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            bar.AddChild(plain);
        }
        bar.AddChild(art);

        HBoxContainer row = k.Row(28);
        row.Position = new Vector2(stage.X + k.U(30), stage.Y);
        row.Size = new Vector2(screenWidth - 2 * (stage.X + k.U(30)), k.U(68));
        bar.AddChild(row);

        HBoxContainer title = k.Row(0);
        title.AddChild(Kit.Center(k.Strong(RecapTexts.ModName + " · ", 30)));
        Label result = k.Strong("", 30);
        title.AddChild(Kit.Center(result));
        row.AddChild(Kit.Center(title));

        (Control floor, Label floorText) = Stat(k, GameArt.Get(GameArt.Floor));
        (Control time, Label timeText) = Stat(k, GameArt.Get(GameArt.Timer));
        (Control ascension, Label ascensionText) = Stat(k, GameArt.Get(GameArt.Ascension));
        (Control team, Label teamText) = Stat(k, GameArt.Get(GameArt.Swords));
        foreach (Control stat in new[] { floor, time, ascension, team }) row.AddChild(Kit.Center(stat));
        HBoxContainer party = k.Row(-8);
        row.AddChild(Kit.Center(party));
        row.AddChild(Kit.Fill());
        row.AddChild(Kit.Center(status));
        Button save = BarButton(k, "Save image", GameArt.Get(GameArt.Share));
        save.Pressed += onSave;
        row.AddChild(Kit.Center(save));
        Button close = BarButton(k, "Close", null);
        close.Pressed += onClose;
        row.AddChild(Kit.Center(close));

        string partyShown = "";
        void Apply(RecapView v)
        {
            (string text, Color color) = RecapTexts.Result(v);
            result.Text = text;
            result.AddThemeColorOverride("font_color", color);
            int f = RecapTexts.Floor(v);
            floorText.Text = f.ToString();
            floor.Visible = f > 0;
            string duration = RecapTexts.Duration(v.Facts?.Seconds ?? 0);
            timeText.Text = duration;
            time.Visible = duration.Length > 0;
            ascensionText.Text = (v.Facts?.Ascension ?? 0).ToString();
            ascension.Visible = (v.Facts?.Ascension ?? 0) > 0;
            teamText.Text = Kit.Num(RecapTexts.TeamDamage(v));
            string signature = string.Join(",", ScoreboardTab.Players(v).Select(p => p.IconKey + p.ColorHex));
            if (signature == partyShown) return;
            partyShown = signature;
            foreach (Node child in party.GetChildren())
            {
                party.RemoveChild(child);
                child.QueueFree();
            }
            foreach (BarRow p in ScoreboardTab.Players(v)) party.AddChild(Kit.Center(Coin(k, p.IconKey, RecapTheme.Accent(p.ColorHex), 38)));
        }
        Apply(view);
        live.On(Apply);
        return bar;
    }

    private static (Control, Label) Stat(Kit k, Texture2D? icon)
    {
        HBoxContainer stat = k.Row(7);
        if (icon != null) stat.AddChild(Kit.Center(k.Pic(icon, 34, 34)));
        Label text = k.Text("", 24, RecapTheme.Text, true, Ink.Soft);
        stat.AddChild(Kit.Center(text));
        return (stat, text);
    }

    /// <summary>A character icon in a dark coin ringed with the player's colour (the party in the top bar).</summary>
    public static Control Coin(Kit k, string? iconKey, Color ring, float size)
    {
        var coin = new Panel { CustomMinimumSize = k.V(size, size), Size = k.V(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat box = RecapTheme.Box(RecapTheme.Ink, k.U(size / 2), ring, k.U(2));
        coin.AddThemeStyleboxOverride("panel", box);
        if (k.Icon(iconKey) is Texture2D icon) coin.AddChild(k.At(k.Pic(icon, size * 0.84f, size * 0.84f), size * 0.08f, size * 0.08f));
        return coin;
    }

    private static Button BarButton(Kit k, string text, Texture2D? icon)
    {
        var button = new Button { Text = text, Icon = icon, FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        if (RecapTheme.Bold is Font font) button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", k.F(18));
        button.AddThemeConstantOverride("icon_max_width", k.F(20));
        button.AddThemeConstantOverride("h_separation", k.F(7));
        button.AddThemeConstantOverride("outline_size", k.F(3));
        button.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(state, RecapTheme.Text);
        StyleBoxFlat Box(float alpha, float edge) =>
            RecapTheme.Box(new Color(0, 0, 0, alpha), k.U(6), new Color(1, 1, 1, edge), k.U(1), k.U(14), k.U(6));
        button.AddThemeStyleboxOverride("normal", Box(0.35f, 0.14f));
        button.AddThemeStyleboxOverride("hover", Box(0.55f, 0.35f));
        button.AddThemeStyleboxOverride("pressed", Box(0.7f, 0.5f));
        button.AddThemeStyleboxOverride("hover_pressed", Box(0.7f, 0.5f));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    /// <summary>
    /// The tabs: plain words; the chosen one is white with a gold brush stroke under it, painted in left to right
    /// each time a tab is chosen. Stays in sync when the view is switched from code (dev preview).
    /// </summary>
    private static Control Nav(Kit k, TabContainer tabs)
    {
        // Where the tabs sit is shared with the scoreboard, whose cards keep clear of them (HandLayout).
        const float top = HandLayout.TabsTop;
        Control nav = k.At(k.Box(1520, 50), 40, top);
        nav.AddChild(k.At(new ColorRect { Color = RecapTheme.Line, MouseFilter = Control.MouseFilterEnum.Ignore }, 0,
            HandLayout.TabLine - top, 1520, 1));
        // The stroke sits in a clipping box that grows from nothing, so it looks painted on.
        var stroke = new Control { ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        TextureRect brush = k.Stretch(GameArt.Get(GameArt.Brush), 0, 16, RecapTheme.Gold);
        stroke.AddChild(brush);
        nav.AddChild(stroke);
        Tween? painting = null;
        var buttons = new List<(Button Button, float X, float Width)>();
        float x = 0;
        for (int i = 0; i < Views.Length; i++)
        {
            int index = i;
            var tab = new Button { Text = Views[i], Flat = true, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop };
            if (RecapTheme.Bold is Font font) tab.AddThemeFontOverride("font", font);
            tab.AddThemeFontSizeOverride("font_size", k.F(19));
            tab.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
                tab.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            float width = (RecapTheme.Bold?.GetStringSize(Views[i], HorizontalAlignment.Left, -1, k.F(19)).X ?? Views[i].Length * k.U(10)) / k.S;
            tab.Position = k.V(x, 0);
            tab.Size = k.V(width, HandLayout.TabsBottom - top);
            tab.Pressed += () => tabs.CurrentTab = index;
            nav.AddChild(tab);
            buttons.Add((tab, x, width));
            x += width + 30;
        }

        void Mark(int active, bool animate)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Button b = buttons[i].Button;
                bool on = i == active;
                b.AddThemeColorOverride("font_color", on ? RecapTheme.Text : RecapTheme.Muted);
                b.AddThemeColorOverride("font_hover_color", RecapTheme.Text);
                b.AddThemeColorOverride("font_pressed_color", RecapTheme.Text);
                b.AddThemeConstantOverride("outline_size", on ? k.F(3) : 0);
            }
            if (active < 0 || active >= buttons.Count) return;
            (_, float bx, float bw) = buttons[active];
            Vector2 size = k.V(bw + 16, 16);
            brush.Position = Vector2.Zero;
            brush.Size = size;
            stroke.Position = k.V(bx - 8, 28);
            painting?.Kill();
            if (animate && stroke.IsInsideTree())
            {
                stroke.Size = new Vector2(0, size.Y);
                painting = stroke.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
                painting.TweenProperty(stroke, "size", size, 0.28);
            }
            else
            {
                stroke.Size = size;
            }
        }
        Mark(0, animate: false);
        tabs.TabChanged += index => Mark((int)index, animate: true);
        return nav;
    }
}

/// <summary>The card table behind everything: a blue-grey spotlight fading to near black at the edges.</summary>
internal static class Table
{
    public static Control Backdrop(Kit k)
    {
        var table = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        table.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var fill = new ColorRect { Color = RecapTheme.TableDark, MouseFilter = Control.MouseFilterEnum.Ignore };
        fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        table.AddChild(fill);
        table.AddChild(Radial(new[] { (0f, RecapTheme.TableLight), (0.55f, RecapTheme.Table), (1f, RecapTheme.TableDark) },
            new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f) + new Vector2(0.62f, 0.62f)));
        table.AddChild(Radial(new[] { (0f, new Color(80 / 255f, 110 / 255f, 150 / 255f, 0.3f)), (1f, new Color(80 / 255f, 110 / 255f, 150 / 255f, 0)) },
            new Vector2(0.36f, 0.5f), new Vector2(0.36f, 0.5f) + new Vector2(0.36f, 0.3f)));
        return table;
    }

    private static TextureRect Radial((float Offset, Color Color)[] stops, Vector2 from, Vector2 to)
    {
        var gradient = new Gradient
        {
            Offsets = stops.Select(s => s.Offset).ToArray(),
            Colors = stops.Select(s => s.Color).ToArray(),
        };
        var rect = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = from, FillTo = to, Width = 256, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }
}
