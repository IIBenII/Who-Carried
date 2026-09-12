using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Damage per fight as lines over the route itself: a legend, then a framed chart whose x-axis is the map you climbed
/// (each fight's room icon), gold act labels, and a tooltip-style readout that snaps to the nearest fight.
/// Live: points glide to their new values and new fights slide in.
/// </summary>
internal static class TimelineTab
{
    /// <summary>The chart's name in the scene tree, so the dev preview can find it and hover it.</summary>
    public const string HoverLayerName = "ChartHover";

    /// <summary>Opens the readout on a fight directly (dev preview screenshots).</summary>
    public static Action<int>? PreviewShowFight { get; private set; }

    /// <summary>Logs hover events (dev preview only).</summary>
    public static bool PreviewDiagnostics { get; set; }

    private const float ChartW = 1488, ChartH = 548;

    public static Control Create(Kit k, RecapView view, Live live)
    {
        Control tab = k.Box(RecapPanel.DesignW, RecapPanel.DesignH);
        tab.AddChild(k.At(Legend(k, view, live), 40, 140));
        Label empty = k.Text("No fights recorded yet. The chart fills in as you go.", 18, RecapTheme.Muted);
        tab.AddChild(k.At(empty, 40, 190));
        PanelContainer frame = k.Tip(12, 10, alpha: 0.55f);
        frame.AddChild(Chart(k, view, ChartW, ChartH, interactive: true, live, visible => { frame.Visible = visible; empty.Visible = !visible; }));
        tab.AddChild(k.At(frame, 40, 184));
        return tab;
    }

    /// <summary>Each player's icon, name and line colour.</summary>
    private static Control Legend(Kit k, RecapView view, Live live)
    {
        HBoxContainer legend = k.Row(26);
        string shown = "";
        void Apply(RecapView v)
        {
            // In scoreboard order, like every other tab.
            List<string> ranked = ScoreboardTab.Players(v).Select(p => p.Label).ToList();
            List<TimelineSeries> series = v.Timeline.OrderBy(s => ranked.IndexOf(s.Label) is int i && i >= 0 ? i : 99).ToList();
            string signature = string.Join("|", series.Select(s => s.Label + s.ColorHex + s.IconKey));
            if (signature == shown) return;
            shown = signature;
            foreach (Node child in legend.GetChildren())
            {
                legend.RemoveChild(child);
                child.QueueFree();
            }
            foreach (TimelineSeries s in series)
            {
                HBoxContainer item = k.Row(8);
                if (k.Icon(s.IconKey) is Texture2D icon) item.AddChild(Kit.Center(k.Pic(icon, 28, 28)));
                item.AddChild(Kit.Center(k.Text(s.Label, 19, RecapTheme.Text, true, Ink.Soft)));
                Panel pill = k.Swatch(RecapTheme.Accent(s.ColorHex), 26, 6, 3);
                ((StyleBoxFlat)pill.GetThemeStylebox("panel")).SetBorderWidthAll(Math.Max(1, k.F(1.5f)));
                ((StyleBoxFlat)pill.GetThemeStylebox("panel")).BorderColor = RecapTheme.Ink;
                item.AddChild(Kit.Center(pill));
                legend.AddChild(item);
            }
        }
        Apply(view);
        live.On(Apply);
        return legend;
    }

