using Godot;
using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>
/// Each player as a combat nameplate: their portrait, the block shield with what they blocked, an HP-style bar of
/// damage taken and healed, and what their debuffs kept off the team. Then the team's totals and what the numbers mean.
/// </summary>
internal static class DefenseTab
{
    public static Control Create(Kit k, RecapView view, Live live)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        int n = Math.Max(1, view.Defense.Count);
        // Two plates a row; five or more players (modded lobbies) get shorter plates so everything still fits.
        int rows = (n + 1) / 2;
        float plateH = rows <= 2 ? 262 : Math.Max(150, (560 - 24 * (rows - 1)) / rows);
        tab.AddChild(k.At(Plates(k, view, 1522, 2, plateH, live), 40, 146));
        float stripY = 146 + rows * plateH + (rows - 1) * 24 + 26;
        tab.AddChild(k.At(TeamStrip(k, view, 1522, live), 40, stripY, 1522, -1));

        Label note = k.Text("", 15, RecapTheme.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(k.At(note, 40, stripY + 84, 1522, -1));
        void Note(RecapView v) => note.Text = "The shield is damage your own block soaked up." +
            (v.Defense.Any(r => r.PetTanked > 0) ? " \"Tanked by pets\" is HP your pets lost to enemies, often in your place." : "") +
            (v.PreventedNote.Length > 0 ? " " + v.PreventedNote : "");
        Note(view);
        live.On(Note);
        return tab;
    }

    /// <summary>The nameplates in a grid, on one shared bar scale.</summary>
    public static Control Plates(Kit k, RecapView view, float width, int columns, float height, Live? live, bool compact = false)
    {
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", k.F(compact ? 18 : 30));
        grid.AddThemeConstantOverride("v_separation", k.F(compact ? 14 : 24));
        float plateW = (width - (compact ? 18 : 30) * (columns - 1)) / columns;
        Label empty = k.Text("No defense data yet.", 16, RecapTheme.Muted);
        grid.AddChild(empty);

        static int Scale(RecapView v) => Math.Max(1, v.Defense.Select(r => r.Taken + r.Healed).DefaultIfEmpty(0).Max());
        var plates = new KeyedRows<PlateItem>(grid, p => p.Row.Label,
            p => compact ? Compact(k, p, plateW, height) : Plate(k, p, plateW, height), offset: 1);
        void Sync(RecapView v)
        {
            empty.Visible = v.Defense.Count == 0;
            int max = Scale(v);
            bool prevented = v.Defense.Any(r => r.Prevented > 0);
            bool pets = v.Defense.Any(r => r.PetTanked > 0);
            plates.Sync(InRankOrder(v).Select(r => new PlateItem(r, max, prevented, pets)));
        }
        Sync(view);
        live?.On(Sync);
        return grid;
    }

    /// <summary>One plate's row, the shared bar scale, and whether anyone has prevention or pet tanking to show.</summary>
    private readonly record struct PlateItem(DefenseRow Row, int Max, bool Prevented, bool Pets);

    /// <summary>What a pet losing HP in its owner's place is shown with: Osty's own "Die for You".</summary>
    private const string PetIcon = DebuffBuilder.IconPrefix + "DIE_FOR_YOU_POWER";

    /// <summary>Players in scoreboard order, like every other tab.</summary>
    private static IEnumerable<DefenseRow> InRankOrder(RecapView v)
    {
        List<string> ranked = ScoreboardTab.Players(v).Select(p => p.Label).ToList();
        return v.Defense.OrderBy(r => ranked.IndexOf(r.Label) is int i && i >= 0 ? i : 99);
    }

