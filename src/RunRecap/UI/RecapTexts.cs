using Godot;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>Wording and small lookups shared by the tabs and the saved image.</summary>
internal static class RecapTexts
{
    /// <summary>"Moth · The Tailor" → "Moth".</summary>
    public static string Name(string label) => Split(label).Name;

    /// <summary>"Moth · The Tailor" → ("Moth", "The Tailor").</summary>
    public static (string Name, string Character) Split(string label)
    {
        int dot = label.IndexOf(" · ", StringComparison.Ordinal);
        return dot < 0 ? (label, "") : (label[..dot], label[(dot + 3)..]);
    }

    public static bool IsOther(BarRow r) => r.Label.StartsWith(RecapBuilder.OtherPrefix + " (", StringComparison.Ordinal);

    /// <summary>Source rows keep their identity across refreshes; "Other (n)" keeps one key as n changes.</summary>
    public static string SourceKey(BarRow r) => IsOther(r) ? "other" : $"{r.SubLabel}:{r.Label}";

    public static string? PortraitKey(string? characterId) => string.IsNullOrEmpty(characterId) ? null : GameReader.PortraitPrefix + characterId;

    public static string? EnergyKey(string? characterId) => string.IsNullOrEmpty(characterId) ? null : GameReader.EnergyPrefix + characterId;

    /// <summary>The word after "Run recap ·": Victory, Defeat, or the act while the run is still going.</summary>
    public static (string Text, Color Color) Result(RecapView view) => view.Victory switch
    {
        true => ("Victory", RecapTheme.Gold),
        false => ("Defeat", RecapTheme.Taken),
        null => (view.Header.Split(',')[0], RecapTheme.Muted),
    };

    /// <summary>Play time as the game's timer shows it: 1:21:39, or 42:05 under an hour.</summary>
    public static string Duration(long seconds)
    {
        if (seconds <= 0) return "";
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }

    /// <summary>The floor reached: from the run facts, or read off the header ("Victory on floor 51").</summary>
    public static int Floor(RecapView view)
    {
        if (view.Facts is RunFacts f && f.Floor > 0) return f.Floor;
        string digits = new(view.Header.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out int floor) ? floor : 0;
    }

    public static int TeamDamage(RecapView view) => view.Overview.Where(r => r.Share != null).Sum(r => r.Value);

    /// <summary>The picture for each of the recap's own awards, from the game's icons.</summary>
    public static Texture2D? AwardArt(Kit k, string title) => title switch
    {
        AwardBuilder.HeavyHitter => GameArt.Get(GameArt.Swords),
        AwardBuilder.Enabler => k.Icon(DebuffBuilder.IconPrefix + "VULNERABLE_POWER"),
        AwardBuilder.Clutch => GameArt.Get(GameArt.Heart),
        AwardBuilder.Protector => k.Icon(DebuffBuilder.IconPrefix + "WEAK_POWER"),
        AwardBuilder.Wall or AwardBuilder.SiegeBreaker => GameArt.Get(GameArt.Block),
        AwardBuilder.FightLeader => GameArt.Get(GameArt.Trophy),
        AwardBuilder.JackOfAllTrades => GameArt.Get(GameArt.Cards),
        AwardBuilder.CardFactory => GameArt.Get(GameArt.Deck),
        AwardBuilder.Unscathed => GameArt.Get(GameArt.Perfect),
        AwardBuilder.PunchingBag => GameArt.Get(GameArt.Skull),
        _ => GameArt.Get(GameArt.Trophy),
    };

    /// <summary>
    /// A lone player's run in four lines: how it ended, the hardest fight, the closest call and the card of the run,
    /// each with a small picture. Lines without data are left out.
    /// </summary>
    public static List<(Control Art, string Text)> Story(Kit k, RecapView view)
    {
        var lines = new List<(Control, string)>();
        Control Icon(Texture2D? texture) => k.ThumbOf(texture, false, RecapTheme.TipEdge, 40, 32, 1.5f);
        BarRow? me = view.Overview.FirstOrDefault(r => r.Share != null);
        int fights = view.FightPoints.Count;
        string fightsText = fights == 1 ? "1 fight" : $"{fights} fights";
        int floor = Floor(view);
        string last = fights > 0 ? view.FightPoints[^1].Label : "";
        if (me != null)
        {
            string text = view.Victory switch
            {
                true when last.Length > 0 => $"Beat {last} on floor {floor}, {Kit.Num(me.Value)} damage over {fightsText}.",
                false when last.Length > 0 => $"Fell to {last} on floor {floor}, after {Kit.Num(me.Value)} damage over {fightsText}.",
                _ => $"Floor {floor} so far: {Kit.Num(me.Value)} damage over {fightsText}.",
            };
            lines.Add((Icon(GameArt.Get(GameArt.Boss)), text));
        }
        if (fights > 0 && view.Timeline.Count > 0)
        {
            int best = Enumerable.Range(0, fights).OrderByDescending(i => view.Timeline.Sum(s => s.Values[i])).First();
            int damage = view.Timeline.Sum(s => s.Values[best]);
            if (damage > 0)
                lines.Add((Icon(GameArt.Room(view.FightPoints[best].Room)), $"Hardest fight: {view.FightPoints[best].Label}, {Kit.Num(damage)} damage in one fight."));
        }
        if (view.Defense.FirstOrDefault() is DefenseRow d && d.LowestHp > 0)
            lines.Add((Icon(GameArt.Get(GameArt.Heart)), $"Closest call: {Kit.Num(d.LowestHp)} HP left of {Kit.Num(d.LowestHpMax)}."));
        BarRow? top = view.Sources.FirstOrDefault()?.Rows.FirstOrDefault(r => !IsOther(r));
        if (top != null && me != null && me.Value > 0)
        {
            string what = top.SubLabel == nameof(SourceKind.Card) ? "Card of the run" : "Top source";
            lines.Add((k.Thumb(top.ArtKey, top.SubLabel, 40, 32, 1.5f),
                $"{what}: {top.Label}, {Math.Round(100.0 * top.Value / me.Value):0}% of your damage."));
        }
        return lines;
    }
}