    /// <summary>
    /// The chart, drawn in one pass: gridlines and values, dashed act lines with gold labels, cased lines with dots,
    /// and the room icons along the bottom. <paramref name="setVisible"/> hears whether there's anything to draw.
    /// </summary>
    public static Control Chart(Kit k, RecapView view, float width, float height, bool interactive, Live? live,
                                Action<bool>? setVisible = null)
    {
        const float left = 58, right = 16, top = 30, bottom = 30, icon = 28;
        float plotW = width - left - right, plotH = height - top - bottom, baseline = top + plotH;
        var chart = new Control
        {
            Name = HoverLayerName, CustomMinimumSize = k.V(width, height), Size = k.V(width, height),
            MouseFilter = interactive ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
        };

        RecapView current = view;
        int fights = 0, hovered = -1;
        float[][] from = Array.Empty<float[]>(), to = Array.Empty<float[]>();
        float fromTop = 1, toTop = 1, t = 1;
        Tween? tween = null;
        Color[] colors = Array.Empty<Color>();
        var actLine = new Color(RecapTheme.Gold, 0.25f);

        float X(int i) => fights <= 1 ? left + plotW / 2 : left + i * plotW / (fights - 1);
        float Value(int i, int s) => s < from[i].Length && s < to[i].Length ? from[i][s] + (to[i][s] - from[i][s]) * t : 0;
        float Top() => fromTop + (toTop - fromTop) * t;
        float Y(float v) => top + plotH - v * plotH / Top();

        chart.Draw += () =>
        {
            if (fights == 0) return;
            // Looked up at each draw: the game can dispose fonts kept from earlier.
            Font? regular = RecapTheme.Regular, bold = RecapTheme.Bold;
            float max = Top();
            for (int g = 0; g <= 4; g++)
            {
                float value = max * g / 4, y = Y(value);
                chart.DrawLine(k.V(left, y), k.V(left + plotW, y), RecapTheme.Line, Math.Max(1, k.U(1)));
                if (regular != null)
                    chart.DrawString(regular, k.V(0, y + 5), Kit.Num((long)Math.Round(value)), HorizontalAlignment.Right, k.U(left - 10), k.F(14), RecapTheme.Faint);
            }
            if (bold != null) chart.DrawString(bold, k.V(left + 8, 16), $"Act {current.FightPoints[0].Act}", HorizontalAlignment.Left, -1, k.F(14), RecapTheme.Gold);
            foreach (int start in current.ActStarts)
            {
                if (start <= 0 || start >= fights) continue;
                float x = (X(start - 1) + X(start)) / 2;
                chart.DrawDashedLine(k.V(x, 4), k.V(x, baseline), actLine, k.U(2), k.U(6));
                if (bold != null) chart.DrawString(bold, k.V(x + 8, 16), $"Act {current.FightPoints[start].Act}", HorizontalAlignment.Left, -1, k.F(14), RecapTheme.Gold);
            }
            if (hovered >= 0 && hovered < fights)
                chart.DrawLine(k.V(X(hovered), top), k.V(X(hovered), baseline), new Color(1, 1, 1, 0.4f), k.U(1.5f));
            for (int s = 0; s < colors.Length; s++)
            {
                Vector2[] points = Enumerable.Range(0, fights).Select(i => k.V(X(i), Y(Value(i, s)))).ToArray();
                if (points.Length >= 2)
                {
                    chart.DrawPolyline(points, RecapTheme.Ink, k.U(7), true);
                    chart.DrawPolyline(points, colors[s], k.U(3.5f), true);
                }
                for (int i = 0; i < points.Length; i++)
                {
                    float r = i == hovered ? 6.5f : 4.5f;
                    chart.DrawCircle(points[i], k.U(r + 2), RecapTheme.Ink);
                    chart.DrawCircle(points[i], k.U(r), colors[s]);
                }
            }
            float pitch = fights > 1 ? plotW / (fights - 1) : plotW;
            float size = Math.Min(icon, pitch - 2);
            for (int i = 0; i < fights; i++)
            {
                FightPoint fight = current.FightPoints[i];
                if (GameArt.Room(fight.Room) is Texture2D room)
                    chart.DrawTextureRect(room, new Rect2(k.V(X(i) - size / 2, height - 24 - (size - icon) / 2), k.V(size, size)), false);
            }
        };

        PanelContainer tip = k.Tip(16, 12, alpha: 0.97f);
        tip.Visible = false;
        tip.ZIndex = 5;
        VBoxContainer rows = k.Column(2);
        tip.AddChild(rows);
        if (interactive) chart.AddChild(tip);

        ulong pinnedUntil = 0; // the dev preview pins the readout so the real, idle cursor can't close it before the screenshot

        void Hide()
        {
            if (Time.GetTicksMsec() < pinnedUntil) return;
            if (PreviewDiagnostics && hovered >= 0) Tracker.Note("preview hover: readout hidden");
            hovered = -1;
            tip.Visible = false;
            chart.QueueRedraw();
        }

        void Show(int i, bool force = false)
        {
            if (i == hovered && !force) return;
            hovered = i;
            FightTip.Fill(k, rows, current, i);
            Vector2 size = tip.GetCombinedMinimumSize();
            tip.Size = size;
            // Beside the point, on whichever side has room, like the game's hover tips.
            float px = k.U(X(i));
            float x = px - k.U(28) - size.X >= 0 ? px - k.U(28) - size.X : px + k.U(28);
            tip.Position = new Vector2(x, k.U(40));
            tip.Visible = true;
            chart.QueueRedraw();
            if (PreviewDiagnostics)
                Tracker.Note($"preview hover: readout for fight {i} at {tip.GlobalPosition} size {size}, visible {tip.IsVisibleInTree()}");
        }

        void Set(RecapView v, bool animate)
        {
            int oldSeries = colors.Length;
            float[][] shown = Enumerable.Range(0, fights).Select(i => Enumerable.Range(0, oldSeries).Select(s => Value(i, s)).ToArray()).ToArray();
            float shownTop = Top();
            current = v;
            colors = v.Timeline.Select(s => RecapTheme.Accent(s.ColorHex)).ToArray();
            int series = colors.Length, n = v.FightPoints.Count;
            to = Enumerable.Range(0, n).Select(i => v.Timeline.Select(s => (float)s.Values[i]).ToArray()).ToArray();
            from = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, series)
                .Select(s => i < shown.Length && s < shown[i].Length ? shown[i][s] : 0f).ToArray()).ToArray();
            fights = n;
            int max = v.Timeline.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
            toTop = max > 0 ? ChartMath.GridCeiling(max) : 1;
            fromTop = animate && shown.Length > 0 ? shownTop : toTop;
            setVisible?.Invoke(n > 0 && max > 0);

            tween?.Kill();
            if (!animate || !chart.IsInsideTree())
            {
                t = 1;
                chart.QueueRedraw();
            }
            else
            {
                t = 0;
                tween = Anim.Progress(chart, p =>
                {
                    t = p;
                    chart.QueueRedraw();
                });
            }
            if (hovered >= fights) Hide();
            else if (hovered >= 0) Show(hovered, force: true);
        }

        Set(view, animate: false);
        live?.On(v => Set(v, animate: true));

        if (interactive)
        {
            chart.GuiInput += input =>
            {
                if (input is not InputEventMouseMotion motion || fights == 0) return;
                float mx = motion.Position.X / k.S, my = motion.Position.Y / k.S;
                int nearest = Enumerable.Range(0, fights).OrderBy(i => Math.Abs(X(i) - mx)).First();
                // Snap to the nearest fight anywhere between points (they can sit 100+ px apart on short runs).
                float reach = Math.Max(40, fights > 1 ? plotW / (fights - 1) / 2 + 2 : width);
                if (Math.Abs(X(nearest) - mx) > reach || my < top - 10 || my > height) Hide();
                else Show(nearest);
            };
            chart.MouseExited += Hide;
            PreviewShowFight = i =>
            {
                if (!GodotObject.IsInstanceValid(chart) || i < 0 || i >= fights) return;
                pinnedUntil = Time.GetTicksMsec() + 1500;
                Show(i);
            };
        }
        return chart;
    }
}
