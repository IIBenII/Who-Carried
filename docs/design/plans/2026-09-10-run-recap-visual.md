# Run Recap Visual Redesign + PNG Export — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the Run Recap panel to match the game (Kreon fonts, StS palette, character icons, highlight tiles), add a "Save image" PNG export of a summary card, and fix the solo-play player name.

**Architecture:**
- **Core:** gains icon keys, highlights, a victory flag, and a nice-axis helper, all unit-tested.
- **UI:** split into a theme (fonts, palette, factories), shared widgets, the interactive panel, and the export card. An offscreen `SubViewport` exporter renders the card.
- **Dev preview:** a flag-file mode screenshots every tab, so Claude can check the visuals without a play session.

**Tech Stack:** C# / .NET 9, GodotSharp 4.5.1 (game-provided), game fonts from `res://`. No NuGet.

**Spec:** `docs/design/specs/2026-09-10-run-recap-visual-design.md`

## Global Constraints

- Everything in the original plan's Global Constraints still applies. In particular: read-only, no NuGet, the x64 `dotnet` by full path, try/catch in every game callback, and **no git** (no commit steps).
- Fonts come from `res://themes/kreon_bold_shared.tres` and `res://themes/kreon_regular_shared.tres`, falling back to `res://fonts/kreon_bold.ttf` and `res://fonts/kreon_regular.ttf`, then to the engine default.
- Palette: cream `fff6e2`, gold `efc851`, muted `a89c88`, faint `6f6556`, panel `1c1714`, border `6e5a36`, inset `151110`, track `2c2420`, divider `3a2f24`, outline `120c07`, red `ff6b5e`, blue `87ceeb`, green `8fd46a`, grey `8a8a8a`.
- Export path: `%USERPROFILE%\Pictures\Run Recap\run-YYYY-MM-DD_HHmm-<victory|defeat|in-progress>.png`.
- Dev preview only runs when `<game>\mods\RunRecap\data\preview.flag` exists.
- In Godot UI files, write `System.Environment` explicitly, because `Godot.Environment` also exists.

## File Map

| File | Change |
|---|---|
| `src/RunRecap/Core/Model.cs` | `PlayerInfo` gains an optional `CharacterId` |
| `src/RunRecap/Core/RecapBuilder.cs` | Icon keys, `Highlight`, `Victory`, `OtherPrefix`, `NoValue` |
| `src/RunRecap/Core/ChartMath.cs` | New: `NiceCeiling` |
| `tests/RunRecap.Tests/RecapBuilderTests.cs` | Replaced (existing tests plus new ones) |
| `tests/RunRecap.Tests/ChartMathTests.cs` | New |
| `src/RunRecap/Game/GameReader.cs` | Replaced: character id, Steam name in solo, `CharacterIcon` |
| `src/RunRecap/Game/Tracker.cs` | Add `DataDir`, `Note` |
| `src/RunRecap/UI/RecapTheme.cs` | New |
| `src/RunRecap/UI/RecapWidgets.cs` | New |
| `src/RunRecap/UI/RecapPanel.cs` | Replaced |
| `src/RunRecap/UI/SummaryCard.cs` | New |
| `src/RunRecap/UI/PngExporter.cs` | New |
| `src/RunRecap/UI/Later.cs` | New |
| `src/RunRecap/UI/DevPreview.cs` | New |
| `src/RunRecap/UI/RecapUi.cs` | Replaced |

Code blocks are introduced by a line holding the file path in backticks followed by a colon. Each block is the **complete** file content.

---

### Task 1: Core view-model additions

**Files:**
- Modify: `src/RunRecap/Core/Model.cs` (full replacement below), `src/RunRecap/Core/RecapBuilder.cs` (full replacement below)
- Create: `src/RunRecap/Core/ChartMath.cs`
- Test: `tests/RunRecap.Tests/RecapBuilderTests.cs` (full replacement), `tests/RunRecap.Tests/ChartMathTests.cs`

**Interfaces:**
- Produces:
  - `record PlayerInfo(ulong NetId, string Name, string Character, string ColorHex, string CharacterId = "")`
  - `record BarRow(..., string ColorHex, string? IconKey = null)`
  - `record SourcesView(string PlayerLabel, IReadOnlyList<BarRow> Rows, string ColorHex = RecapBuilder.GreyHex, string? IconKey = null)`
  - `record TimelineSeries(string Label, string ColorHex, IReadOnlyList<int> Values, string? IconKey = null)`
  - `record DefenseRow(string Label, string ColorHex, int Taken, int Blocked, int Healed, string? IconKey = null)`
  - `record Highlight(string Label, string Value, string Sub)`
  - `record RecapView(string Header, IReadOnlyList<BarRow> Overview, IReadOnlyList<SourcesView> Sources, IReadOnlyList<TimelineSeries> Timeline, IReadOnlyList<int> FightActs, IReadOnlyList<int> ActStarts, IReadOnlyList<DefenseRow> Defense, IReadOnlyList<Highlight> Highlights, bool? Victory)`
  - `RecapBuilder.Build(RunStats, IReadOnlyList<PlayerInfo>, IReadOnlyDictionary<ulong, DefenseTotals>, string header, bool? victory = null)`
  - constants `RecapBuilder.OtherPrefix = "Other"`, `RecapBuilder.NoValue = "—"`
  - `static int ChartMath.NiceCeiling(int value)`

- [ ] **Step 1: Write the failing tests**

`tests/RunRecap.Tests/RecapBuilderTests.cs`:

