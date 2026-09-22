using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DebuffBonusTests
{
    [Test]
    public static void VulnerableAddsHalfOfTheBaseDamage()
    {
        Check.Equal(5, DebuffBonus.Bonus(15m, 1.5m, blocked: 0, hpRemoved: 15), "10 base -> 15 with Vulnerable");
    }

    [Test]
    public static void OnlyExtraHpCountsNotExtraBlockRemoved()
    {
        Check.Equal(5, DebuffBonus.Bonus(15m, 1.5m, blocked: 8, hpRemoved: 7), "8 block: 2 -> 7 HP");
        Check.Equal(3, DebuffBonus.Bonus(15m, 1.5m, blocked: 12, hpRemoved: 3), "12 block: 0 -> 3 HP");
        Check.Equal(0, DebuffBonus.Bonus(15m, 1.5m, blocked: 15, hpRemoved: 0), "fully blocked: no HP either way");
        Check.Equal(0, DebuffBonus.Bonus(15m, 1.5m, blocked: 4, hpRemoved: 1), "Intangible behind block");
    }

    [Test]
    public static void HpDifferenceComparesTwoVersionsOfAHit()
    {
        Check.Equal(6, DebuffBonus.HpDifference(16m, 10m, block: 0, hpCap: 50), "Strength back: 10 -> 16");
        Check.Equal(3, DebuffBonus.HpDifference(16m, 10m, block: 13, hpCap: 50), "block soaks the first 13");
        Check.Equal(0, DebuffBonus.HpDifference(16m, 10m, block: 20, hpCap: 50), "blocked either way");
        Check.Equal(0, DebuffBonus.HpDifference(16m, 10m, block: 0, hpCap: 8), "lethal either way");
        Check.Equal(0, DebuffBonus.HpDifference(10m, 16m, block: 0, hpCap: 50), "never negative");
    }

    [Test]
    public static void SplitIndexedSharesByPosition()
    {
        int[] shares = DebuffBonus.SplitIndexed(10, new[] { 6m, 0m, 4m });
        Check.Equal("6,0,4", string.Join(",", shares), "by weight, zero weight gets nothing");
        Check.Equal(7, DebuffBonus.SplitIndexed(7, new[] { 1m, 1m, 1m }).Sum(), "adds up");
    }

    [Test]
    public static void KillingBlowOnlyCountsHpThatWouldHaveSurvived()
    {
        Check.Equal(0, DebuffBonus.Bonus(15m, 1.5m, blocked: 0, hpRemoved: 4), "4 HP left: dies either way");
        Check.Equal(2, DebuffBonus.Bonus(15m, 1.5m, blocked: 0, hpRemoved: 12), "12 HP left: 10 -> 12");
    }

    [Test]
    public static void CappedHitsGetNoBonus()
    {
        Check.Equal(0, DebuffBonus.Bonus(15m, 1.5m, blocked: 0, hpRemoved: 1), "Intangible caps HP loss at 1");
    }

    [Test]
    public static void OddNumbersTruncateLikeTheGame()
    {
        // 7 base * 1.5 = 10.5 -> 10 HP; without = 7.
        Check.Equal(3, DebuffBonus.Bonus(10.5m, 1.5m, blocked: 0, hpRemoved: 10), "7 -> 10");
        // Paper Phrog: 1.75.
        Check.Equal(5, DebuffBonus.Bonus(12.25m, 1.75m, blocked: 0, hpRemoved: 12), "7 -> 12");
        Check.Equal(0, DebuffBonus.Bonus(10m, 1m, blocked: 0, hpRemoved: 10), "no multiplier");
    }

    [Test]
    public static void BonusShowsNextToDamageWithoutChangingTotals()
    {
        var alice = new PlayerInfo(1, "Alice", "Ironclad", "d85a30");
        var bob = new PlayerInfo(2, "Bob", "The Silent", "7fff00");
        var vuln = new SourceRef(SourceKind.Power, "VULNERABLE_POWER", "Vulnerable");
        var s = new RunStats();
        s.RecordDamage(1, new SourceRef(SourceKind.Card, "BASH", "Bash"), 100);
        s.RecordDamage(2, new SourceRef(SourceKind.Card, "SHIV", "Shiv"), 300);
        s.RecordDebuffApplied(1, vuln, 4);
        s.RecordDebuffBonus(1, vuln, 90);
        RecapView v = RecapBuilder.Build(s, new[] { alice, bob }, new Dictionary<ulong, DefenseTotals>(), "h");

        BarRow aliceRow = v.Overview.Single(r => r.Label == "Alice");
        Check.Equal(100, aliceRow.Value, "own damage unchanged");
        Check.Equal(90, aliceRow.Bonus, "bonus alongside");
        Check.Equal(0, v.Overview.Single(r => r.Label == "Bob").Bonus, "bob enabled nothing");
        Check.Near(0.25, aliceRow.Share!.Value, "share uses own damage only");
        Check.Equal("400", v.Highlights[0].Value, "team damage not inflated");
        Check.Equal("Bonus damage is the extra damage teammates dealt thanks to your Vulnerable.", v.BonusNote, "note");

        DebuffGroup group = v.Debuffs.Applied.Single();
        Check.Equal(90, group.BonusTotal, "group bonus");
        Check.Equal(90, group.Bars.Single(b => b.PlayerLabel == "Alice").Bonus, "bar bonus");
    }

    [Test]
    public static void NoBonusMeansNoNote()
    {
        RecapView v = RecapBuilder.Build(new RunStats(), new[] { new PlayerInfo(1, "A", "X", "fff") },
            new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal("", v.BonusNote, "no note");
    }

    [Test]
    public static void WeakPreventsTheHpItKeepsOffTheTarget()
    {
        Check.Equal(3, DebuffBonus.Prevented(7.5m, 0.75m, block: 0, hpCap: 50), "10 -> 7.5: 3 HP saved");
        Check.Equal(2, DebuffBonus.Prevented(7.5m, 0.75m, block: 8, hpCap: 50), "8 block: 2 -> 0");
        Check.Equal(0, DebuffBonus.Prevented(7.5m, 0.75m, block: 12, hpCap: 50), "blocked either way");
        Check.Equal(0, DebuffBonus.Prevented(7.5m, 0.75m, block: 0, hpCap: 6), "6 HP: lethal either way");
        Check.Equal(4, DebuffBonus.Prevented(6m, 0.6m, block: 0, hpCap: 50), "Paper Krane: 10 -> 6");
        Check.Equal(0, DebuffBonus.Prevented(10m, 1m, block: 0, hpCap: 50), "no reduction");
        Check.Equal(0, DebuffBonus.Prevented(10m, 0m, block: 0, hpCap: 50), "zero multiplier is ignored");
    }

    [Test]
    public static void PreventedShowsOnDefenseAndTheWeakGroup()
    {
        var alice = new PlayerInfo(1, "Alice", "Ironclad", "d85a30");
        var bob = new PlayerInfo(2, "Bob", "The Silent", "7fff00");
        var weak = new SourceRef(SourceKind.Power, "WEAK_POWER", "Weak");
        var s = new RunStats();
        s.RecordDebuffApplied(2, weak, 5);
        s.RecordDebuffPrevented(2, weak, 40);
        RecapView v = RecapBuilder.Build(s, new[] { alice, bob }, new Dictionary<ulong, DefenseTotals>(), "h");
        Check.Equal(40, v.Defense.Single(r => r.Label == "Bob").Prevented, "defense column");
        Check.Equal(0, v.Defense.Single(r => r.Label == "Alice").Prevented, "alice prevented nothing");
        Check.Equal("\"Kept off the team\" is what your Weak prevented.", v.PreventedNote, "note");
        DebuffGroup group = v.Debuffs.Applied.Single();
        Check.Equal(40, group.PreventedTotal, "group total");
        Check.Equal(40, group.Bars.Single(b => b.PlayerLabel == "Bob").Prevented, "bar");
        Check.Equal("", RecapBuilder.Build(new RunStats(), new[] { alice }, new Dictionary<ulong, DefenseTotals>(), "h")
            .PreventedNote, "no note without prevention");
    }

    [Test]
    public static void SplitAddsUpAndFollowsWeights()
    {
        IReadOnlyDictionary<ulong, int> s = DebuffBonus.Split(10, new[] { (1UL, 2), (2UL, 3) });
        Check.Equal(4, s[1], "2 of 5 stacks");
        Check.Equal(6, s[2], "3 of 5 stacks");

        IReadOnlyDictionary<ulong, int> odd = DebuffBonus.Split(5, new[] { (1UL, 1), (2UL, 1), (3UL, 1) });
        Check.Equal(5, odd.Values.Sum(), "whole points add up");
        Check.Equal(2, odd[1], "leftover to the earliest tie");
        Check.Equal(2, odd[2], "second leftover");
        Check.Equal(1, odd[3], "third");

        Check.Equal(0, DebuffBonus.Split(5, Array.Empty<(ulong, int)>()).Count, "no weights");
        Check.Equal(7, DebuffBonus.Split(7, new[] { (4UL, 3) })[4], "single applier gets it all");
    }

    [Test]
    public static void StrengthLossOnAHitThatStillLandsIsTheStrengthTimesTheMultipliers()
    {
        // A 12 attack with 8 Strength taken off: 4 lands.
        Check.Equal(8, DebuffBonus.StrengthPrevented(4m, 8m, 1m, restored: null, block: 0, hpCap: 50), "12 -> 4");
        // The same under Vulnerable: 18 -> 6.
        Check.Equal(12, DebuffBonus.StrengthPrevented(6m, 8m, 1.5m, restored: null, block: 0, hpCap: 50), "x1.5");
        Check.Equal(0, DebuffBonus.StrengthPrevented(4m, 8m, 1m, restored: null, block: 12, hpCap: 50), "blocked either way");
    }

    [Test]
    public static void StrengthLossThatZeroesAHitCreditsTheWholeAttack()
    {
        // An 8 attack with 8 Strength taken off lands as exactly 0.
        Check.Equal(8, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: 8m, block: 0, hpCap: 50), "8 -> 0");
    }

    [Test]
    public static void StrengthLossBelowZeroCreditsOnlyTheAttackNotTheExcessStrength()
    {
        // A 6 attack with 8 Strength taken off: the game floors -2 at 0. 6 was kept off, not 8.
        Check.Equal(6, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: 6m, block: 0, hpCap: 50), "6 -> 0");
    }

    [Test]
    public static void AZeroedHitStillGoesThroughBlockAndHp()
    {
        Check.Equal(0, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: 6m, block: 10, hpCap: 50), "block would have taken it all");
        Check.Equal(2, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: 6m, block: 4, hpCap: 50), "2 past block");
        Check.Equal(9, DebuffBonus.StrengthPrevented(0m, 8m, 1.5m, restored: 9m, block: 0, hpCap: 50), "6 x1.5 Vulnerable");
        Check.Equal(3, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: 6m, block: 0, hpCap: 3), "capped at HP");
    }

    [Test]
    public static void AZeroedHitWhoseFullSizeIsUnknownGetsNoCredit()
    {
        // Adding the Strength back to 0 would claim 8 whether the attack was 1 or 100.
        Check.Equal(0, DebuffBonus.StrengthPrevented(0m, 8m, 1m, restored: null, block: 0, hpCap: 50), "no restored value");
    }
}
