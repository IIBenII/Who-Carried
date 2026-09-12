using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>Sharing bonus damage fairly: between debuffs on the same hit, and between players by stacks still active.</summary>
public static class DebuffSharingTests
{
    [Test]
    public static void TwoBoostsShareTheRealExtraInsteadOfEachClaimingIt()
    {
        // 20 base, Vulnerable x1.5 and Flanking x1.5: 45 dealt, 25 of it extra. Each alone would claim 15.
        int[] shares = DebuffBonus.Bonuses(45m, new[] { 1.5m, 1.5m }, blocked: 0, hpRemoved: 45);
        Check.Equal(25, shares.Sum(), "total is the real extra");
        Check.Equal(13, shares[0], "first");
        Check.Equal(12, shares[1], "second");
    }

    [Test]
    public static void StrongerBoostGetsTheBiggerShare()
    {
        // 10 base, Flanking x2 and Vulnerable x1.5: 30 dealt, 20 extra, shared by how much each multiplies.
        int[] shares = DebuffBonus.Bonuses(30m, new[] { 2m, 1.5m }, blocked: 0, hpRemoved: 30);
        Check.Equal(13, shares[0], "x2");
        Check.Equal(7, shares[1], "x1.5");
    }

    [Test]
    public static void OneBoostMatchesTheSingleDebuffRule()
    {
        Check.Equal(DebuffBonus.Bonus(15m, 1.5m, 8, 7), DebuffBonus.Bonuses(15m, new[] { 1.5m }, 8, 7)[0], "with block");
        Check.Equal(DebuffBonus.Bonus(30m, 1.5m, 0, 12), DebuffBonus.Bonuses(30m, new[] { 1.5m }, 0, 12)[0], "killing blow");
        Check.Equal(0, DebuffBonus.Bonuses(30m, new[] { 1.5m }, 30, 0).Sum(), "all blocked");
    }

    [Test]
    public static void TwoReducersShareWhatTheyKeptOffTogether()
    {
        // A 20-damage attack from an enemy with Weak x0.75 and Shrink x0.75 lands as 11.25: 9 HP kept off in all.
        int[] shares = DebuffBonus.PreventedShares(11.25m, new[] { 0.75m, 0.75m }, block: 0, hpCap: 50);
        Check.Equal(9, shares.Sum(), "total kept off");
        Check.Equal(5, shares[0], "first");
        Check.Equal(4, shares[1], "second");
        Check.Equal(DebuffBonus.Prevented(15m, 0.75m, 4, 50), DebuffBonus.PreventedShares(15m, new[] { 0.75m }, 4, 50)[0], "one reducer");
    }

    [Test]
    public static void OldestStacksWearOffFirst()
    {
        // Wren applies 3; it wears down to 1; Juniper adds 2 (3 on the enemy).
        var ledger = new StackLedger();
        ledger.Add(1, 3);
        ledger.SyncTo(1);
        ledger.Add(2, 2);
        ledger.SyncTo(3);
        Check.Equal("1:1,2:2", Describe(ledger.Active()), "Wren's one left, Juniper's two");
        // Next round 2 remain: Wren's last stack is gone.
        ledger.SyncTo(2);
        Check.Equal("2:2", Describe(ledger.Active()), "all Juniper's");
    }

    [Test]
    public static void WearingOffBetweenAddsGivesTheSameAnswerCheckedLate()
    {
        // The tracker only trims when a hit needs the split; adds always go to the back, so checking late is the same.
        var ledger = new StackLedger();
        ledger.Add(1, 3);
        ledger.Add(2, 2);
        ledger.SyncTo(3);
        Check.Equal("1:1,2:2", Describe(ledger.Active()), "trimmed from the front");
    }

    [Test]
    public static void StacksNobodySawCountAsOlderAndBelongToNoPlayer()
    {
        // The enemy has 4 but only 2 were seen (applied before a Save & Quit): the unseen 2 are older, and wear off first.
        var ledger = new StackLedger();
        ledger.Add(1, 2);
        ledger.SyncTo(4);
        Check.Equal("1:2", Describe(ledger.Active()), "unseen stacks don't take a share");
        ledger.SyncTo(2);
        Check.Equal("1:2", Describe(ledger.Active()), "the unseen ones went first");
        ledger.Add(null, 1); // an enemy-applied stack
        ledger.Add(2, 1);
        Check.Equal("1:2,2:1", Describe(ledger.Active()), "enemy stacks sit in the queue but credit no one");
    }

    [Test]
    public static void PlayersAddingMoreKeepTheirPlaceInTheList()
    {
        var ledger = new StackLedger();
        ledger.Add(2, 1);
        ledger.Add(1, 2);
        ledger.Add(2, 3);
        Check.Equal("2:4,1:2", Describe(ledger.Active()), "grouped by player, first-seen order");
        Check.Equal("2:4,1:2", Describe(ledger.Lifetime()), "lifetime before anything wears off");
        ledger.SyncTo(3);
        Check.Equal("2:3", Describe(ledger.Active()), "the oldest three wore off: player 2's first stack and player 1's two");
        Check.Equal("2:4,1:2", Describe(ledger.Lifetime()), "lifetime keeps everything");
    }

    private static string Describe(IReadOnlyList<(ulong Player, int Weight)> weights) =>
        string.Join(",", weights.Select(w => $"{w.Player}:{w.Weight}"));
}