```csharp
using RunRecap.Core;

namespace RunRecap.Tests;

public static class RecapBuilderTests
{
    private static readonly PlayerInfo Alice = new(1, "Alice", "Ironclad", "d85a30");
    private static readonly PlayerInfo Bob = new(2, "Bob", "The Tailor", "7f77dd");
    private static readonly IReadOnlyDictionary<ulong, DefenseTotals> NoDefense = new Dictionary<ulong, DefenseTotals>();

    private static SourceRef Card(string id) => new(SourceKind.Card, id, id.ToLowerInvariant());

    [Test]
    public static void OverviewIsSortedWithSharesOverRealPlayers()
    {
        var s = new RunStats();
        s.RecordDamage(1, Card("A"), 30);
        s.RecordDamage(2, Card("A"), 70);
        s.RecordDamage(null, Card("A"), 10);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal("Bob", v.Overview[0].Label, "top row");
        Check.Equal("The Tailor", v.Overview[0].SubLabel, "character sub-label");
        Check.Near(0.7, v.Overview[0].Share!.Value, "bob share");
        Check.Near(0.3, v.Overview[1].Share!.Value, "alice share");
        Check.Near(1.0, v.Overview[0].Fraction, "bob fraction is the max");
        Check.Equal(RecapBuilder.UnattributedLabel, v.Overview[2].Label, "unattributed last");
        Check.True(v.Overview[2].Share == null, "unattributed has no share");
        Check.Equal(RecapBuilder.UnattributedLabel, v.Sources[2].PlayerLabel, "unattributed sources view");
    }

    [Test]
    public static void UnattributedOnlyAppearsWhenNonZero()
    {
        var s = new RunStats();
        s.RecordDamage(1, Card("A"), 5);
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal(2, v.Overview.Count, "overview rows");
        Check.Equal(2, v.Sources.Count, "source views");
    }

    [Test]
    public static void SourcesShowTopSixPlusOther()
    {
        var s = new RunStats();
        for (int i = 1; i <= 9; i++) s.RecordDamage(1, Card($"C{i}"), i * 10);
        RecapView v = RecapBuilder.Build(s, new[] { Alice }, NoDefense, "h");
        IReadOnlyList<BarRow> rows = v.Sources[0].Rows;
        Check.Equal(7, rows.Count, "6 + other");
        Check.Equal("c9", rows[0].Label, "biggest first");
        Check.Equal("Other (3)", rows[6].Label, "other label");
        Check.Equal(60, rows[6].Value, "other = 30 + 20 + 10");
        Check.Equal(RecapBuilder.GreyHex, rows[6].ColorHex, "other is grey");
        Check.True(rows.All(r => r.Fraction <= 1.0), "fractions capped at 1");
        Check.Equal("Alice · Ironclad", v.Sources[0].PlayerLabel, "player label");
    }

    [Test]
    public static void TimelineHasOneValuePerFightAndMarksActStarts()
    {
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(1, Card("A"), 10); s.EndFight();
        s.BeginFight(1, 2, "f2"); s.RecordDamage(2, Card("A"), 20); s.EndFight();
        s.BeginFight(2, 18, "f3"); s.RecordDamage(1, Card("A"), 30); s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal("10,0,30", string.Join(",", v.Timeline[0].Values), "alice series");
        Check.Equal("0,20,0", string.Join(",", v.Timeline[1].Values), "bob series");
        Check.Equal("1,1,2", string.Join(",", v.FightActs), "acts per fight");
        Check.Equal("2", string.Join(",", v.ActStarts), "act 2 starts at fight index 2");
    }

    [Test]
    public static void DefenseCombinesGameTotalsWithTrackedBlock()
    {
        var s = new RunStats();
        s.RecordBlocked(1, 40);
        var defense = new Dictionary<ulong, DefenseTotals> { [1] = new(Taken: 12, Healed: 6) };
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, defense, "h");
        Check.Equal(new DefenseRow("Alice", "d85a30", 12, 40, 6), v.Defense[0], "alice");
        Check.Equal(new DefenseRow("Bob", "7f77dd", 0, 0, 0), v.Defense[1], "bob defaults to zero");
    }

    [Test]
    public static void EmptyRunDoesNotDivideByZero()
    {
        RecapView v = RecapBuilder.Build(new RunStats(), new[] { Alice, Bob }, NoDefense, "h");
        Check.True(v.Overview.All(r => r.Share == 0 && r.Fraction == 0), "all zero");
        Check.Equal(0, v.FightActs.Count, "no fights");
        Check.Equal("h", v.Header, "header passed through");
        Check.Equal(new Highlight("Team damage", "0", "0 fights"), v.Highlights[0], "team highlight");
        Check.Equal(RecapBuilder.NoValue, v.Highlights[1].Value, "no top source");
        Check.Equal(RecapBuilder.NoValue, v.Highlights[2].Value, "no biggest fight");
        Check.True(v.Victory == null, "in progress by default");
    }

    [Test]
    public static void IconKeysColorsAndResultFlowThrough()
    {
        var carol = new PlayerInfo(3, "Carol", "The Silent", "7fff00", "SILENT");
        var s = new RunStats();
        s.BeginFight(1, 1, "f1"); s.RecordDamage(3, Card("A"), 5); s.EndFight();
        s.RecordBlocked(3, 2);
        RecapView v = RecapBuilder.Build(s, new[] { carol }, NoDefense, "h", victory: true);
        Check.Equal("SILENT", v.Overview[0].IconKey, "overview icon");
        Check.Equal("SILENT", v.Sources[0].IconKey, "sources icon");
        Check.Equal("7fff00", v.Sources[0].ColorHex, "sources colour");
        Check.Equal("SILENT", v.Timeline[0].IconKey, "timeline icon");
        Check.Equal("SILENT", v.Defense[0].IconKey, "defense icon");
        Check.True(v.Sources[0].Rows[0].IconKey == null, "source rows carry no icon");
        Check.Equal<bool?>(true, v.Victory, "victory");
    }

    [Test]
    public static void HighlightsSummariseTheRun()
    {
        var s = new RunStats();
        s.BeginFight(1, 3, "Jaw Worm");
        s.RecordDamage(1, Card("BASH"), 30);
        s.RecordDamage(2, Card("ZAP"), 10);
        s.EndFight();
        s.BeginFight(2, 20, "Hexaghost");
        s.RecordDamage(2, Card("ZAP"), 70);
        s.EndFight();
        RecapView v = RecapBuilder.Build(s, new[] { Alice, Bob }, NoDefense, "h");
        Check.Equal(new Highlight("Team damage", "110", "2 fights"), v.Highlights[0], "team");
        Check.Equal(new Highlight("Top source", "zap", "80 · Bob"), v.Highlights[1], "top source");
        Check.Equal(new Highlight("Biggest fight", "70", "Hexaghost · Act 2"), v.Highlights[2], "biggest fight");
    }
}
```

`tests/RunRecap.Tests/ChartMathTests.cs`:

```csharp
using RunRecap.Core;

namespace RunRecap.Tests;

public static class ChartMathTests
{
    [Test]
    public static void NiceCeilingRoundsUpToFriendlyAxisValues()
    {
        Check.Equal(1, ChartMath.NiceCeiling(0), "0");
        Check.Equal(1, ChartMath.NiceCeiling(1), "1");
        Check.Equal(5, ChartMath.NiceCeiling(3), "3");
        Check.Equal(10, ChartMath.NiceCeiling(7), "7");
        Check.Equal(100, ChartMath.NiceCeiling(95), "95");
        Check.Equal(200, ChartMath.NiceCeiling(120), "120");
        Check.Equal(2500, ChartMath.NiceCeiling(2400), "2400");
        Check.Equal(5000, ChartMath.NiceCeiling(2600), "2600");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project tests\RunRecap.Tests`
Expected: build errors, including `CS0246: The type or namespace name 'Highlight' could not be found` and `CS0103: The name 'ChartMath' does not exist`.

- [ ] **Step 3: Implement**

`src/RunRecap/Core/Model.cs`:

```csharp
namespace RunRecap.Core;

public enum SourceKind { Card, Power, Relic, Potion, Orb, Pet, Monster, Other, Unknown }

/// <summary>What caused a piece of damage. <see cref="Key"/> merges repeat hits from the same source.</summary>
public sealed record SourceRef(SourceKind Kind, string Id, string Label)
{
    public static readonly SourceRef Unknown = new(SourceKind.Unknown, "UNKNOWN", "Unknown");

    public string Key => $"{Kind}:{Id}";
}

/// <summary>A possible source of a hit, plus the player who owns it (if any).</summary>
public sealed record SourceCandidate(SourceRef Source, ulong? OwnerId);

/// <summary>Plain facts about one damage result, extracted from game objects by Game/FactsExtractor.</summary>
public sealed record DamageFacts(
    int HpRemoved,
    int Blocked,
    bool TargetIsEnemy,
    ulong? TargetPlayerId,
    ulong? DealerPlayerId,
    SourceCandidate? Pet,
    SourceCandidate? Card,
    SourceCandidate? StackTop,
    SourceCandidate? Fallback);

/// <summary>A player as shown in the recap. CharacterId (e.g. "IRONCLAD") is used to look up the character icon.</summary>
public sealed record PlayerInfo(ulong NetId, string Name, string Character, string ColorHex, string CharacterId = "");
```

`src/RunRecap/Core/ChartMath.cs`:

```csharp
namespace RunRecap.Core;

public static class ChartMath
{
    /// <summary>The smallest "nice" axis maximum (1, 2, 2.5 or 5 × 10ⁿ) that is at least <paramref name="value"/>.</summary>
    public static int NiceCeiling(int value)
    {
        if (value <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1, 2, 2.5, 5, 10 })
        {
            double candidate = step * magnitude;
            if (candidate >= value) return (int)Math.Ceiling(candidate);
        }
        return (int)Math.Ceiling(10 * magnitude);
    }
}
```

`src/RunRecap/Core/RecapBuilder.cs`:

