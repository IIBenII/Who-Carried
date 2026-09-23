using System.Text.Json;
using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RecapJsonTests
{
    [Test]
    public static void RoundTripKeepsTheRecapAndSkipsComputedFields()
    {
        var view = new RecapView(
            "Victory on floor 51",
            new[]
            {
                new BarRow("Ada", "Ironclad", 120, 1, 0.75, "d85a30", Bonus: 8, BlockRemoved: 15, Award: "Heavy hitter",
                    Badges: new[] { new BadgeInfo("OVERKILL", "gold", "Overkill", "Deal a lot") }),
            },
            new[]
            {
                new SourcesView("Ada · Ironclad", new[] { new BarRow("Bash", "Card", 120, 1, null, "d85a30") }, "d85a30",
                    Created: new[] { new CreatedCard("Shiv", 3) }),
            },
            new[] { new TimelineSeries("Ada", "d85a30", new[] { 40, 80 }) },
            new[] { 1, 1 },
            new[] { 0 },
            new[] { new FightPoint(1, 1, "Jaw Worm", "monster") },
            new[] { new DefenseRow("Ada", "d85a30", 30, 20, 10, Character: "Ironclad", LowestHp: 12, LowestHpMax: 80) },
            new[] { new Highlight("Hardest fight", "Jaw Worm", "floor 1") },
            true,
            new[]
            {
                new DeckView(1, "Ada · Ironclad", "d85a30", "IRONCLAD", 1, new[]
                {
                    new DeckEntry("BASH", "Bash", "Attack", "Starter", 1, 0, 120),
                }),
            },
            new DebuffsView(
                new[]
                {
                    new DebuffGroup("Vulnerable", "VULNERABLE_POWER", 4, new[]
                    {
                        new DebuffBar("Ada", "d85a30", null, 4, 1, Bonus: 8),
                    }, BonusTotal: 8),
                },
                0,
                Array.Empty<DebuffReceivedRow>()),
            "Ada set up 8",
            "",
            new[] { new Award("Heavy hitter", 1, "Ada", "d85a30", null, "120", "damage in one hit") },
            null,
            new RunFacts(51, 5, 3723, "SEED"),
            "SEED:100");

        string json = JsonSerializer.Serialize(view, RecapJson.Default.RecapView);
        RecapView? back = JsonSerializer.Deserialize(json, RecapJson.Default.RecapView);

        Check.True(json.Contains("\"header\"", StringComparison.Ordinal), "camel case");
        Check.True(!json.Contains("badgeList", StringComparison.Ordinal), "no computed badge list");
        Check.True(!json.Contains("badgesKnown", StringComparison.Ordinal), "no computed flag");
        Check.True(!json.Contains("\"costs\"", StringComparison.Ordinal), "no computed debuff costs");
        Check.True(back != null, "deserialized");
        Check.Equal("Victory on floor 51", back!.Header, "header");
        Check.Equal(true, back.Victory, "victory");
        Check.Equal("Ada", back.Overview[0].Label, "player");
        Check.Equal(120, back.Overview[0].Value, "damage");
        Check.Equal(8, back.Overview[0].Bonus, "bonus");
        Check.Equal("Overkill", back.Overview[0].BadgeList[0].Title, "badge");
        Check.Equal("badge:OVERKILL", back.Overview[0].BadgeList[0].IconKey, "badge icon still derived");
        Check.Equal("Shiv", back.Sources[0].CreatedCards[0].Label, "created card");
        Check.Equal(80, back.Timeline[0].Values[1], "second fight");
        Check.Equal("Jaw Worm", back.FightPoints[0].Label, "fight");
        Check.Equal(12, back.Defense[0].LowestHp, "lowest hp");
        Check.Equal("Bash", back.Decks[0].Entries[0].Label, "deck card");
        Check.Equal(8, back.Debuffs.Applied[0].BonusTotal, "debuff bonus");
        Check.Equal("SEED", back.Facts!.Seed, "seed");
        Check.Equal(51, back.Facts.Floor, "floor");
        Check.Equal("SEED:100", back.RunKey, "run key");
        Check.True(back.Badges == null, "badges not known yet");
    }
}
