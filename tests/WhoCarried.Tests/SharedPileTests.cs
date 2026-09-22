using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>Poison and Doom piles shared by who owns them: docs/design/specs/2026-09-12-poison-doom-split-design.md.</summary>
public static class SharedPileTests
{
    private const ulong You = 1, Ash = 2, Jo = 3;

    private static string Show(IReadOnlyDictionary<ulong, int> credits) =>
        string.Join(",", credits.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>A pile built from stacks landing in this order, with nothing ticking in between.</summary>
    private static SharedPile Pile(params (ulong Player, int Stacks)[] stacks)
    {
        var pile = new SharedPile();
        int size = 0;
        foreach ((ulong player, int n) in stacks) pile.Add(player, n, size += n);
        return pile;
    }

    /// <summary>Poison ticking from <paramref name="from"/> down to 1: each tick deals the pile's size, then it drops by one.</summary>
    private static void TickDown(SharedPile pile, int from, Dictionary<ulong, int> totals)
    {
        for (int size = from; size >= 1; size--)
            foreach ((ulong player, int points) in pile.Credit(size, size))
                totals[player] = totals.GetValueOrDefault(player) + points;
    }

    [Test]
    public static void ATickIsSplitByShareAndTheSparePointGoesToTheLargestFraction()
    {
        SharedPile pile = Pile((You, 3), (Ash, 5));
        Check.Equal("1:3,2:5", Show(pile.Credit(8, 8)), "tick of 8");
        // The pile drops to 7: 2.625 and 4.375. Rounded down 2 + 4; the spare goes to 0.625 over 0.375.
        Check.Equal("1:3,2:4", Show(pile.Credit(7, 7)), "tick of 7");
    }

    [Test]
    public static void EqualStacksTakeTurnsOnTies()
    {
        SharedPile pile = Pile((You, 5), (Jo, 5));
        Check.Equal("1:5,3:5", Show(pile.Credit(10, 10)), "tick of 10");
        Check.Equal("1:5,3:4", Show(pile.Credit(9, 9)), "the first tie goes to whoever stacked first");
        Check.Equal("1:4,3:4", Show(pile.Credit(8, 8)), "tick of 8");
        Check.Equal("1:3,3:4", Show(pile.Credit(7, 7)), "the next tie is the other player's");

        var totals = new Dictionary<ulong, int>();
        TickDown(Pile((You, 5), (Jo, 5)), 10, totals);
        Check.Equal(28, totals[You], "you, of 55");
        Check.Equal(27, totals[Jo], "Jo, of 55 (without turns it would be 30 vs 25)");
    }

    [Test]
    public static void TheFirstTieGoesToWhoeverStackedFirst()
    {
        var totals = new Dictionary<ulong, int>();
        TickDown(Pile((Jo, 5), (You, 5)), 10, totals);
        Check.Equal(28, totals[Jo], "Jo stacked first");
        Check.Equal(27, totals[You], "you");
    }

    [Test]
    public static void ThreePlayersSplittingEightThenSevenComeOutEven()
    {
        SharedPile pile = Pile((You, 3), (Ash, 3), (Jo, 3));
        Check.Equal("1:3,2:3,3:3", Show(pile.Credit(9, 9)), "tick of 9");
        Check.Equal("1:3,2:3,3:2", Show(pile.Credit(8, 8)), "2.667 each: two spares in a three-way tie, you then Ash");
        Check.Equal("1:2,2:2,3:3", Show(pile.Credit(7, 7)), "2.333 each: one spare, and it's Jo's turn");
    }

    [Test]
    public static void ALateJoinerSharesFromWhenTheyJoin()
    {
        var pile = new SharedPile();
        pile.Add(You, 5, pileAfter: 5);
        Check.Equal("1:5", Show(pile.Credit(5, 5)), "your Poison alone");
        Check.Equal("1:4", Show(pile.Credit(4, 4)), "still alone");
        pile.Add(Jo, 5, pileAfter: 8); // the pile was down to 3, all yours
        Check.Equal(3m, pile.Shares()[0].Share, "your 3");
        Check.Equal(5m, pile.Shares()[1].Share, "Jo's 5");
        var totals = new Dictionary<ulong, int> { [You] = 5 + 4 };
        TickDown(pile, 8, totals);
        Check.Equal(23, totals[You], "you, of 45");
        Check.Equal(22, totals[Jo], "Jo, of 45");
    }

    [Test]
    public static void ADoomKillIsSplitByDoomAdded()
    {
        SharedPile doom = Pile((You, 30), (Jo, 10));
        Check.Equal("1:19,3:6", Show(doom.Credit(40, 25)), "25 HP left: 18.75 and 6.25");
    }

    /// <summary>
    /// Zone the Spire's Hallowed turns half of itself into Doom on an enemy that has both, naming the enemy as the Doom's
    /// applier. The new Doom is passed on by the Hallowed's shares, so a Doom kill credits whoever applied the Hallowed.
    /// </summary>
    [Test]
    public static void DoomTurnedFromYourHallowedCountsForYou()
    {
        SharedPile doom = Pile((Ash, 4));
        var hallowed = new StackLedger();
        hallowed.Add(You, 10);
        IReadOnlyDictionary<ulong, int> passed = DebuffBonus.Split(5, hallowed.Active());
        doom.Add(Parts(passed), pileAfter: 9);
        Check.Equal("1:10,2:8", Show(doom.Credit(9, 18)), "an 18 HP Doom kill: your 5 of 9, Ash's 4");
    }

    [Test]
    public static void HallowedTwoPlayersStackedPassesItsDoomOnByTheirShares()
    {
        SharedPile doom = Pile((Ash, 4));
        var hallowed = new StackLedger();
        hallowed.Add(You, 6);
        hallowed.Add(Jo, 3);
        IReadOnlyDictionary<ulong, int> passed = DebuffBonus.Split(5, hallowed.Active());
        Check.Equal("1:3,3:2", Show(passed), "5 Doom: 3.33 and 1.67");
        doom.Add(Parts(passed), pileAfter: 9);
        Check.Equal("1:10,2:13,3:7", Show(doom.Credit(9, 30)), "a 30 HP Doom kill: 10, 13.33 and 6.67");
    }

    [Test]
    public static void SeveralOwnersLandingInOneChangeKeepTheirStacks()
    {
        SharedPile doom = Pile((Ash, 4));
        doom.Add(new List<(ulong?, int)> { (You, 3), (Jo, 2) }, pileAfter: 9);
        Check.Equal("1:3,2:4,3:2", Show(doom.Credit(9, 9)), "none of it taken as nobody's");
    }

    private static List<(ulong?, int)> Parts(IReadOnlyDictionary<ulong, int> split) =>
        split.Select(kv => ((ulong?)kv.Key, kv.Value)).ToList();

    [Test]
    public static void AThreeWayTieWithOneSparePointGoesRoundAllThree()
    {
        SharedPile pile = Pile((You, 1), (Ash, 1), (Jo, 1)); // Doom-style: the pile doesn't shrink
        Check.Equal("1:1", Show(pile.Credit(3, 1)), "you");
        Check.Equal("2:1", Show(pile.Credit(3, 1)), "Ash");
        Check.Equal("3:1", Show(pile.Credit(3, 1)), "Jo");
        Check.Equal("1:1", Show(pile.Credit(3, 1)), "back to you");
    }

    [Test]
    public static void ATieEveryoneTiedWinsDoesNotMoveTheTurn()
    {
        // Five players (a modded party) with 31, 31, 17, 17 and 4 of a 100 pile that doesn't shrink.
        SharedPile pile = Pile((1, 31), (2, 31), (3, 17), (4, 17), (5, 4));
        Check.Equal("1:1", Show(pile.Credit(100, 1)), "0.31 each for 1 and 2: 1 wins, and the turn passes to 2");
        // 10 damage: 3.1, 3.1, 1.7, 1.7, 0.4. Two spares, and 3 and 4 are the only ones on 0.7: both score.
        Check.Equal("1:3,2:3,3:2,4:2", Show(pile.Credit(100, 10)), "3 and 4 take the spares");
        Check.Equal("2:1", Show(pile.Credit(100, 1)), "still 2's turn: that tie didn't decide anything");
    }

    [Test]
    public static void StacksNoPlayerAppliedShrinkButTakeNoShare()
    {
        var pile = new SharedPile();
        pile.Add(null, 6, pileAfter: 6); // an enemy's
        pile.Add(You, 2, pileAfter: 8);
        pile.Add(Jo, 2, pileAfter: 10);
        Check.Equal("1:5,3:5", Show(pile.Credit(10, 10)), "the players split the whole tick");
        Check.Equal("1:5,3:4", Show(pile.Credit(9, 9)), "the pile shrank to 9, the enemy's part with it");
        Check.Equal(2, pile.Shares().Count, "only players are listed");
        Check.Equal(1.8m, pile.Shares()[0].Share, "your 2 is now 1.8");
    }

    [Test]
    public static void StacksSeenOnlyAsABiggerPileBelongToNoOne()
    {
        var pile = new SharedPile();
        pile.Add(You, 2, pileAfter: 6); // the pile already had 4 that the mod never saw land
        Check.Equal("1:6", Show(pile.Credit(6, 6)), "you're the only player in it");
        Check.Equal(2m, pile.Shares()[0].Share, "but your share is still 2 of the 6");
    }

    [Test]
    public static void APileNoPlayerOwnsCreditsNobody()
    {
        var pile = new SharedPile();
        pile.Add(null, 5, pileAfter: 5);
        Check.Equal("", Show(pile.Credit(5, 5)), "an enemy's Poison");
        Check.Equal("", Show(new SharedPile().Credit(3, 3)), "a pile the mod never saw stacked");
    }

    [Test]
    public static void ATickBelowThePileCreditsOnlyWhatItRemoved()
    {
        SharedPile pile = Pile((You, 6), (Jo, 4));
        Check.Equal("1:2,3:1", Show(pile.Credit(10, 3)), "the enemy had 3 HP left: 1.8 and 1.2");
    }

    [Test]
    public static void ACleanseShrinksEveryoneEvenly()
    {
        SharedPile pile = Pile((You, 6), (Jo, 4));
        Check.Equal("1:3,3:2", Show(pile.Credit(5, 5)), "halved from 10 to 5 before it ticked");
        Check.Equal(3m, pile.Shares()[0].Share, "you 3");
        Check.Equal(2m, pile.Shares()[1].Share, "Jo 2");
    }

    [Test]
    public static void AnEmptiedPileStartsOver()
    {
        SharedPile pile = Pile((You, 1), (Jo, 1));
        Check.Equal("1:1", Show(pile.Credit(2, 1)), "a tie: you win");
        pile.Add(Ash, 4, pileAfter: 4); // it had run out: 0 before these 4
        Check.Equal("2:4", Show(pile.Credit(4, 4)), "only Ash's Poison now");
        Check.Equal(1, pile.Shares().Count, "the old owners are gone");
    }

    [Test]
    public static void ZeroDamageCreditsNothingAndKeepsTheTurn()
    {
        SharedPile pile = Pile((You, 1), (Jo, 1));
        Check.Equal("", Show(pile.Credit(2, 0)), "nothing dealt");
        Check.Equal("1:1", Show(pile.Credit(2, 1)), "still your turn");
    }

    /// <summary>
    /// The rule with no rounding and no shortcuts: it shrinks on every change to the pile and splits in exact fractions.
    /// SharedPile only catches up when stacks land or damage is credited; this checks that it comes out the same.
    /// </summary>
    private sealed class ExactPile
    {
        private readonly Dictionary<ulong, decimal> _players = new();
        private decimal _unowned;

        public void Add(ulong? player, int stacks)
        {
            if (player is ulong id) _players[id] = _players.GetValueOrDefault(id) + stacks;
            else _unowned += stacks;
        }

        public void ShrinkTo(int amount)
        {
            if (amount <= 0)
            {
                _players.Clear();
                _unowned = 0;
                return;
            }
            decimal total = _players.Values.Sum() + _unowned;
            if (total <= 0) return;
            foreach (ulong id in _players.Keys.ToList()) _players[id] = _players[id] * amount / total;
            _unowned = _unowned * amount / total;
        }

        public Dictionary<ulong, decimal> Parts(int damage)
        {
            decimal owned = _players.Values.Sum();
            return owned <= 0 ? new() : _players.ToDictionary(kv => kv.Key, kv => damage * kv.Value / owned);
        }
    }

    private static void CheckTick(IReadOnlyDictionary<ulong, int> credits, Dictionary<ulong, decimal> parts, int damage,
                                  string where)
    {
        if (parts.Count == 0)
        {
            Check.Equal(0, credits.Count, $"{where}: no player has a share, so nobody is credited");
            return;
        }
        Check.Equal(damage, credits.Values.Sum(), $"{where}: the points add up to the damage");
        foreach ((ulong player, int points) in credits)
        {
            Check.True(points > 0, $"{where}: player {player} got {points}");
            Check.True(parts.ContainsKey(player), $"{where}: player {player} has no share but got {points}");
        }
        foreach ((ulong player, decimal part) in parts)
        {
            int points = credits.GetValueOrDefault(player);
            Check.True(Math.Abs(points - part) < 1.000001m, $"{where}: player {player} got {points} for an exact {part}");
        }
    }

    [Test]
    public static void RandomFightsMatchTheExactRuleWithinAPointEveryTick()
    {
        for (int seed = 1; seed <= 500; seed++)
        {
            var rng = new Random(seed);
            int players = rng.Next(1, 5);
            bool doom = seed % 4 == 0; // Doom never ticks down; its kill ends the pile
            var pile = new SharedPile();
            var exact = new ExactPile();
            int amount = 0;
            bool seenStacksLand = false; // on this pile
            for (int step = 0; step < 80; step++)
            {
                string where = $"seed {seed}, step {step}";
                int roll = rng.Next(100);
                if (roll < 35)
                {
                    ulong? who = rng.Next(6) == 0 ? null : (ulong)rng.Next(1, players + 1); // sometimes an enemy's
                    int stacks = rng.Next(1, 12);
                    amount += stacks;
                    pile.Add(who, stacks, amount);
                    exact.Add(who, stacks);
                    seenStacksLand = true;
                }
                else if (roll < 40)
                {
                    // Stacks the mod never saw land: already on the pile before it saw any. (In the game every later
                    // change fires the event it listens to; HiddenStacksPartwayThroughStillAddUp covers other mods.)
                    if (!seenStacksLand)
                    {
                        int stacks = rng.Next(1, 6);
                        amount += stacks;
                        exact.Add(null, stacks);
                    }
                }
                else if (roll < 47)
                {
                    amount = amount == 0 ? 0 : rng.Next(0, amount); // a cleanse
                    exact.ShrinkTo(amount);
                }
                else
                {
                    int ticks = doom ? 1 : rng.Next(1, 4); // Accelerant makes Poison tick more than once a turn
                    for (int t = 0; t < ticks && amount > 0; t++)
                    {
                        // A dying enemy (or a Doom kill) can take less than the pile.
                        int damage = doom || rng.Next(5) == 0 ? rng.Next(1, amount + 1) : amount;
                        CheckTick(pile.Credit(amount, damage), exact.Parts(damage), damage, where);
                        amount = doom ? 0 : amount - 1;
                        exact.ShrinkTo(amount);
                    }
                }
                // At 0 the game removes the power; Poison or Doom landing later makes a new pile.
                if (amount == 0)
                {
                    pile = new SharedPile();
                    seenStacksLand = false;
                }
            }
        }
    }

    [Test]
    public static void PlayersWhoStackAlikeOnOnePileEndWithinAPointOfEachOther()
    {
        for (int seed = 1; seed <= 200; seed++)
        {
            var rng = new Random(seed);
            int players = rng.Next(2, 6);
            var pile = new SharedPile();
            var totals = new Dictionary<ulong, int>();
            int amount = 0;
            for (int turn = 0; turn < 12; turn++)
            {
                if (amount <= 3 || rng.Next(3) == 0)
                {
                    // Everyone adds the same stacks, one after another, before anything ticks.
                    int stacks = rng.Next(1, 8);
                    for (ulong p = 1; p <= (ulong)players; p++)
                    {
                        amount += stacks;
                        pile.Add(p, stacks, amount);
                    }
                }
                int ticks = rng.Next(1, 4);
                for (int t = 0; t < ticks && amount > 1; t++) // never runs out, so it stays one pile
                {
                    foreach ((ulong p, int points) in pile.Credit(amount, amount))
                        totals[p] = totals.GetValueOrDefault(p) + points;
                    amount--;
                }
            }
            int spread = totals.Values.Max() - totals.Values.Min();
            Check.True(totals.Count == players && spread <= 1,
                $"seed {seed}, {players} players: totals {string.Join(", ", totals.OrderBy(kv => kv.Key).Select(kv => kv.Value))}");
        }
    }

    [Test]
    public static void HiddenStacksPartwayThroughStillAddUp()
    {
        // Another mod could set a pile's size directly, so stacks appear without the mod seeing them land. If the pile
        // then shrinks before the mod next looks, it can't tell how many there were, so the split can be off. Here the
        // pile is really yours 5, hidden 5, shrinks to 6 (3 and 3), then Jo adds 3: exactly half each from then on.
        // The mod sees only a pile of 6 that it thought was your 5.
        var pile = new SharedPile();
        pile.Add(You, 5, pileAfter: 5);
        pile.Add(Jo, 3, pileAfter: 9);
        IReadOnlyDictionary<ulong, int> credits = pile.Credit(9, 9);
        Check.Equal(9, credits.Values.Sum(), "the points still add up to the damage");
        Check.Equal("1:6,3:3", Show(credits), "5 of 8 player stacks look like yours (exact would be 4.5 each)");
    }
}