```csharp
using System.Globalization;

namespace RunRecap.Core;

public sealed record BarRow(string Label, string SubLabel, int Value, double Fraction, double? Share, string ColorHex,
                            string? IconKey = null);

public sealed record SourcesView(string PlayerLabel, IReadOnlyList<BarRow> Rows, string ColorHex = RecapBuilder.GreyHex,
                                 string? IconKey = null);

public sealed record TimelineSeries(string Label, string ColorHex, IReadOnlyList<int> Values, string? IconKey = null);

public sealed record DefenseRow(string Label, string ColorHex, int Taken, int Blocked, int Healed, string? IconKey = null);

public sealed record DefenseTotals(int Taken, int Healed);

public sealed record Highlight(string Label, string Value, string Sub);

public sealed record RecapView(
    string Header,
    IReadOnlyList<BarRow> Overview,
    IReadOnlyList<SourcesView> Sources,
    IReadOnlyList<TimelineSeries> Timeline,
    IReadOnlyList<int> FightActs,
    IReadOnlyList<int> ActStarts,
    IReadOnlyList<DefenseRow> Defense,
    IReadOnlyList<Highlight> Highlights,
    bool? Victory);

/// <summary>Turns counted stats into exactly what the panel and the exported card display. No game or Godot types.</summary>
public static class RecapBuilder
{
    public const int TopSources = 6;
    public const string GreyHex = "8a8a8a";
    public const string UnattributedLabel = "Unattributed";
    public const string OtherPrefix = "Other";
    public const string NoValue = "—";

    public static RecapView Build(
        RunStats stats,
        IReadOnlyList<PlayerInfo> players,
        IReadOnlyDictionary<ulong, DefenseTotals> defense,
        string header,
        bool? victory = null)
    {
        Dictionary<ulong, int> dealt = players.ToDictionary(p => p.NetId, p => stats.Get(p.NetId)?.DamageDealt ?? 0);
        int team = dealt.Values.Sum();
        int unattributed = stats.Get(null)?.DamageDealt ?? 0;
        int max = Math.Max(dealt.Values.DefaultIfEmpty(0).Max(), unattributed);
        List<PlayerInfo> byDamage = players.OrderByDescending(p => dealt[p.NetId]).ToList();

        var overview = byDamage
            .Select(p => new BarRow(p.Name, p.Character, dealt[p.NetId], Fraction(dealt[p.NetId], max),
                team == 0 ? 0 : (double)dealt[p.NetId] / team, p.ColorHex, IconOf(p)))
            .ToList();
        if (unattributed > 0)
            overview.Add(new BarRow(UnattributedLabel, "", unattributed, Fraction(unattributed, max), null, GreyHex));

        var sources = byDamage
            .Select(p => new SourcesView($"{p.Name} · {p.Character}", SourceRows(stats.Get(p.NetId), p.ColorHex),
                p.ColorHex, IconOf(p)))
            .ToList();
        if (unattributed > 0)
            sources.Add(new SourcesView(UnattributedLabel, SourceRows(stats.Get(null), GreyHex)));

        var timeline = players
            .Select(p => new TimelineSeries(p.Name, p.ColorHex,
                stats.Fights.Select(f => f.DamageByPlayer.GetValueOrDefault(RunStats.KeyFor(p.NetId))).ToList(),
                IconOf(p)))
            .ToList();
        List<int> fightActs = stats.Fights.Select(f => f.Act).ToList();
        List<int> actStarts = Enumerable.Range(1, Math.Max(0, fightActs.Count - 1))
            .Where(i => fightActs[i] != fightActs[i - 1])
            .ToList();

        var defenseRows = players
            .Select(p => new DefenseRow(p.Name, p.ColorHex,
                defense.GetValueOrDefault(p.NetId)?.Taken ?? 0,
                stats.Get(p.NetId)?.Blocked ?? 0,
                defense.GetValueOrDefault(p.NetId)?.Healed ?? 0,
                IconOf(p)))
            .ToList();

        return new RecapView(header, overview, sources, timeline, fightActs, actStarts, defenseRows,
            Highlights(stats, players, team), victory);
    }

    private static List<Highlight> Highlights(RunStats stats, IReadOnlyList<PlayerInfo> players, int team)
    {
        int fights = stats.Fights.Count;
        var list = new List<Highlight>
        {
            new("Team damage", Num(team), fights == 1 ? "1 fight" : $"{fights} fights"),
        };

        (PlayerInfo Player, SourceTotal Source) top = players
            .SelectMany(p => (stats.Get(p.NetId)?.Sources.Values ?? Enumerable.Empty<SourceTotal>())
                .Select(s => (Player: p, Source: s)))
            .OrderByDescending(x => x.Source.Amount)
            .ThenBy(x => x.Source.Label, StringComparer.Ordinal)
            .FirstOrDefault();
        list.Add(top.Source == null
            ? new Highlight("Top source", NoValue, "")
            : new Highlight("Top source", top.Source.Label, $"{Num(top.Source.Amount)} · {top.Player.Name}"));

        HashSet<string> realKeys = players.Select(p => RunStats.KeyFor(p.NetId)).ToHashSet();
        int FightTotal(FightBucket f) => f.DamageByPlayer.Where(kv => realKeys.Contains(kv.Key)).Sum(kv => kv.Value);
        FightBucket? biggest = stats.Fights.OrderByDescending(FightTotal).FirstOrDefault();
        list.Add(biggest == null || FightTotal(biggest) == 0
            ? new Highlight("Biggest fight", NoValue, "")
            : new Highlight("Biggest fight", Num(FightTotal(biggest)), $"{biggest.Label} · Act {biggest.Act}"));
        return list;
    }

    private static List<BarRow> SourceRows(PlayerTotals? totals, string colorHex)
    {
        if (totals == null) return new List<BarRow>();
        List<SourceTotal> ordered = totals.Sources.Values
            .OrderByDescending(s => s.Amount)
            .ThenBy(s => s.Label, StringComparer.Ordinal)
            .ToList();
        int top = ordered.Count > 0 ? ordered[0].Amount : 0;
        List<BarRow> rows = ordered.Take(TopSources)
            .Select(s => new BarRow(s.Label, s.Kind.ToString(), s.Amount, Fraction(s.Amount, top), null, colorHex))
            .ToList();
        List<SourceTotal> rest = ordered.Skip(TopSources).ToList();
        if (rest.Count > 0)
        {
            int sum = rest.Sum(s => s.Amount);
            rows.Add(new BarRow($"{OtherPrefix} ({rest.Count})", "", sum, Fraction(sum, top), null, GreyHex));
        }
        return rows;
    }

    private static string? IconOf(PlayerInfo p) => string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId;

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static double Fraction(int value, int max) => max <= 0 ? 0 : Math.Clamp((double)value / max, 0, 1);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests\RunRecap.Tests`
Expected: `27/27 passed`.

---

### Task 2: Game reader — names, character ids, icons

**Files:**
- Modify: `src/RunRecap/Game/GameReader.cs` (full replacement), `src/RunRecap/Game/Tracker.cs` (add two members)

**Interfaces:**
- Consumes: `PlayerInfo` with `CharacterId` (Task 1)
- Produces: `GameReader.CharacterIcon(IRunState run, string? characterId) → Texture2D?`, `GameReader.CharacterIcon(CharacterModel) → Texture2D?`, `GameReader.CharacterHex(CharacterModel, int index) → string`, `Tracker.DataDir → string`, `Tracker.Note(string line)`

- [ ] **Step 1: Replace GameReader**

`src/RunRecap/Game/GameReader.cs`:

