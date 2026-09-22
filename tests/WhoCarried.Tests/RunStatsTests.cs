using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunStatsTests
{
    private static readonly SourceRef Strike = new(SourceKind.Card, "STRIKE", "Strike");
    private static readonly SourceRef Bash = new(SourceKind.Card, "BASH", "Bash");

    [Test]
    public static void DamageAccumulatesPerPlayerAndSource()
    {
        var s = new RunStats();
        s.RecordDamage(1, Strike, 6);
        s.RecordDamage(1, Strike, 6);
        s.RecordDamage(1, Bash, 8);
        s.RecordDamage(2, Strike, 5);
        Check.Equal(20, s.Get(1)!.DamageDealt, "p1 total");
        Check.Equal(12, s.Get(1)!.Sources[Strike.Key].Amount, "p1 strike");
        Check.Equal(8, s.Get(1)!.Sources[Bash.Key].Amount, "p1 bash");
        Check.Equal("Bash", s.Get(1)!.Sources[Bash.Key].Label, "label kept");
        Check.Equal(5, s.Get(2)!.DamageDealt, "p2 total");
    }

    [Test]
    public static void NullPlayerGoesToUnattributed()
    {
        var s = new RunStats();
        s.RecordDamage(null, Strike, 4);
        Check.Equal(4, s.Get(null)!.DamageDealt, "unattributed total");
        Check.True(s.Players.ContainsKey(RunStats.UnattributedKey), "stored under the unattributed key");
    }

    [Test]
    public static void ZeroDamageIsIgnored()
    {
        var s = new RunStats();
        s.RecordDamage(1, Strike, 0);
        Check.True(s.Get(1) == null, "no entry created");
    }

    [Test]
    public static void FightBucketsOnlyCountDamageInsideTheFight()
    {
        var s = new RunStats();
        s.BeginFight(act: 1, floor: 3, label: "Jaw Worm");
        s.RecordDamage(1, Strike, 10);
        s.RecordDamage(2, Strike, 4);
        s.EndFight();
        s.RecordDamage(1, Strike, 99);
        Check.Equal(1, s.Fights.Count, "fight count");
        Check.Equal(10, s.Fights[0].DamageByPlayer[RunStats.KeyFor(1)], "p1 in fight");
        Check.Equal(4, s.Fights[0].DamageByPlayer[RunStats.KeyFor(2)], "p2 in fight");
        Check.Equal(3, s.Fights[0].Floor, "floor");
        Check.Equal("Jaw Worm", s.Fights[0].Label, "label");
        Check.Equal(109, s.Get(1)!.DamageDealt, "run total counts everything");
    }

    [Test]
    public static void BlockedAccumulatesAndIgnoresZero()
    {
        var s = new RunStats();
        s.RecordBlocked(1, 5);
        s.RecordBlocked(1, 7);
        s.RecordBlocked(1, 0);
        Check.Equal(12, s.Get(1)!.Blocked, "blocked");
    }

    [Test]
    public static void PetTankedAccumulatesForTheOwnerAndIgnoresZero()
    {
        var s = new RunStats();
        s.RecordPetTanked(1, 7);
        s.RecordPetTanked(1, 4);
        s.RecordPetTanked(1, 0);
        s.RecordPetTanked(1, -3);
        Check.Equal(11, s.Get(1)!.PetTanked, "pet tanked");
        Check.Equal(0, s.Get(1)!.Blocked, "not block");
        Check.Equal(0, s.Get(1)!.DamageDealt, "not damage");
        Check.True(s.Get(2) == null, "nobody else");
    }
}
