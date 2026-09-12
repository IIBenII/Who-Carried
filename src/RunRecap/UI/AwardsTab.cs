using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>
/// The recap's awards as a spread of small cards (the award's icon as art, its title on the banner, the winner's face
/// in the gem and name on the plaque), and down the right each player's game badges: "Relics of the run".
/// </summary>
internal static class AwardsTab
{
    public static Control Create(Kit k, RecapView view, Live live)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        Control spread = k.At(k.Box(1130, 640), 40, 146);
        tab.AddChild(spread);
        Label empty = k.Text("No awards yet. They fill in as the run goes.", 18, RecapTheme.Muted);
        spread.AddChild(empty);

        var cards = new Dictionary<string, (CardFace Face, Label Value, Label Detail)>();
        string shown = "";
        void Apply(RecapView v)
        {
            empty.Visible = v.Awards.Count == 0;
            // An award changing hands gets a new card; values update in place.
            string signature = string.Join("|", v.Awards.Select(a => $"{a.Title}:{a.PlayerId}"));
            if (signature != shown)
            {
                shown = signature;
                foreach ((CardFace face, _, _) in cards.Values) face.Root.QueueFree();
                cards.Clear();
                (float width, float step, int columns) = Grid(v.Awards.Count);
                for (int i = 0; i < v.Awards.Count; i++)
                {
                    Award award = v.Awards[i];
                    (CardFace face, Label value, Label detail) = AwardCard(k, award, width);
                    k.At(face.Root, (i % columns) * step, (i / columns) * (width * CardFace.Aspect + 22));
                    face.LiftOnHover();
                    spread.AddChild(face.Root);
                    cards[$"{award.Title}:{award.PlayerId}"] = (face, value, detail);
                }
            }
            foreach (Award award in v.Awards)
            {
                if (!cards.TryGetValue($"{award.Title}:{award.PlayerId}", out var card)) continue;
                card.Value.Text = award.Value;
                card.Detail.Text = award.Detail;
            }
        }
        Apply(view);
        live.On(Apply);

        tab.AddChild(k.At(Relics(k, view, 378, full: true, live), 1190, 140, 378, -1));
        return tab;
    }

    /// <summary>One row of cards for up to five awards, else two rows: as wide as fits, 206 at most.</summary>
    private static (float Width, float Step, int Columns) Grid(int count)
    {
        int rows = count <= 5 ? 1 : 2;
        int columns = Math.Max(1, (count + rows - 1) / rows);
        float step = Math.Min(226, 1130f / columns);
        return (step - 20, step, columns);
    }

    public static (CardFace Face, Label Value, Label Detail) AwardCard(Kit k, Award award, float width)
    {
        Color color = RecapTheme.FromHex(award.ColorHex);
        var face = new CardFace(k, new CardSpec(width, color, award.Title, Art: RecapTexts.AwardArt(k, award.Title),
            Gem: k.Icon(RecapTexts.EnergyKey(award.IconKey)), GemFace: k.Icon(award.IconKey), Plaque: award.PlayerName));
        float em = face.Em;
        Label value = k.Strong(award.Value, em * 2.8f);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        face.Body.AddChild(value);
        Label detail = k.Text(award.Detail, em * 1.12f, new Color("d9dde3"));
        detail.HorizontalAlignment = HorizontalAlignment.Center;
        detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.CustomMinimumSize = k.V(width * 0.78f, 0);
        detail.AddThemeConstantOverride("line_spacing", -k.F(em * 0.25f));
        face.Body.AddChild(detail);
        return (face, value, detail);
    }

    /// <summary>
    /// Each player's game badges: the medal and its name in the rarity's colour. <paramref name="full"/> panels stack
    /// down a column; otherwise a compact line per player for the saved image.
    /// </summary>
    public static Control Relics(Kit k, RecapView view, float width, bool full, Live? live)
    {
        VBoxContainer box = k.Column(10);
        box.AddChild(k.Heading("Relics of the run", GameArt.Get(GameArt.Achievements), "the game's badges", full ? 22 : 26));
        VBoxContainer body = k.Column(10);
        box.AddChild(body);
        string shown = "";

        Control Panel(PlayerBadges p)
        {
            Color color = RecapTheme.Accent(p.ColorHex);
            PanelContainer tip = k.Tip(12, 10);
            VBoxContainer column = k.Column(7);
            column.AddChild(k.Who(p.Name, p.IconKey, color));
            if (p.Badges.Count == 0)
            {
                column.AddChild(k.Text("No badges this run", 15, RecapTheme.Faint));
            }
            else
            {
                var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                flow.AddThemeConstantOverride("h_separation", k.F(14));
                flow.AddThemeConstantOverride("v_separation", k.F(7));
                flow.CustomMinimumSize = k.V(width - 28, 0);
                foreach (BadgeInfo badge in p.Badges)
                {
                    HBoxContainer item = k.Row(7);
                    item.AddChild(Kit.Center(k.Medal(badge, 34)));
                    item.AddChild(Kit.Center(k.Text(badge.Title, 15, RecapTheme.MedalColor(badge.Rarity), true, Ink.Soft)));
                    flow.AddChild(item);
                }
                column.AddChild(flow);
            }
            tip.AddChild(column);
            return tip;
        }

        void Apply(RecapView v)
        {
            string signature = v.Badges == null ? "-" : string.Join(";", v.Badges.Select(p => $"{p.PlayerId}:{string.Join(",", p.Badges.Select(b => b.Id + b.Rarity))}"));
            if (signature == shown) return;
            shown = signature;
            foreach (Node child in body.GetChildren())
            {
                body.RemoveChild(child);
                child.QueueFree();
            }
            if (v.Badges == null)
            {
                Label wait = k.Text("The game hands out its badges when the run ends.", 16, RecapTheme.Muted);
                wait.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                wait.CustomMinimumSize = k.V(width, 0);
                body.AddChild(wait);
                return;
            }
            foreach (PlayerBadges p in v.Badges) body.AddChild(Panel(p));
        }
        Apply(view);
        live?.On(Apply);
        return box;
    }
}