```csharp
using System.Globalization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using RunRecap.Core;

namespace RunRecap.Game;

/// <summary>Read-only queries against game state.</summary>
internal static class GameReader
{
    private static readonly string[] Palette = { "d85a30", "7f77dd", "1d9e75", "d4537e", "378add", "ba7517" };

    public static IReadOnlyList<PlayerInfo> Players(IRunState run)
    {
        var list = new List<PlayerInfo>();
        for (int i = 0; i < run.Players.Count; i++)
        {
            Player p = run.Players[i];
            list.Add(new PlayerInfo(p.NetId, PlayerName(p, i, run.Players.Count), CharacterName(p.Character),
                CharacterHex(p.Character, i), p.Character.Id.Entry));
        }
        return list;
    }

    /// <summary>The top-bar icon of the character with this id among the run's players (modded characters included).</summary>
    public static Texture2D? CharacterIcon(IRunState run, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        CharacterModel? character = run.Players.Select(p => p.Character).FirstOrDefault(c => c.Id.Entry == characterId);
        return character == null ? null : CharacterIcon(character);
    }

    public static Texture2D? CharacterIcon(CharacterModel character)
    {
        try
        {
            if (character.IconTexture is Texture2D texture) return texture;
        }
        catch (Exception)
        {
            // not preloaded: try the conventional path below
        }
        try
        {
            string path = $"res://images/ui/top_panel/character_icon_{character.Id.Entry.ToLowerInvariant()}.png";
            if (ResourceLoader.Exists(path)) return ResourceLoader.Load<Texture2D>(path);
        }
        catch (Exception)
        {
            // fall back to the initials badge
        }
        return null;
    }

    public static string CharacterName(CharacterModel character) => GameText.Title(character.Title, character.Id.Entry);

    public static string CharacterHex(CharacterModel character, int index)
    {
        try { return character.NameColor.ToHtml(false); }
        catch (Exception) { return Palette[index % Palette.Length]; }
    }

    /// <summary>Damage taken and HP healed per player, from the game's own per-floor history.</summary>
    public static IReadOnlyDictionary<ulong, DefenseTotals> Defense(IRunState run)
    {
        var taken = new Dictionary<ulong, int>();
        var healed = new Dictionary<ulong, int>();
        foreach (var act in run.MapPointHistory)
        foreach (var point in act)
        foreach (var stats in point.PlayerStats)
        {
            taken[stats.PlayerId] = taken.GetValueOrDefault(stats.PlayerId) + stats.DamageTaken;
            healed[stats.PlayerId] = healed.GetValueOrDefault(stats.PlayerId) + stats.HpHealed;
        }
        return taken.Keys.Union(healed.Keys)
            .ToDictionary(id => id, id => new DefenseTotals(taken.GetValueOrDefault(id), healed.GetValueOrDefault(id)));
    }

    /// <summary>Stable across Save &amp; Quit: seed + the run's start time (set before RunStarted fires).</summary>
    public static string RunKey(IRunState run)
    {
        long start = 0;
        try
        {
            start = (long)(AccessTools.Field(typeof(RunManager), "_startTime")?.GetValue(RunManager.Instance) ?? 0L);
        }
        catch (Exception)
        {
            // fall back to seed only
        }
        return $"{run.Rng.StringSeed}:{start}";
    }

    public static string Header(IRunState run, bool? victory)
    {
        string result = victory switch { true => "Victory", false => "Defeat", null => "In progress" };
        return $"{result} · Act {run.CurrentActIndex + 1} · Floor {run.TotalFloor}";
    }

    public static string EncounterLabel(ICombatState? combat)
    {
        EncounterModel? encounter = combat?.Encounter;
        return encounter == null ? "Fight" : GameText.Title(encounter.Title, encounter.Id.Entry);
    }

    /// <summary>
    /// Solo runs use the "None" platform, whose player id is just 1, so ask Steam for the local persona instead.
    /// Raw (unescaped) names: our labels are plain text, not BBCode.
    /// </summary>
    private static string PlayerName(Player p, int index, int playerCount)
    {
        try
        {
            PlatformType platform = RunManager.Instance.NetService.Platform;
            string name = platform == PlatformType.None && playerCount == 1
                ? PlatformUtil.GetPlayerNameRaw(PlatformType.Steam, PlatformUtil.GetLocalPlayerId(PlatformType.Steam))
                : PlatformUtil.GetPlayerNameRaw(platform, p.NetId);
            if (!string.IsNullOrWhiteSpace(name) && name != p.NetId.ToString(CultureInfo.InvariantCulture)) return name;
        }
        catch (Exception)
        {
            // fall through
        }
        return playerCount == 1 ? "You" : $"Player {index + 1}";
    }
}
```

- [ ] **Step 2: Add `DataDir` and `Note` to Tracker**

In `src/RunRecap/Game/Tracker.cs`, directly below `public static IRunState? CurrentRun => _run;`, insert:

```csharp

    public static string DataDir => _dataDir;

    /// <summary>Appends a free-form line to events.log (exports, preview status).</summary>
    public static void Note(string line) => _log?.Write(line);
```

- [ ] **Step 3: Build**

Run: `dotnet build RunRecap.sln -c Release --nologo -v q`
Expected: `Build succeeded.`, 0 errors. `RecapUi` still compiles against the old `RecapBuilder.Build` signature, because `victory` is optional.

---

### Task 3: Game-native UI, export, and dev preview

**Files:**
- Create: `src/RunRecap/UI/RecapTheme.cs`, `src/RunRecap/UI/RecapWidgets.cs`, `src/RunRecap/UI/SummaryCard.cs`, `src/RunRecap/UI/PngExporter.cs`, `src/RunRecap/UI/Later.cs`, `src/RunRecap/UI/DevPreview.cs`
- Modify: `src/RunRecap/UI/RecapPanel.cs` (full replacement), `src/RunRecap/UI/RecapUi.cs` (full replacement)

**Interfaces:**
- Consumes: Tasks 1–2
- Produces: `record PanelHandle(Control Root, TabContainer Tabs, Label Status)`; `RecapPanel.Create(RecapView, Func<string?, Texture2D?> icons, Action onClose, Action<PanelHandle> onSave) → PanelHandle`; `RecapUi.ShowView(RecapView, Func<string?, Texture2D?>) → PanelHandle`; `SummaryCard.Create(RecapView, Func<string?, Texture2D?>) → Control`; `PngExporter.Save(Control, int width, string path, Action<string?> onDone)`; `Later.Run(double seconds, Action)`; `DevPreview.StartIfFlagged(string dataDir)`

- [ ] **Step 1: Theme**

`src/RunRecap/UI/RecapTheme.cs`:

