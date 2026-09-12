using RunRecap.Core;

namespace RunRecap.Tests;

public static class SourceArtTests
{
    [Test]
    public static void SourcesPointAtTheirPictures()
    {
        Check.Equal("card:SOVEREIGN_BLADE", SourceArt.Key("Card:SOVEREIGN_BLADE"), "card portrait");
        Check.Equal("relic:STONE_CALENDAR", SourceArt.Key("Relic:STONE_CALENDAR"), "relic icon");
        Check.Equal("power:DOOM_POWER", SourceArt.Key("Power:DOOM_POWER"), "power icon, same key as the debuffs");
        Check.Equal("orb:LIGHTNING_ORB", SourceArt.Key("Orb:LIGHTNING_ORB"), "orb icon");
        Check.Equal("model:SOMEMOD-FLAMMA_RUNE", SourceArt.Key("Other:SOMEMOD-FLAMMA_RUNE"), "a mod's own kind of thing, by its model");
        Check.Equal("card:UNLEASH", SourceArt.Key("Pet:OSTY>UNLEASH"), "a pet's hit shows the card that sent it");
        Check.Equal<string?>(null, SourceArt.Key("Pet:OSTY"), "a pet on its own has no picture");
        Check.Equal<string?>(null, SourceArt.Key("Unknown:UNKNOWN"), "unknown");
    }

    [Test]
    public static void ModdedIdsReadAsNames()
    {
        Check.Equal("Flamma Rune", SourceArt.Readable("SOMEMOD-FLAMMA_RUNE"), "mod prefix goes, words title-cased");
        Check.Equal("Flamma", SourceArt.Readable("SOMEMOD-FLAMMA_RUNE", "Rune"), "its own kind word would only repeat");
        Check.Equal("Minion Phase", SourceArt.Readable("SOMEMOD-MINION_PHASE_POWER", "Power"), "same for a power");
        Check.Equal("Lightning", SourceArt.Readable("LIGHTNING_ORB", "Orb"), "no prefix");
        Check.Equal("Rune", SourceArt.Readable("RUNE", "Rune"), "nothing left but the kind: keep it");
    }

    [Test]
    public static void SourceRowsCarryArtAndDefenseRowsTheLowestHp()
    {
        var stats = new RunStats();
        stats.BeginFight(1, 3, "Seapunk", "elite");
        stats.RecordDamage(1, new SourceRef(SourceKind.Card, "STRIKE_REGENT", "Strike"), 10);
        stats.EndFight();
        stats.RecordHp(1, 9, 70);
        var players = new[] { new PlayerInfo(1, "Wren", "The Regent", "ffa518", "REGENT") };
        RecapView view = RecapBuilder.Build(stats, players, new Dictionary<ulong, DefenseTotals> { [1] = new(20, 5, 30, 70) },
            "Act 1", facts: new RunFacts(3, 6, 4899, "SEED"));
        Check.Equal("card:STRIKE_REGENT", view.Sources[0].Rows[0].ArtKey, "source art");
        Check.Equal("elite", view.FightPoints[0].Room, "fight room");
        Check.Equal(9, view.Defense[0].LowestHp, "the lower of the two lows");
        Check.Equal("The Regent", view.Defense[0].Character, "character");
        Check.Equal(6, view.Facts!.Ascension, "facts pass through");
    }

    [Test]
    public static void DamageAddsUpByKindWithTheBiggestOfEach()
    {
        var stats = new RunStats();
        stats.BeginFight(1, 2, "Toadpoles", "monster");
        stats.RecordDamage(1, new SourceRef(SourceKind.Card, "STRIKE_DEFECT", "Strike"), 6);
        stats.RecordDamage(1, new SourceRef(SourceKind.Orb, "LIGHTNING_ORB", "Lightning"), 16);
        stats.RecordDamage(1, new SourceRef(SourceKind.Card, "ZAP", "Zap"), 2);
        stats.RecordDamage(1, SourceRef.Unknown, 3);
        stats.EndFight();
        var players = new[] { new PlayerInfo(1, "You", "The Defect", "5ec2e0", "DEFECT") };
        RecapView view = RecapBuilder.Build(stats, players, new Dictionary<ulong, DefenseTotals>(), "Act 1");
        string kinds = string.Join(" | ", view.Sources[0].KindTotals.Select(k => $"{k.Kind} {k.Amount} x{k.Sources} {k.TopLabel} {k.TopArtKey}"));
        Check.Equal("Orb 16 x1 Lightning orb:LIGHTNING_ORB | Card 8 x2 Strike card:STRIKE_DEFECT | Other 3 x1 Unknown ", kinds, "by kind");
    }
}