    private static (Control, Action<PlateItem>) Plate(Kit k, PlateItem item, float width, float height)
    {
        DefenseRow row = item.Row;
        Color color = RecapTheme.FromHex(row.ColorHex), accent = RecapTheme.Accent(row.ColorHex);
        PanelContainer tip = k.Tip(24, 20, new Color(accent, 0.47f));
        tip.CustomMinimumSize = k.V(width, height);
        HBoxContainer line = k.Row(24);
        tip.AddChild(line);

        float portraitH = height - 42, portraitW = Math.Min(170, portraitH * 0.78f);
        line.AddChild(Kit.Center(Portrait(k, row.IconKey, color, portraitW, portraitH)));

        VBoxContainer right = k.Column(0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(right);
        Label name = k.Text(row.Label, 34, accent, true, Ink.Strong);
        k.Fit(name, width - portraitW - 96, 20);
        right.AddChild(name);
        right.AddChild(k.Text(row.Character, 16, RecapTheme.Muted));

        float barW = width - 48 - portraitW - 24 - 78 - 12;
        HBoxContainer middle = k.Row(12);
        (Control shield, Label shieldText) = Shield(k, 78, 22);
        middle.AddChild(Kit.Center(shield));
        VBoxContainer barBox = k.Column(6);
        var bar = new HpBar(k, barW, 30);
        barBox.AddChild(bar.Control);
        HBoxContainer under = k.Row(0);
        (Control takenLine, LiveNumber taken) = Amount(k, RecapTheme.Taken, " taken");
        under.AddChild(takenLine);
        under.AddChild(Kit.Fill());
        (Control healedLine, LiveNumber healed) = Amount(k, RecapTheme.Healed, " healed");
        under.AddChild(healedLine);
        under.CustomMinimumSize = k.V(barW, 0);
        barBox.AddChild(under);
        middle.AddChild(Kit.Center(barBox));
        right.AddChild(Pad(k, middle, 16));

        HBoxContainer facts = k.Row(22);
        (Control blockedLine, Label blocked) = Fact(k, GameArt.Get(GameArt.Block), RecapTheme.Blocked, " blocked");
        facts.AddChild(blockedLine);
        (Control keptLine, Label kept) = Fact(k, k.Icon(DebuffBuilder.IconPrefix + "WEAK_POWER"), RecapTheme.Teal, " kept off the team");
        facts.AddChild(keptLine);
        (Control petLine, Label pet) = Fact(k, k.Icon(PetIcon), RecapTheme.Blocked, " tanked by pets");
        facts.AddChild(petLine);
        right.AddChild(Pad(k, facts, 14));

        void Apply(PlateItem it)
        {
            DefenseRow r = it.Row;
            shieldText.Text = Kit.Num(r.Blocked);
            bar.Set((double)r.Taken / it.Max, (double)r.Healed / it.Max);
            taken.Set(r.Taken);
            healed.Set(r.Healed);
            blocked.Text = Kit.Num(r.Blocked);
            kept.Text = Kit.Num(r.Prevented);
            keptLine.Visible = it.Prevented;
            pet.Text = Kit.Num(r.PetTanked);
            petLine.Visible = it.Pets;
        }
        Apply(item);
        return (tip, Apply);
    }

    /// <summary>The saved image's smaller plate: portrait, shield and the bar with its numbers under it.</summary>
    private static (Control, Action<PlateItem>) Compact(Kit k, PlateItem item, float width, float height)
    {
        DefenseRow row = item.Row;
        Color color = RecapTheme.FromHex(row.ColorHex), accent = RecapTheme.Accent(row.ColorHex);
        PanelContainer tip = k.Tip(10, 8, new Color(accent, 0.47f));
        tip.CustomMinimumSize = k.V(width, height);
        HBoxContainer line = k.Row(12);
        tip.AddChild(line);
        line.AddChild(Kit.Center(Portrait(k, row.IconKey, color, 66, 84)));
        VBoxContainer right = k.Column(4);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(Kit.Center(right));
        right.AddChild(k.Text(row.Label, 20, accent, true, Ink.Soft));
        HBoxContainer middle = k.Row(10);
        (Control shield, Label shieldText) = Shield(k, 38, 13);
        middle.AddChild(Kit.Center(shield));
        float barW = width - 20 - 66 - 12 - 38 - 10;
        VBoxContainer barBox = k.Column(4);
        var bar = new HpBar(k, barW, 16);
        barBox.AddChild(bar.Control);
        HBoxContainer under = k.Row(0);
        under.CustomMinimumSize = k.V(barW, 0);
        (Control takenLine, LiveNumber taken) = Amount(k, RecapTheme.Taken, " taken", 13, 11);
        under.AddChild(takenLine);
        under.AddChild(Kit.Fill());
        (Control keptLine, LiveNumber kept) = Amount(k, RecapTheme.Teal, " kept off", 13, 11);
        under.AddChild(keptLine);
        under.AddChild(Kit.Fill());
        (Control petLine, LiveNumber pet) = Amount(k, RecapTheme.Blocked, " tanked by pets", 13, 11);
        under.AddChild(petLine);
        under.AddChild(Kit.Fill());
        (Control healedLine, LiveNumber healed) = Amount(k, RecapTheme.Healed, " healed", 13, 11);
        under.AddChild(healedLine);
        barBox.AddChild(under);
        middle.AddChild(Kit.Center(barBox));
        right.AddChild(middle);

        void Apply(PlateItem it)
        {
            DefenseRow r = it.Row;
            shieldText.Text = Kit.Num(r.Blocked);
            bar.Set((double)r.Taken / it.Max, (double)r.Healed / it.Max);
            taken.Set(r.Taken);
            healed.Set(r.Healed);
            kept.Set(r.Prevented);
            keptLine.Visible = it.Prevented;
            pet.Set(r.PetTanked);
            petLine.Visible = it.Pets;
        }
        Apply(item);
        return (tip, Apply);
    }

    /// <summary>The character's portrait with rounded corners and a ring of their colour.</summary>
    public static Control Portrait(Kit k, string? characterId, Color ring, float width, float height)
    {
        Control holder = k.Box(width, height);
        var clip = new Panel { Size = k.V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
        StyleBoxFlat mask = RecapTheme.Box(RecapTheme.Inset, k.U(10));
        mask.ShadowColor = new Color(0, 0, 0, 0.5f);
        mask.ShadowSize = k.F(12);
        mask.ShadowOffset = new Vector2(0, k.U(6));
        clip.AddThemeStyleboxOverride("panel", mask);
        if (k.Icon(RecapTexts.PortraitKey(characterId)) is Texture2D portrait)
        {
            clip.AddChild(k.Cover(portrait, width, height, 0.25f));
        }
        else
        {
            // A modded character without select-screen art: its icon on a glow of its colour.
            clip.AddChild(k.Glow(ring.Lerp(Colors.White, 0.1f), ring.Lerp(new Color("0b0f16"), 0.65f), width, height));
            float icon = Math.Min(width, height) * 0.6f;
            clip.AddChild(k.At(k.Pic(k.Icon(characterId), icon, icon), (width - icon) / 2, (height - icon) / 2));
        }
        holder.AddChild(clip);
        var edge = new Panel { Size = k.V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        StyleBoxFlat border = RecapTheme.Box(RecapTheme.Clear, k.U(10), ring, k.U(3));
        border.SetExpandMarginAll(k.U(3));
        border.DrawCenter = false;
        edge.AddThemeStyleboxOverride("panel", border);
        holder.AddChild(edge);
        return holder;
    }

    /// <summary>The combat block shield with a number on it.</summary>
    private static (Control, Label) Shield(Kit k, float size, float text)
    {
        Control shield = k.Box(size, size);
        shield.AddChild(k.Pic(GameArt.Get(GameArt.Block), size, size));
        Label number = k.Strong("", text);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        shield.AddChild(k.At(number, 0, -size * 0.04f, size, size));
        return (shield, number);
    }

    /// <summary>"289 taken": the number big and coloured, the word after it.</summary>
    private static (Control, LiveNumber) Amount(Kit k, Color tone, string word, float size = 20, float wordSize = 15)
    {
        HBoxContainer line = k.Row(0);
        var number = new LiveNumber(k.Text("", size, tone, true, Ink.Soft), 0);
        line.AddChild(Kit.Center(number.Control));
        Label after = k.Text(word, wordSize, RecapTheme.Text);
        after.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        line.AddChild(after);
        return (line, number);
    }

    /// <summary>"🛡 573 blocked": an icon, the number in its colour, the words.</summary>
    private static (Control, Label) Fact(Kit k, Texture2D? icon, Color tone, string words)
    {
        HBoxContainer line = k.Row(6);
        if (icon != null) line.AddChild(Kit.Center(k.Pic(icon, 22, 22)));
        Label number = k.Text("", 15, tone, true);
        line.AddChild(Kit.Center(number));
        line.AddChild(Kit.Center(k.Text(words.TrimStart(), 15, new Color("dfe4ea"))));
        return (line, number);
    }

    private static MarginContainer Pad(Kit k, Control child, float top)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_top", k.F(top));
        margin.AddChild(child);
        return margin;
    }

    /// <summary>"The team": everyone's damage taken, blocked, healed and kept off, added up.</summary>
    public static Control TeamStrip(Kit k, RecapView view, float width, Live? live, float scale = 1)
    {
        PanelContainer tip = k.Tip(24 * scale, 14 * scale);
        tip.CustomMinimumSize = k.V(width, 0);
        HBoxContainer row = k.Row(40 * scale);
        tip.AddChild(row);
        row.AddChild(Kit.Center(k.Text(view.Defense.Count == 1 ? "The run" : "The team", 20 * scale, RecapTheme.Gold, true, Ink.Soft)));
        Texture2D? heal = k.Icon(DebuffBuilder.IconPrefix + "REGEN_POWER") ?? GameArt.Get(GameArt.Heart);
        // The last two only show once someone has some.
        var items = new (Texture2D? Icon, Color Tone, string Words, Func<DefenseRow, int> Value, bool Always)[]
        {
            (GameArt.Get(GameArt.Heart), RecapTheme.Taken, "damage taken", r => r.Taken, true),
            (GameArt.Get(GameArt.Block), RecapTheme.Blocked, "blocked", r => r.Blocked, true),
            (heal, RecapTheme.Healed, "healed", r => r.Healed, true),
            (k.Icon(PetIcon), RecapTheme.Blocked, "tanked by pets", r => r.PetTanked, false),
            (k.Icon(DebuffBuilder.IconPrefix + "WEAK_POWER"), RecapTheme.Teal, "kept off by debuffs", r => r.Prevented, false),
        };
        var numbers = new List<(Control Line, LiveNumber Number, Func<DefenseRow, int> Value, bool Always)>();
        foreach ((Texture2D? icon, Color tone, string words, Func<DefenseRow, int> value, bool always) in items)
        {
            HBoxContainer line = k.Row(9 * scale);
            if (icon != null) line.AddChild(Kit.Center(k.Pic(icon, 30 * scale, 30 * scale)));
            var number = new LiveNumber(k.Text("", 26 * scale, tone, true, Ink.Soft), view.Defense.Sum(value));
            line.AddChild(Kit.Center(number.Control));
            line.AddChild(Kit.Center(k.Text(words, 16 * scale, RecapTheme.Muted)));
            row.AddChild(Kit.Center(line));
            numbers.Add((line, number, value, always));
        }
        void Apply(RecapView v)
        {
            foreach ((Control line, LiveNumber number, Func<DefenseRow, int> value, bool always) in numbers)
            {
                int total = v.Defense.Sum(value);
                number.Set(total);
                line.Visible = always || total > 0;
            }
        }
        Apply(view);
        live?.On(Apply);
        return tip;
    }

    /// <summary>
    /// The combat HUD's HP bar, used for a whole run: red for damage taken, then green for what was healed, on one
    /// scale shared by every player.
    /// </summary>
    private sealed class HpBar
    {
        private readonly Kit _k;
        private readonly float _w, _h;
        private readonly TextureRect _taken, _healed;

        public HpBar(Kit k, float width, float height)
        {
            _k = k;
            _w = width;
            _h = height;
            var well = new Panel
            {
                CustomMinimumSize = k.V(width, height), Size = k.V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipChildren = CanvasItem.ClipChildrenMode.AndDraw,
            };
            StyleBoxFlat box = RecapTheme.Box(new Color("1c0d0d"), k.U(height * 0.27f), new Color("050505"), k.U(2));
            well.AddThemeStyleboxOverride("panel", box);
            _taken = Fill(new Color("ff7a68"), new Color("b8352a"));
            _healed = Fill(new Color("a2ea88"), new Color("4c9a38"));
            well.AddChild(_taken);
            well.AddChild(_healed);
            var rim = new Panel { Size = k.V(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
            StyleBoxFlat rimBox = RecapTheme.Box(RecapTheme.Clear, k.U(height * 0.27f), new Color("050505"), k.U(2));
            rimBox.DrawCenter = false;
            rim.AddThemeStyleboxOverride("panel", rimBox);
            well.AddChild(rim);
            Control = Kit.Center(well);
        }

        public Control Control { get; }

        public void Set(double taken, double healed)
        {
            float t = (float)Math.Clamp(taken, 0, 1) * _w, h = (float)Math.Clamp(healed, 0, 1 - Math.Clamp(taken, 0, 1)) * _w;
            Vector2 takenSize = _k.V(t, _h), healedSize = _k.V(h, _h), healedAt = _k.V(t, 0);
            if (_taken.IsInsideTree())
            {
                Anim.To(_taken, "size", takenSize);
                Anim.To(_healed, "size", healedSize);
                Anim.To(_healed, "position", healedAt);
            }
            else
            {
                _taken.Size = takenSize;
                _healed.Size = healedSize;
                _healed.Position = healedAt;
            }
        }

        private TextureRect Fill(Color light, Color dark)
        {
            var gradient = new Gradient();
            gradient.SetColor(0, light);
            gradient.SetColor(1, dark);
            return new TextureRect
            {
                Texture = new GradientTexture2D { Gradient = gradient, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 32 },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Size = Vector2.Zero,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }
    }
}