```csharp
using Godot;

namespace RunRecap.UI;

/// <summary>The game's own palette and fonts, plus factories for consistently styled controls.</summary>
internal static class RecapTheme
{
    public static readonly Color Cream = new("fff6e2");
    public static readonly Color Gold = new("efc851");
    public static readonly Color Muted = new("a89c88");
    public static readonly Color Faint = new("6f6556");
    public static readonly Color PanelBg = new("1c1714");
    public static readonly Color PanelBorder = new("6e5a36");
    public static readonly Color Inset = new("151110");
    public static readonly Color Track = new("2c2420");
    public static readonly Color Divider = new("3a2f24");
    public static readonly Color Outline = new("120c07");
    public static readonly Color Red = new("ff6b5e");
    public static readonly Color Blue = new("87ceeb");
    public static readonly Color Green = new("8fd46a");
    public static readonly Color Grey = new("8a8a8a");
    public static readonly Color Clear = new(0, 0, 0, 0);

    private static Font? _bold;
    private static Font? _regular;
    private static bool _fontsLoaded;

    public static Font? Bold
    {
        get { LoadFonts(); return _bold; }
    }

    public static Font? Regular
    {
        get { LoadFonts(); return _regular; }
    }

    public static Color FromHex(string hex)
    {
        try { return Color.FromHtml(hex); }
        catch (Exception) { return Grey; }
    }

    public static Label Text(string text, int size, Color color, bool bold = false, int outline = 0)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        Font? font = bold ? Bold : Regular;
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (outline > 0)
        {
            label.AddThemeConstantOverride("outline_size", outline);
            label.AddThemeColorOverride("font_outline_color", Outline);
        }
        return label;
    }

    public static StyleBoxFlat Box(Color background, int radius, Color? border = null, int borderWidth = 0,
                                   float padX = 0, float padY = 0)
    {
        var box = new StyleBoxFlat { BgColor = background };
        box.SetCornerRadiusAll(radius);
        if (border is Color b && borderWidth > 0)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(borderWidth);
        }
        box.ContentMarginLeft = padX;
        box.ContentMarginRight = padX;
        box.ContentMarginTop = padY;
        box.ContentMarginBottom = padY;
        return box;
    }

    /// <summary>A fixed-size rounded shape: bar fills, dots, swatches.</summary>
    public static Panel Pill(Color color, float width, float height, bool highlight = false)
    {
        var pill = new Panel
        {
            CustomMinimumSize = new Vector2(width, height),
            Size = new Vector2(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        StyleBoxFlat box = Box(color, (int)Math.Ceiling(height / 2));
        if (highlight)
        {
            box.BorderColor = color.Lightened(0.35f);
            box.BorderWidthTop = Math.Max(1, (int)(height / 6));
        }
        pill.AddThemeStyleboxOverride("panel", box);
        return pill;
    }

    /// <summary>The character's icon, or a coloured badge with initials when there is none.</summary>
    public static Control CharacterIcon(Texture2D? texture, string nameForInitials, Color accent, float size)
    {
        if (texture != null)
        {
            return new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(size, size),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }
        var badge = new PanelContainer
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        badge.AddThemeStyleboxOverride("panel", Box(Track, (int)(size / 2), accent, 2));
        Label initials = Text(Initials(nameForInitials), (int)(size * 0.36f), accent, bold: true);
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(initials);
        return badge;
    }

    public static string Initials(string name)
    {
        string[] words = name.Replace("The ", "", StringComparison.OrdinalIgnoreCase)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return letters.Length > 0 ? letters : "?";
    }

    public static Button ActionButton(string text, int size = 16)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        if (Bold != null) button.AddThemeFontOverride("font", Bold);
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Gold);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_hover_pressed_color", Gold);
        button.AddThemeStyleboxOverride("normal", Box(Track, 8, PanelBorder, 1, 14, 6));
        button.AddThemeStyleboxOverride("hover", Box(Divider, 8, Gold, 1, 14, 6));
        button.AddThemeStyleboxOverride("pressed", Box(Divider, 8, Gold, 2, 14, 6));
        button.AddThemeStyleboxOverride("hover_pressed", Box(Divider, 8, Gold, 2, 14, 6));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    /// <summary>A toggle chip with an icon, used to pick a player on the Sources tab.</summary>
    public static Button PlayerChip(string text, Texture2D? icon, Color accent, ButtonGroup group)
    {
        Button chip = ActionButton(text, 15);
        chip.ToggleMode = true;
        chip.ButtonGroup = group;
        chip.Icon = icon;
        chip.AddThemeConstantOverride("icon_max_width", 26);
        chip.AddThemeConstantOverride("h_separation", 8);
        chip.AddThemeStyleboxOverride("pressed", Box(Divider, 8, accent, 2, 14, 6));
        chip.AddThemeStyleboxOverride("hover_pressed", Box(Divider, 8, accent, 2, 14, 6));
        return chip;
    }

    public static void StyleTabs(TabContainer tabs)
    {
        tabs.AddThemeStyleboxOverride("tab_selected", TabBox(Gold));
        tabs.AddThemeStyleboxOverride("tab_unselected", TabBox(Clear));
        tabs.AddThemeStyleboxOverride("tab_hovered", TabBox(Faint));
        tabs.AddThemeStyleboxOverride("tab_focus", new StyleBoxEmpty());
        tabs.AddThemeStyleboxOverride("tabbar_background", new StyleBoxEmpty());
        var panel = new StyleBoxFlat { BgColor = Clear, BorderColor = Divider, BorderWidthTop = 1, ContentMarginTop = 18 };
        tabs.AddThemeStyleboxOverride("panel", panel);
        tabs.AddThemeColorOverride("font_selected_color", Gold);
        tabs.AddThemeColorOverride("font_unselected_color", Muted);
        tabs.AddThemeColorOverride("font_hovered_color", Cream);
        if (Bold != null) tabs.AddThemeFontOverride("font", Bold);
        tabs.AddThemeFontSizeOverride("font_size", 19);
    }

    private static StyleBoxFlat TabBox(Color underline)
    {
        var box = new StyleBoxFlat { BgColor = Clear, BorderColor = underline, BorderWidthBottom = 3 };
        box.ContentMarginLeft = 16;
        box.ContentMarginRight = 16;
        box.ContentMarginTop = 6;
        box.ContentMarginBottom = 8;
        return box;
    }

    private static void LoadFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        _bold = LoadFont("res://themes/kreon_bold_shared.tres", "res://fonts/kreon_bold.ttf");
        _regular = LoadFont("res://themes/kreon_regular_shared.tres", "res://fonts/kreon_regular.ttf");
    }

    private static Font? LoadFont(params string[] paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (ResourceLoader.Exists(path) && ResourceLoader.Load<Font>(path) is Font font) return font;
            }
            catch (Exception)
            {
                // try the next candidate
            }
        }
        return null;
    }
}
```

- [ ] **Step 2: Shared widgets**

`src/RunRecap/UI/RecapWidgets.cs`:

```csharp
using System.Globalization;
using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>Data → styled controls, shared by the interactive panel and the exported summary card.</summary>
internal static class RecapWidgets
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(int value) => value.ToString("N0", Inv);

    public static HBoxContainer Row(int separation)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    public static VBoxContainer Column(int separation, string? name = null)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (name != null) column.Name = name;
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    public static T Center<T>(T control) where T : Control
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    /// <summary>A rounded track with a rounded fill; the fill is never narrower than it is tall.</summary>
    public static Control Bar(double fraction, Color color, float width, float height)
    {
        var track = new Panel { CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Track, (int)Math.Ceiling(height / 2)));
        if (fraction > 0)
            track.AddChild(RecapTheme.Pill(color, Math.Max(height, (float)(width * fraction)), height, highlight: true));
        return Center(track);
    }

    /// <summary>Icon, name + character, bar, big number (and share).</summary>
    public static Control PlayerRow(BarRow row, Texture2D? icon, float barWidth, bool showShare)
    {
        Color color = RecapTheme.FromHex(row.ColorHex);
        HBoxContainer line = Row(16);
        line.AddChild(Center(RecapTheme.CharacterIcon(icon, row.SubLabel.Length > 0 ? row.SubLabel : row.Label, color, 46)));

        VBoxContainer names = Column(-4);
        names.CustomMinimumSize = new Vector2(200, 0);
        Label name = RecapTheme.Text(row.Label, 21, RecapTheme.Cream, bold: true);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(name);
        if (row.SubLabel.Length > 0) names.AddChild(RecapTheme.Text(row.SubLabel, 15, RecapTheme.Muted));
        line.AddChild(Center(names));

        line.AddChild(Bar(row.Fraction, color, barWidth, 20));

        HBoxContainer value = Row(8);
        value.AddChild(Center(RecapTheme.Text(Num(row.Value), 27, RecapTheme.Cream, bold: true, outline: 5)));
        if (showShare && row.Share is double share)
            value.AddChild(Center(RecapTheme.Text($"{Math.Round(share * 100):0}%", 17, RecapTheme.Muted)));
        line.AddChild(Center(value));
        return line;
    }

    /// <summary>Kind tag, source name, slim bar, number.</summary>
    public static Control SourceRow(BarRow row, float barWidth, float labelWidth)
    {
        Color color = RecapTheme.FromHex(row.ColorHex);
        HBoxContainer line = Row(14);
        line.AddChild(Center(KindTag(row.SubLabel)));
        Label label = RecapTheme.Text(row.Label, 18, RecapTheme.Cream);
        label.CustomMinimumSize = new Vector2(labelWidth, 0);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(Center(label));
        line.AddChild(Bar(row.Fraction, color, barWidth, 14));
        line.AddChild(Center(RecapTheme.Text(Num(row.Value), 21, RecapTheme.Cream, bold: true)));
        return line;
    }

    public static Control Highlights(IReadOnlyList<Highlight> highlights)
    {
        var grid = new GridContainer { Columns = Math.Max(1, highlights.Count), MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        foreach (Highlight h in highlights)
        {
            var tile = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            tile.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Inset, 10, RecapTheme.Divider, 1, 16, 12));
            VBoxContainer column = Column(0);
            column.AddChild(RecapTheme.Text(h.Label, 14, RecapTheme.Muted));
            Label value = RecapTheme.Text(h.Value, 25, RecapTheme.Cream, bold: true);
            value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            column.AddChild(value);
            Label sub = RecapTheme.Text(h.Sub, 14, RecapTheme.Faint);
            sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            column.AddChild(sub);
            tile.AddChild(column);
            grid.AddChild(tile);
        }
        return grid;
    }

    public static Control Legend(IReadOnlyList<TimelineSeries> series, Func<string?, Texture2D?> icons)
    {
        HBoxContainer legend = Row(24);
        foreach (TimelineSeries s in series)
        {
            Color color = RecapTheme.FromHex(s.ColorHex);
            HBoxContainer item = Row(8);
            item.AddChild(Center(RecapTheme.CharacterIcon(icons(s.IconKey), s.Label, color, 28)));
            item.AddChild(Center(RecapTheme.Text(s.Label, 17, RecapTheme.Cream)));
            item.AddChild(Center(RecapTheme.Pill(color, 24, 6)));
            legend.AddChild(item);
        }
        return legend;
    }

    /// <summary>Legend + inset line chart of damage per fight, with nice gridlines and act separators.</summary>
    public static Control Chart(RecapView view, Func<string?, Texture2D?> icons, float width, float height)
    {
        VBoxContainer box = Column(12);
        box.AddChild(Legend(view.Timeline, icons));

        int fights = view.FightActs.Count;
        int max = view.Timeline.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
        if (fights == 0 || max == 0)
        {
            box.AddChild(RecapTheme.Text("No fights recorded yet.", 17, RecapTheme.Muted));
            return box;
        }

        int top = ChartMath.NiceCeiling(max);
        const float left = 56f, right = 18f, topPad = 30f, bottom = 12f;
        float plotW = width - left - right;
        float plotH = height - topPad - bottom;
        float X(int i) => fights == 1 ? left + plotW / 2 : left + i * plotW / (fights - 1);
        float Y(int v) => topPad + plotH - v * plotH / top;

        var inset = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.Inset, 10, RecapTheme.Divider, 1, 12, 12));
        var chart = new Control { CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddChild(chart);

        for (int g = 0; g <= 4; g++)
        {
            int value = top * g / 4;
            float y = Y(value);
            chart.AddChild(Line(new[] { new Vector2(left, y), new Vector2(left + plotW, y) }, RecapTheme.Divider, 1));
            chart.AddChild(At(RecapTheme.Text(Num(value), 13, RecapTheme.Faint), 0, y - 10));
        }

        var actLine = new Color(RecapTheme.Gold, 0.35f);
        var segmentStarts = new List<float> { left };
        foreach (int start in view.ActStarts)
        {
            float x = (X(start - 1) + X(start)) / 2;
            segmentStarts.Add(x);
            chart.AddChild(Line(new[] { new Vector2(x, 4), new Vector2(x, topPad + plotH) }, actLine, 2));
        }
        List<int> startIndexes = view.ActStarts.Prepend(0).ToList();
        for (int k = 0; k < startIndexes.Count; k++)
            chart.AddChild(At(RecapTheme.Text($"Act {view.FightActs[startIndexes[k]]}", 14, RecapTheme.Gold, bold: true),
                segmentStarts[k] + 8, 2));

        foreach (TimelineSeries s in view.Timeline)
        {
            Color color = RecapTheme.FromHex(s.ColorHex);
            Vector2[] points = s.Values.Select((v, i) => new Vector2(X(i), Y(v))).ToArray();
            chart.AddChild(Line(points, color, 3.5f));
            foreach (Vector2 p in points)
            {
                Panel dot = RecapTheme.Pill(color, 9, 9);
                dot.Position = p - new Vector2(4.5f, 4.5f);
                chart.AddChild(dot);
            }
        }

        box.AddChild(inset);
        box.AddChild(RecapTheme.Text(fights == 1 ? "1 fight" : $"{fights} fights", 14, RecapTheme.Faint));
        return box;
    }

    public static Control DefenseGrid(IReadOnlyList<DefenseRow> rows, Func<string?, Texture2D?> icons)
    {
        var grid = new GridContainer { Columns = 4, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 64);
        grid.AddThemeConstantOverride("v_separation", 16);
        grid.AddChild(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        grid.AddChild(RecapTheme.Text("Damage taken", 16, RecapTheme.Red, bold: true));
        grid.AddChild(RecapTheme.Text("Blocked", 16, RecapTheme.Blue, bold: true));
        grid.AddChild(RecapTheme.Text("Healed", 16, RecapTheme.Green, bold: true));
        foreach (DefenseRow row in rows)
        {
            Color color = RecapTheme.FromHex(row.ColorHex);
            HBoxContainer who = Row(12);
            who.AddChild(Center(RecapTheme.CharacterIcon(icons(row.IconKey), row.Label, color, 38)));
            Label name = RecapTheme.Text(row.Label, 20, RecapTheme.Cream, bold: true);
            name.CustomMinimumSize = new Vector2(180, 0);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            who.AddChild(Center(name));
            grid.AddChild(who);
            grid.AddChild(Center(RecapTheme.Text(Num(row.Taken), 26, RecapTheme.Cream, bold: true)));
            grid.AddChild(Center(RecapTheme.Text(Num(row.Blocked), 26, RecapTheme.Cream, bold: true)));
            grid.AddChild(Center(RecapTheme.Text(Num(row.Healed), 26, RecapTheme.Cream, bold: true)));
        }
        return grid;
    }

    private static Control KindTag(string kind)
    {
        var tag = new PanelContainer { CustomMinimumSize = new Vector2(76, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        tag.AddThemeStyleboxOverride("panel",
            RecapTheme.Box(kind.Length > 0 ? RecapTheme.Track : RecapTheme.Clear, 6, padX: 6, padY: 3));
        Label text = RecapTheme.Text(kind.ToUpperInvariant(), 12, RecapTheme.Muted, bold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        tag.AddChild(text);
        return tag;
    }

    private static Line2D Line(Vector2[] points, Color color, float width)
    {
        if (points.Length == 1)
            points = new[] { points[0] - new Vector2(4, 0), points[0] + new Vector2(4, 0) };
        return new Line2D
        {
            Points = points,
            DefaultColor = color,
            Width = width,
            Antialiased = true,
            JointMode = Line2D.LineJointMode.Round,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
    }

    private static Control At(Control control, float x, float y)
    {
        control.Position = new Vector2(x, y);
        return control;
    }
}
```

- [ ] **Step 3: Interactive panel**

`src/RunRecap/UI/RecapPanel.cs`:

```csharp
using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>What the caller needs to drive an open panel (switch tabs, show a status message).</summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status);

/// <summary>The interactive recap: dimmed backdrop, framed panel, header, and four tabs.</summary>
internal static class RecapPanel
{
    public const string IdleHint = "F8 toggles";
    private const float PanelWidth = 1000f;
    private const float BarWidth = 420f;

    public static PanelHandle Create(RecapView view, Func<string?, Texture2D?> icons, Action onClose,
                                     Action<PanelHandle> onSave)
    {
        var root = new Control { Name = "RunRecap", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) onClose();
        };
        root.AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        StyleBoxFlat frame = RecapTheme.Box(RecapTheme.PanelBg, 14, RecapTheme.PanelBorder, 2, 28, 22);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 24;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);

        VBoxContainer column = RecapWidgets.Column(14);
        panel.AddChild(column);

        Label status = RecapTheme.Text(IdleHint, 14, RecapTheme.Faint);
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 440) };
        RecapTheme.StyleTabs(tabs);
        var handle = new PanelHandle(root, tabs, status);

        column.AddChild(Header(view, status, onClose, () => onSave(handle)));
        tabs.AddChild(OverviewTab(view, icons));
        tabs.AddChild(SourcesTab(view, icons));
        tabs.AddChild(TimelineTab(view, icons));
        tabs.AddChild(DefenseTab(view, icons));
        column.AddChild(tabs);
        return handle;
    }

    private static Control Header(RecapView view, Label status, Action onClose, Action onSave)
    {
        HBoxContainer row = RecapWidgets.Row(16);
        row.AddChild(RecapWidgets.Center(RecapTheme.Text("Run recap", 36, RecapTheme.Gold, bold: true, outline: 8)));
        Color tone = view.Victory switch { true => RecapTheme.Gold, false => RecapTheme.Red, null => RecapTheme.Muted };
        row.AddChild(RecapWidgets.Center(RecapTheme.Text(view.Header, 18, tone)));
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(RecapWidgets.Center(status));
        Button save = RecapTheme.ActionButton("Save image");
        save.Pressed += onSave;
        row.AddChild(RecapWidgets.Center(save));
        Button close = RecapTheme.ActionButton("Close");
        close.Pressed += onClose;
        row.AddChild(RecapWidgets.Center(close));
        return row;
    }

    private static Control OverviewTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(12, "Overview");
        foreach (BarRow row in view.Overview)
            box.AddChild(RecapWidgets.PlayerRow(row, icons(row.IconKey), BarWidth, showShare: true));
        if (view.Overview.All(r => r.Value == 0))
            box.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        box.AddChild(RecapWidgets.Highlights(view.Highlights));
        return box;
    }

    private static Control SourcesTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(16, "Sources");
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 10);
        chips.AddThemeConstantOverride("v_separation", 8);
        VBoxContainer rows = RecapWidgets.Column(10);
        var group = new ButtonGroup();

        void ShowPlayer(int index)
        {
            foreach (Node child in rows.GetChildren()) child.QueueFree();
            IReadOnlyList<BarRow> sourceRows = view.Sources[index].Rows;
            foreach (BarRow row in sourceRows) rows.AddChild(RecapWidgets.SourceRow(row, BarWidth, 260));
            if (sourceRows.Count == 0) rows.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        }

        for (int i = 0; i < view.Sources.Count; i++)
        {
            int index = i;
            SourcesView source = view.Sources[i];
            Button chip = RecapTheme.PlayerChip(source.PlayerLabel, icons(source.IconKey),
                RecapTheme.FromHex(source.ColorHex), group);
            chip.ButtonPressed = i == 0;
            chip.Pressed += () => ShowPlayer(index);
            chips.AddChild(chip);
        }

        box.AddChild(chips);
        box.AddChild(rows);
        if (view.Sources.Count > 0) ShowPlayer(0);
        else rows.AddChild(RecapTheme.Text("No damage recorded yet.", 17, RecapTheme.Muted));
        return box;
    }

    private static Control TimelineTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(0, "Timeline");
        box.AddChild(RecapWidgets.Chart(view, icons, 910, 300));
        return box;
    }

    private static Control DefenseTab(RecapView view, Func<string?, Texture2D?> icons)
    {
        VBoxContainer box = RecapWidgets.Column(0, "Defense");
        box.AddChild(RecapWidgets.DefenseGrid(view.Defense, icons));
        return box;
    }
}
```

- [ ] **Step 4: Export card, exporter, and delay helper**

`src/RunRecap/UI/SummaryCard.cs`:

```csharp
using System.Globalization;
using Godot;
using RunRecap.Core;

namespace RunRecap.UI;

/// <summary>A single shareable image of the whole recap: overview, highlights, top sources, timeline, defense.</summary>
internal static class SummaryCard
{
    public const int Width = 1200;
    private const int Pad = 40;
    private const float Inner = Width - 2 * Pad;

    public static Control Create(RecapView view, Func<string?, Texture2D?> icons)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0) };
        card.AddThemeStyleboxOverride("panel", RecapTheme.Box(RecapTheme.PanelBg, 0, RecapTheme.PanelBorder, 3, Pad, 34));
        VBoxContainer column = RecapWidgets.Column(22);
        card.AddChild(column);

        HBoxContainer header = RecapWidgets.Row(18);
        header.AddChild(RecapWidgets.Center(RecapTheme.Text("Run recap", 46, RecapTheme.Gold, bold: true, outline: 10)));
        Color tone = view.Victory switch { true => RecapTheme.Gold, false => RecapTheme.Red, null => RecapTheme.Muted };
        header.AddChild(RecapWidgets.Center(RecapTheme.Text(view.Header, 21, tone)));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(RecapWidgets.Center(RecapTheme.Text(
            DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture), 17, RecapTheme.Muted)));
        column.AddChild(header);
        column.AddChild(new ColorRect { Color = RecapTheme.Divider, CustomMinimumSize = new Vector2(0, 1) });

        foreach (BarRow row in view.Overview)
            column.AddChild(RecapWidgets.PlayerRow(row, icons(row.IconKey), 600, showShare: true));
        column.AddChild(RecapWidgets.Highlights(view.Highlights));

        column.AddChild(Section("Top sources"));
        column.AddChild(TopSources(view, icons));

        column.AddChild(Section("Damage per fight"));
        column.AddChild(RecapWidgets.Chart(view, icons, Inner - 26, 260));

        column.AddChild(Section("Defense"));
        column.AddChild(RecapWidgets.DefenseGrid(view.Defense, icons));

        Label footer = RecapTheme.Text("Run Recap · Slay the Spire 2", 14, RecapTheme.Faint);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        column.AddChild(footer);
        return card;
    }

    private static Control TopSources(RecapView view, Func<string?, Texture2D?> icons)
    {
        int columns = Math.Clamp(view.Sources.Count, 1, 4);
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 24);
        float colWidth = (Inner - 28 * (columns - 1)) / columns;
        foreach (SourcesView s in view.Sources)
        {
            Color color = RecapTheme.FromHex(s.ColorHex);
            VBoxContainer col = RecapWidgets.Column(8);
            col.CustomMinimumSize = new Vector2(colWidth, 0);

            HBoxContainer who = RecapWidgets.Row(10);
            who.AddChild(RecapWidgets.Center(RecapTheme.CharacterIcon(icons(s.IconKey), s.PlayerLabel, color, 30)));
            Label name = RecapTheme.Text(s.PlayerLabel, 17, RecapTheme.Cream, bold: true);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            who.AddChild(RecapWidgets.Center(name));
            col.AddChild(who);

            foreach (BarRow row in s.Rows.Where(r => !r.Label.StartsWith(RecapBuilder.OtherPrefix + " (", StringComparison.Ordinal)).Take(3))
            {
                HBoxContainer line = RecapWidgets.Row(8);
                Label label = RecapTheme.Text(row.Label, 16, RecapTheme.Cream);
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.AddChild(label);
                line.AddChild(RecapTheme.Text(RecapWidgets.Num(row.Value), 16, RecapTheme.Cream, bold: true));
                col.AddChild(line);
                col.AddChild(RecapWidgets.Bar(row.Fraction, color, colWidth, 6));
            }
            grid.AddChild(col);
        }
        return grid;
    }

    private static Control Section(string title) => RecapTheme.Text(title, 24, RecapTheme.Gold, bold: true, outline: 6);
}
```

`src/RunRecap/UI/PngExporter.cs`:

```csharp
using Godot;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>Renders a control offscreen in a SubViewport and saves it as a PNG. Never throws.</summary>
internal static class PngExporter
{
    /// <param name="onDone">Called with null on success, or an error message.</param>
    public static void Save(Control content, int width, string path, Action<string?> onDone)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, 4096),
            TransparentBg = true,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewport.AddChild(content);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);

        // Frame 1: layout runs and gives us the content height. Frame 2: render at that height, then read back.
        Later.Run(0.15, () =>
        {
            int height = Mathf.CeilToInt(Math.Max(content.Size.Y, content.GetCombinedMinimumSize().Y));
            viewport.Size = new Vector2I(width, Math.Clamp(height, 1, 8192));
            Later.Run(0.15, () =>
            {
                string? error = null;
                try
                {
                    Image image = viewport.GetTexture().GetImage();
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    Error result = image.SavePng(path);
                    if (result != Error.Ok) error = result.ToString();
                }
                catch (Exception e)
                {
                    error = e.Message;
                    Tracker.LogError("export", e);
                }
                viewport.QueueFree();
                onDone(error);
            });
        });
    }
}
```

`src/RunRecap/UI/Later.cs`:

```csharp
using Godot;
using RunRecap.Game;

namespace RunRecap.UI;

internal static class Later
{
    /// <summary>Runs <paramref name="action"/> after a real-time delay on the main thread. Errors are logged, never thrown.</summary>
    public static void Run(double seconds, Action action)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () =>
        {
            try { action(); }
            catch (Exception e) { Tracker.LogError("Later", e); }
        };
    }
}
```

- [ ] **Step 5: Dev preview**

`src/RunRecap/UI/DevPreview.cs`:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Models;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>
/// Developer-only visual check. If data/preview.flag exists, opens the panel with sample data shortly after start-up,
/// saves a screenshot of each tab plus the exported card into data/, then closes. Inert otherwise.
/// </summary>
internal static class DevPreview
{
    private static readonly string[] TabNames = { "overview", "sources", "timeline", "defense" };

    public static void StartIfFlagged(string dataDir)
    {
        if (!File.Exists(Path.Combine(dataDir, "preview.flag"))) return;
        Later.Run(10, () => Run(dataDir));
    }

    private static void Run(string dataDir)
    {
        (RecapView view, Func<string?, Texture2D?> icons) = Sample();
        PanelHandle handle = RecapUi.ShowView(view, icons);
        CaptureTab(0);

        void CaptureTab(int index)
        {
            if (index >= TabNames.Length)
            {
                PngExporter.Save(SummaryCard.Create(view, icons), SummaryCard.Width,
                    Path.Combine(dataDir, "preview-5-export.png"), error =>
                    {
                        Tracker.Note(error == null ? "preview done" : $"preview export failed: {error}");
                        RecapUi.Hide();
                    });
                return;
            }
            handle.Tabs.CurrentTab = index;
            Later.Run(0.7, () =>
            {
                Image screen = ((SceneTree)Engine.GetMainLoop()).Root.GetTexture().GetImage();
                screen.SavePng(Path.Combine(dataDir, $"preview-{index + 1}-{TabNames[index]}.png"));
                CaptureTab(index + 1);
            });
        }
    }

    private static (RecapView, Func<string?, Texture2D?>) Sample()
    {
        CharacterModel[] characters = ModelDb.AllCharacters.Take(3).ToArray();
        string[] names = { "Ash", "Mika", "Sam" };
        List<PlayerInfo> players = characters
            .Select((c, i) => new PlayerInfo((ulong)(i + 1), names[i], GameReader.CharacterName(c),
                GameReader.CharacterHex(c, i), c.Id.Entry))
            .ToList();

        string[][] pools =
        {
            new[] { "Bash", "Heavy Blade", "Whirlwind", "Strike", "Pommel Strike", "Hemokinesis", "Burning Pact", "Clash" },
            new[] { "Poison", "Blade Dance", "Shiv", "Dagger Throw", "Neutralize" },
            new[] { "Lightning", "Ball Lightning", "Cold Snap", "Zap" },
        };
        var stats = new RunStats();
        var rng = new Random(7);
        int floor = 1;
        for (int act = 1; act <= 3; act++)
        {
            for (int fight = 0; fight < 5; fight++, floor += 3)
            {
                stats.BeginFight(act, floor, fight == 4 ? "Boss" : "Hallway fight");
                for (int p = 0; p < players.Count; p++)
                {
                    string[] pool = pools[p % pools.Length];
                    for (int hit = 0; hit < 4; hit++)
                    {
                        string label = pool[rng.Next(pool.Length)];
                        SourceKind kind = label == "Poison" ? SourceKind.Power
                            : label == "Lightning" ? SourceKind.Orb
                            : SourceKind.Card;
                        int amount = rng.Next(8, 30) * act * (fight == 4 ? 2 : 1);
                        stats.RecordDamage(players[p].NetId, new SourceRef(kind, label.ToUpperInvariant(), label), amount);
                    }
                    stats.RecordBlocked(players[p].NetId, rng.Next(10, 40) * act);
                }
                stats.EndFight();
            }
        }
        Dictionary<ulong, DefenseTotals> defense = players.ToDictionary(
            p => p.NetId, _ => new DefenseTotals(rng.Next(150, 400), rng.Next(60, 200)));

        RecapView view = RecapBuilder.Build(stats, players, defense, "Victory · Act 3 · Floor 43", victory: true);
        Func<string?, Texture2D?> icons = id =>
            characters.FirstOrDefault(c => c.Id.Entry == id) is CharacterModel c ? GameReader.CharacterIcon(c) : null;
        return (view, icons);
    }
}
```

- [ ] **Step 6: Orchestration**

`src/RunRecap/UI/RecapUi.cs`:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>Owns the overlay: F8 polling, the canvas layer, opening on the end-of-run screen, and image export.</summary>
internal static class RecapUi
{
    private static CanvasLayer? _layer;
    private static Control? _panel;
    private static bool _f8WasDown;

    public static void Install()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += OnFrame;
            DevPreview.StartIfFlagged(Tracker.DataDir);
        }
        else
        {
            Log.Warn("[RunRecap] no SceneTree at init; F8 toggle unavailable");
        }
    }

    public static void Toggle()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) Hide();
        else Show();
    }

    /// <summary>Opens the recap for the current (or just-finished) run.</summary>
    public static void Show()
    {
        IRunState? run = Tracker.CurrentRun;
        if (run == null) return;
        RunStats stats = Tracker.Stats;
        bool? victory = stats.Finished ? stats.Victory : null;
        RecapView view = RecapBuilder.Build(stats, GameReader.Players(run), GameReader.Defense(run),
            GameReader.Header(run, victory), victory);
        ShowView(view, id => GameReader.CharacterIcon(run, id));
    }

    public static PanelHandle ShowView(RecapView view, Func<string?, Texture2D?> icons)
    {
        Hide();
        PanelHandle handle = RecapPanel.Create(view, icons, Hide, h => Export(view, icons, h));
        _panel = handle.Root;
        EnsureLayer().AddChild(handle.Root);
        return handle;
    }

    public static void Hide()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
        _panel = null;
    }

    /// <summary>Called after the victory/defeat screen is ready: open the recap once its banner has animated in.</summary>
    public static void OnGameOverScreen(NGameOverScreen screen)
    {
        screen.TreeExiting += Hide;
        Later.Run(1.5, () =>
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsInsideTree()) Show();
        });
    }

    private static void Export(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        string result = view.Victory switch { true => "victory", false => "defeat", null => "in-progress" };
        string folder = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures), "Run Recap");
        string path = Path.Combine(folder, $"run-{DateTime.Now:yyyy-MM-dd_HHmm}-{result}.png");
        handle.Status.Text = "Saving...";
        PngExporter.Save(SummaryCard.Create(view, icons), SummaryCard.Width, path, error =>
        {
            Tracker.Note(error == null ? $"exported {path}" : $"export failed: {error}");
            if (GodotObject.IsInstanceValid(handle.Status))
                handle.Status.Text = error == null ? "Saved to Pictures\\Run Recap" : "Couldn't save the image";
        });
    }

    private static void OnFrame()
    {
        try
        {
            bool down = Input.IsKeyPressed(Key.F8);
            if (down && !_f8WasDown) Toggle();
            _f8WasDown = down;
        }
        catch (Exception e)
        {
            Tracker.LogError("F8", e);
        }
    }

    private static CanvasLayer EnsureLayer()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return _layer;
        _layer = new CanvasLayer { Layer = 100, Name = "RunRecapLayer" };
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
        return _layer;
    }
}
```

- [ ] **Step 7: Build and test**

Run:
```powershell
dotnet build RunRecap.sln -c Release --nologo -v q
dotnet run --project tests\RunRecap.Tests
```
Expected: `Build succeeded.`, 0 errors, then `27/27 passed`.

---

### Task 4: Visual verification loop (Claude)

**Files:** only visual constants in `UI/*.cs`, as the screenshots require.

- [ ] **Step 1: Deploy with the preview flag**

Make sure the game is closed. Then run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1
$data = '<game>\mods\RunRecap\data'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item "$data\preview-*.png" -ErrorAction SilentlyContinue
New-Item -ItemType File -Force "$data\preview.flag" | Out-Null
```

- [ ] **Step 2: Launch and wait for the screenshots**

Run `Start-Process 'steam://rungameid/2868840'`. Poll for up to 3 minutes until `$data\preview-5-export.png` exists. Then check `events.log` for `preview done` and `godot.log` for `RunRecap` errors.
Expected: `preview-1-overview.png` … `preview-4-defense.png` and `preview-5-export.png` all exist.

- [ ] **Step 3: Review**

Read each PNG and check:
- Kreon is applied (the slab serif in the title and numbers).
- Character icons show; initials are acceptable only if icons fail.
- Nothing is clipped or overflowing the panel.
- Tabs are underlined gold.
- Bars are rounded, the chart has gridlines and act labels, and the export card is complete top to bottom.

Fix any issue by adjusting sizes and constants in `UI/*.cs`. Close the game, redeploy, and repeat Steps 1–3, at most 3 rounds.

- [ ] **Step 4: Remove the flag and redeploy clean**

```powershell
Remove-Item "$data\preview.flag"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1
```
Close the game if Claude launched it.

---

### Task 5: The owner checks it in a real run

- [ ] The owner opens F8 in a run and confirms that the look, the Steam name and the character icon are right.
- [ ] The owner presses **Save image** and confirms the PNG appears in `Pictures\Run Recap`.
- [ ] Claude reads `events.log` for `exported …` or `export failed …` lines.
