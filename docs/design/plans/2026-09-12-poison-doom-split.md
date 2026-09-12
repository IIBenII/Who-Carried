# Fair Poison and Doom Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poison ticks and Doom kills are credited to every player by their share of the pile, instead of all to whoever started it.

**Architecture:** A new pure class, `Core/SharedPile`, holds one pile's owners and splits damage in whole points (rounded down, spare points to the largest fractions, exact ties take turns). `Game/DebuffBonusTracker` keeps one `SharedPile` per Poison or Doom power instance, fed by the stack capture that already exists. `Game/Tracker` asks it for the split on each Poison tick and Doom kill, and records one entry per player.

**Tech Stack:** C# / .NET 9, the game's own DLLs, Harmony (game-provided). Tests: the built-in runner in `tests/WhoCarried.Tests` (no NuGet).

**Spec:** `docs/design/specs/2026-09-12-poison-doom-split-design.md`

## Global Constraints

- Game: Slay the Spire 2 **v0.111.0**. Decompiled source lives outside the repo; never commit it.
- **Read-only:** never call game commands, never mutate game state, never subclass `AbstractModel`.
- **No NuGet packages.** Tests use `tests/WhoCarried.Tests/TestRunner.cs` (`[Test]`, `Check.Equal`, `Check.True`).
- **No hardcoding other mods.** `PoisonPower` and `DoomPower` are the game's own types, so they're allowed.
- **No absolute paths in tracked files.** Machine settings live in the untracked `local.props`.
- **Every Harmony patch body** already catches and logs; new code in the Tracker falls back to today's behaviour on any error.
- **Git:** work on `feature/fair-poison-doom-split`; commit per task; squash into `main` at the end; never push. The repo-local identity is already set.
- **dotnet:** use a `dotnet` with the .NET 9 SDK. If the one on PATH doesn't have it, use the path in `local.props` (`DotnetPath`).

---

## File structure

| File | Responsibility |
|---|---|
| `src/WhoCarried/Core/SharedPile.cs` (create) | One pile: owners' shares, shrinking, and the whole-point split with turns. Pure; compiled into the tests too. |
| `tests/WhoCarried.Tests/SharedPileTests.cs` (create) | Worked examples, turns, edge cases, stress test against an exact model, and fairness. |
| `src/WhoCarried/Game/DebuffBonusTracker.cs` (modify) | Route Poison and Doom stacks into a `SharedPile` per power; `SplitPile` for the Tracker. |
| `src/WhoCarried/Game/FactsExtractor.cs` (modify) | `PoisonTick`: the Poison power a hit fell back to. |
| `src/WhoCarried/Game/Tracker.cs` (modify) | Split Poison ticks and Doom kills; one stat entry and log line per share. |
| `tests/WhoCarried.Tests/ReplayTests.cs` (modify) | Per-share log lines replay into each player's totals. |
| `docs/design/specs/2026-09-12-poison-doom-split-design.md` (modify) | Fairness is guaranteed per pile (a new pile starts its turns again). |

---

### Task 1: `SharedPile` with worked examples, turns and edge cases

**Files:**
- Create: `src/WhoCarried/Core/SharedPile.cs`
- Test: `tests/WhoCarried.Tests/SharedPileTests.cs`

**Interfaces:**
- Consumes: `DebuffBonus.SplitIndexed(int amount, IReadOnlyList<decimal> weights) → int[]` (existing, unchanged). It floors each exact part and gives the leftover points to the largest remainders; ties go to the earlier index.
- Produces:
  - `public sealed class SharedPile` (namespace `WhoCarried.Core`)
  - `void Add(ulong? player, int stacks, int pileAfter)`
  - `IReadOnlyDictionary<ulong, int> Credit(int pileNow, int damage)`
  - `IReadOnlyList<(ulong Player, decimal Share)> Shares()`

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/SharedPileTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run the tests and see them fail**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- SharedPile`
Expected: build error `CS0246: The type or namespace name 'SharedPile' could not be found`.

- [ ] **Step 3: Write `SharedPile`**

Create `src/WhoCarried/Core/SharedPile.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>
/// One Poison or Doom pile on one enemy, and whose it is. Each player's stacks are their share; when the pile shrinks,
/// every share shrinks by the same factor, so the proportions only change when stacks are added. Damage the pile deals
/// is split by the players' shares in whole points: everyone rounded down, the spare points to the largest fractions,
/// and exact ties take turns (the first goes to whoever stacked first, then the turn passes to the player after each
/// tie's winner). Stacks no player applied shrink with the rest but take no part in the split.
/// </summary>
public sealed class SharedPile
{
    /// <summary>Owners in the order they first stacked; null is stacks no player applied (an enemy's, or unseen).</summary>
    private readonly List<(ulong? Player, decimal Share)> _shares = new();

    /// <summary>Index into the players (in first-stacked order) of whoever wins the next exact tie.</summary>
    private int _turn;

    /// <summary>Scaling leaves decimal dust: a pile this close to its real size counts as matching it.</summary>
    private const decimal Dust = 0.000001m;

    /// <summary>Stacks that just landed; <paramref name="pileAfter"/> is the pile's size including them.</summary>
    public void Add(ulong? player, int stacks, int pileAfter)
    {
        if (stacks <= 0) return;
        ShrinkTo(pileAfter - stacks);
        int at = _shares.FindIndex(s => s.Player == player);
        if (at < 0) _shares.Add((player, stacks));
        else _shares[at] = (player, _shares[at].Share + stacks);
    }

    /// <summary>
    /// Shares out <paramref name="damage"/> that the pile just dealt at size <paramref name="pileNow"/>: whole points per
    /// player that add up to the damage. Empty when there's no damage or no player has a share.
    /// </summary>
    public IReadOnlyDictionary<ulong, int> Credit(int pileNow, int damage)
    {
        ShrinkTo(pileNow);
        var credits = new Dictionary<ulong, int>();
        IReadOnlyList<(ulong Player, decimal Share)> players = Shares();
        if (damage <= 0 || players.Count == 0) return credits;

        // In turn order, so the split's "ties go to the earlier one" hands ties out in turn.
        int n = players.Count;
        int first = _turn % n;
        var inTurn = Enumerable.Range(0, n).Select(i => players[(first + i) % n]).ToList();
        List<decimal> weights = inTurn.Select(p => p.Share).ToList();
        int[] points = DebuffBonus.SplitIndexed(damage, weights);

        // The same exact parts the split worked from: who got a spare point, and on what fraction.
        decimal total = weights.Sum();
        decimal[] exact = weights.Select(w => w * damage / total).ToArray();
        decimal[] fraction = exact.Select(e => e - Math.Floor(e)).ToArray();
        bool[] spare = Enumerable.Range(0, n).Select(i => points[i] > Math.Floor(exact[i])).ToArray();
        int lastTieWinner = -1;
        for (int i = 0; i < n; i++)
            if (spare[i] && Enumerable.Range(0, n).Any(j => !spare[j] && fraction[j] == fraction[i]))
                lastTieWinner = i;
        if (lastTieWinner >= 0) _turn = ((first + lastTieWinner) % n + 1) % n;

        for (int i = 0; i < n; i++)
            if (points[i] > 0) credits[inTurn[i].Player] = points[i];
        return credits;
    }

    /// <summary>Each player's share of the pile as last seen, in the order they first stacked.</summary>
    public IReadOnlyList<(ulong Player, decimal Share)> Shares() =>
        _shares.Where(s => s.Player.HasValue).Select(s => (s.Player.GetValueOrDefault(), s.Share)).ToList();

    /// <summary>
    /// Matches the pile's size: every share shrinks by the same factor, and stacks nobody was seen adding join the
    /// no-player share. At 0 the pile is gone; the game makes a new one when Poison or Doom lands again.
    /// </summary>
    private void ShrinkTo(int amount)
    {
        if (amount <= 0)
        {
            _shares.Clear();
            _turn = 0;
            return;
        }
        decimal total = _shares.Sum(s => s.Share);
        if (total > amount + Dust)
        {
            for (int i = 0; i < _shares.Count; i++)
                _shares[i] = (_shares[i].Player, _shares[i].Share * amount / total);
        }
        else if (total < amount - Dust)
        {
            decimal unseen = amount - total;
            int at = _shares.FindIndex(s => s.Player == null);
            if (at < 0) _shares.Add((null, unseen));
            else _shares[at] = (null, _shares[at].Share + unseen);
        }
    }
}
```

`exact` must use the same expression as `SplitIndexed` (`w * damage / total`, with `total` the sum of the weights), so the tie check sees the same fractions the split did.

- [ ] **Step 4: Run the tests and see them pass**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- SharedPile`
Expected: 15 `PASS SharedPileTests.…` lines and `15/15 passed`.

Then run the whole suite: `dotnet run --project tests/WhoCarried.Tests -c Release`
Expected: `110/110 passed` (95 before + 15).

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/SharedPile.cs tests/WhoCarried.Tests/SharedPileTests.cs
git commit -m "feat: SharedPile splits a Poison or Doom pile's damage by its owners"
```

---

### Task 2: Stress test against an exact model, and fairness

**Files:**
- Modify: `tests/WhoCarried.Tests/SharedPileTests.cs` (add to the class)
- Modify: `docs/design/specs/2026-09-12-poison-doom-split-design.md` (the Fairness bullet)

**Interfaces:**
- Consumes: `SharedPile.Add`, `SharedPile.Credit` from Task 1.
- Produces: nothing new for later tasks.

- [ ] **Step 1: Add the exact model and the stress test**

Append inside `SharedPileTests`:

```csharp
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
                }
                else if (roll < 40)
                {
                    // Stacks the mod never saw land (already on the pile when tracking began).
                    int stacks = rng.Next(1, 6);
                    amount += stacks;
                    exact.Add(null, stacks);
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
                if (amount == 0) pile = new SharedPile();
            }
        }
    }
```

- [ ] **Step 2: Run it**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- RandomFights`
Expected: `PASS SharedPileTests.RandomFightsMatchTheExactRuleWithinAPointEveryTick`, `1/1 passed`.
If it fails, the message names the seed and step: rerun that one fight by hand and fix `SharedPile`, not the test.

> **Found while executing:** as first written, the stress test failed (seed 3, step 66: 18 points for an exact 19.09).
> It added hidden stacks at any point in a pile's life. When hidden stacks land and the pile shrinks before
> `SharedPile` next looks, it only sees the net change, so it can't know how big the hidden part was. The exact model
> can. In the game this can't happen: `PowerCmd` is the only caller of `SetAmount`, and every increase fires the hook.
> So the test now adds hidden stacks only before any stacks are seen landing on that pile (the spec's "already on the
> pile before the mod saw it"). A separate test, `HiddenStacksPartwayThroughStillAddUp`, pins down what happens if
> another mod changes a pile directly. The spec says so under Rules. Counts below include that extra test.

- [ ] **Step 3: Add the fairness test**

Append inside `SharedPileTests`:

```csharp
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
```

- [ ] **Step 4: Run it, then the whole suite**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- SharedPile`
Expected: `18/18 passed`.
Run: `dotnet run --project tests/WhoCarried.Tests -c Release`
Expected: `113/113 passed`.

- [ ] **Step 5: Say in the spec that fairness is per pile**

A pile that runs out and restarts begins its turns again with whoever stacks first, so equal play is guaranteed even within one pile, not across piles. In `docs/design/specs/2026-09-12-poison-doom-split-design.md`, replace:

```
- **Fairness.** In fights where every player adds the same stacks at the same moments, every player's total ends within
  1 point of every other's.
```

with:

```
- **Fairness.** On a pile where every player adds the same stacks at the same moments, every player's total ends within
  1 point of every other's. (A pile that runs out and restarts begins its turns again with whoever stacks first.)
```

- [ ] **Step 6: Commit**

```bash
git add tests/WhoCarried.Tests/SharedPileTests.cs docs/design/specs/2026-09-12-poison-doom-split-design.md
git commit -m "test: SharedPile against an exact model in 500 random fights, and fairness"
```

---

### Task 3: Wire it into the game

**Files:**
- Modify: `src/WhoCarried/Game/DebuffBonusTracker.cs` (`AddStacks`, around line 129; add `Piles` and `SplitPile`)
- Modify: `src/WhoCarried/Game/FactsExtractor.cs` (add `PoisonTick` after `PoisonFallback`)
- Modify: `src/WhoCarried/Game/Tracker.cs` (`OnDamage` at line 95, `OnDoomKill` at line 269)
- Test: `tests/WhoCarried.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: `SharedPile` from Task 1.
- Produces (internal to the mod):
  - `DebuffBonusTracker.SplitPile(PowerModel power, int damage) → IReadOnlyDictionary<ulong, int>?`
  - `FactsExtractor.PoisonTick(DamageFacts facts, Creature? dealer, Creature target) → PoisonPower?`

- [ ] **Step 1: Write the replay test**

The live split writes one log line per share, in the existing formats. The replay must read them as each player's own damage. Add to `ReplayTests`:

```csharp
    [Test]
    public static void SplitPoisonAndDoomLinesReplayAsEachPlayersOwn()
    {
        string[] log =
        {
            LogReplay.HeaderLine("0.1.0", "SEED3:1789300000", new DateTime(2026, 9, 13, 20, 0, 0)),
            "player 1 = Ash (The Silent) #76b041",
            "player 2 = Jo (The Necrobinder) #ee82ee",
            "[F3 A1] fight start: Cultist",
            "[F3 A1] Ash <- Power:POISON_POWER (Poison) 5 hp | target CULTIST, blocked 0, dealer null, stack []",
            "[F3 A1] Jo <- Power:POISON_POWER (Poison) 4 hp | target CULTIST, blocked 0, dealer null, stack []",
            "[F3 A1] Ash <- Power:DOOM_POWER (Doom) 19 hp | target CULTIST, doom kill",
            "[F3 A1] Jo <- Power:DOOM_POWER (Doom) 6 hp | target CULTIST, doom kill",
            "[F3 A1] fight end, saved",
        };
        RunStats stats = LogReplay.Parse(log).Stats;
        Check.Equal(5, stats.Get(1)!.Sources["Power:POISON_POWER"].Amount, "Ash's Poison");
        Check.Equal(4, stats.Get(2)!.Sources["Power:POISON_POWER"].Amount, "Jo's Poison");
        Check.Equal(19, stats.Get(1)!.Sources["Power:DOOM_POWER"].Amount, "Ash's Doom");
        Check.Equal(6, stats.Get(2)!.Sources["Power:DOOM_POWER"].Amount, "Jo's Doom");
        Check.Equal(5 + 4 + 19 + 6, stats.Fights[0].DamageByPlayer.Values.Sum(), "the fight's total");
    }
```

- [ ] **Step 2: Run it**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- SplitPoison`
Expected: `1/1 passed`. The replay already reads these lines; this test pins the format the live code must keep.

- [ ] **Step 3: Route Poison and Doom stacks into piles**

In `src/WhoCarried/Game/DebuffBonusTracker.cs`, next to the `Stacks` table near the top of the class, add:

```csharp
    private static readonly ConditionalWeakTable<PowerModel, SharedPile> Piles = new();

    /// <summary>Poison and Doom: one pile per enemy, whose damage is shared by who owns it.</summary>
    private static bool IsSharedPile(PowerModel power) => power is PoisonPower or DoomPower;
```

Replace `AddStacks`:

```csharp
    /// <summary>Stacks landing on an enemy's debuff; <paramref name="player"/> null when no player applied them.</summary>
    public static void AddStacks(PowerModel power, ulong? player, int stacks)
    {
        if (stacks <= 0) return;
        // The game has already added them: Hook.AfterPowerAmountChanged fires after the amount changes.
        if (IsSharedPile(power)) Piles.GetOrCreateValue(power).Add(player, stacks, power.Amount);
        else Stacks.GetOrCreateValue(power).Add(player, stacks);
    }

    /// <summary>
    /// Shares out <paramref name="damage"/> that a Poison or Doom pile just dealt, by who owns it. Null when no player
    /// has a share in it.
    /// </summary>
    public static IReadOnlyDictionary<ulong, int>? SplitPile(PowerModel power, int damage)
    {
        IReadOnlyDictionary<ulong, int> credits = Piles.GetOrCreateValue(power).Credit(power.Amount, damage);
        return credits.Count > 0 ? credits : null;
    }
```

- [ ] **Step 4: Recognise a Poison tick**

In `src/WhoCarried/Game/FactsExtractor.cs`, after `PoisonFallback`, add:

```csharp
    /// <summary>
    /// The Poison pile a hit came from: no dealer, no card and nothing on the stack (so the fallback credited Poison),
    /// and the target has Poison. Null for any other hit.
    /// </summary>
    public static PoisonPower? PoisonTick(DamageFacts facts, Creature? dealer, Creature target) =>
        dealer == null && facts.Card == null && facts.StackTop == null ? target.GetPower<PoisonPower>() : null;
```

- [ ] **Step 5: Split Poison ticks in the Tracker**

In `src/WhoCarried/Game/Tracker.cs`, replace the enemy branch at the top of `OnDamage`:

```csharp
        if (facts.TargetIsEnemy)
        {
            AttributionResult who = Attribution.Resolve(facts);
            _stats.RecordDamage(who.PlayerId, who.Source, facts.HpRemoved, facts.Blocked);
            _log?.Write($"{Where} {NameOf(who.PlayerId)} <- {who.Source.Kind}:{who.Source.Id} ({who.Source.Label}) " +
                        $"{facts.HpRemoved} hp | target {Describe(target)}, blocked {facts.Blocked}, " +
                        $"dealer {Describe(dealer)}, stack [{StackIds(context)}]");
            if (boosted != null) CreditDebuffBonus(boosted, facts, who.PlayerId);
        }
```

with:

```csharp
        if (facts.TargetIsEnemy)
        {
            AttributionResult who = Attribution.Resolve(facts);
            if (PoisonShares(facts, dealer, target) is { } shares)
            {
                foreach ((ulong player, int hp) in shares) RecordHit(player, who.Source, hp, 0, target, dealer, context);
            }
            else
            {
                RecordHit(who.PlayerId, who.Source, facts.HpRemoved, facts.Blocked, target, dealer, context);
            }
            if (boosted != null) CreditDebuffBonus(boosted, facts, who.PlayerId);
        }
```

and add these below `OnDamage`:

```csharp
    private static void RecordHit(ulong? player, SourceRef source, int hp, int blocked, Creature target, Creature? dealer,
                                  PlayerChoiceContext? context)
    {
        _stats.RecordDamage(player, source, hp, blocked);
        _log?.Write($"{Where} {NameOf(player)} <- {source.Kind}:{source.Id} ({source.Label}) " +
                    $"{hp} hp | target {Describe(target)}, blocked {blocked}, " +
                    $"dealer {Describe(dealer)}, stack [{StackIds(context)}]");
    }

    /// <summary>
    /// A Poison tick's HP shared by who owns the pile. Null for any other hit, or if the split can't be made (the hit
    /// then goes to the pile's starter, as before).
    /// </summary>
    private static IReadOnlyDictionary<ulong, int>? PoisonShares(DamageFacts facts, Creature? dealer, Creature target)
    {
        try
        {
            return facts.HpRemoved > 0 && FactsExtractor.PoisonTick(facts, dealer, target) is PoisonPower poison
                ? DebuffBonusTracker.SplitPile(poison, facts.HpRemoved)
                : null;
        }
        catch (Exception e)
        {
            LogError("poison split", e);
            return null;
        }
    }
```

Poison is unblockable, so each share is recorded with 0 blocked. `CreditDebuffBonus` keeps `who.PlayerId`: Vulnerable only boosts powered attacks (`VulnerablePower.ModifyDamageMultiplicative` checks `IsPoweredAttack`), and Poison is unpowered, so no bonus ever comes from a Poison tick.

- [ ] **Step 6: Split Doom kills in the Tracker**

Replace `OnDoomKill` (and its summary) with:

```csharp
    /// <summary>
    /// Doom kills bypass the damage hooks: the game removes the creature's remaining HP with a direct kill. Count that
    /// HP as removed by the Doom power, shared by how much Doom each player added (or to whoever applied it, if that
    /// can't be worked out).
    /// </summary>
    public static void OnDoomKill(IReadOnlyList<Creature> creatures)
    {
        foreach (Creature creature in creatures)
        {
            if (creature == null || !creature.IsEnemy) continue;
            int hp = creature.CurrentHp;
            if (hp <= 0) continue;
            DoomPower? doom = creature.GetPower<DoomPower>();
            SourceCandidate source = doom != null
                ? FactsExtractor.Candidate(doom)
                : new SourceCandidate(new SourceRef(SourceKind.Power, "DOOM_POWER", "Doom"), null);
            IReadOnlyDictionary<ulong, int>? shares = null;
            try
            {
                if (doom != null) shares = DebuffBonusTracker.SplitPile(doom, hp);
            }
            catch (Exception e)
            {
                LogError("doom split", e);
            }
            if (shares != null)
            {
                foreach ((ulong player, int part) in shares) RecordDoomKill(player, source.Source, part, creature);
            }
            else
            {
                RecordDoomKill(source.OwnerId, source.Source, hp, creature);
            }
        }
        Touch();
    }

    private static void RecordDoomKill(ulong? player, SourceRef source, int hp, Creature creature)
    {
        _stats.RecordDamage(player, source, hp);
        _log?.Write($"{Where} {NameOf(player)} <- {source.Kind}:{source.Id} ({source.Label}) " +
                    $"{hp} hp | target {Describe(creature)}, doom kill");
    }
```

- [ ] **Step 7: Build and run everything**

Run: `dotnet build WhoCarried.sln -c Release --nologo -v q`
Expected: `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet run --project tests/WhoCarried.Tests -c Release`
Expected: `114/114 passed`.

- [ ] **Step 8: Commit**

```bash
git add src/WhoCarried/Game/DebuffBonusTracker.cs src/WhoCarried/Game/FactsExtractor.cs src/WhoCarried/Game/Tracker.cs tests/WhoCarried.Tests/ReplayTests.cs
git commit -m "feat: share Poison ticks and Doom kills by who owns the pile"
```

---

### Task 4: Finish

- [ ] **Step 1: Check the branch**

Run: `git status --short` (expected: empty) and `git log --oneline main..HEAD` (expected: the two spec commits, this plan's commit, and Tasks 1 to 3).

- [ ] **Step 2: Squash into main** (the owner's workflow: one commit per change, never push)

```bash
git switch main
git merge --squash feature/fair-poison-doom-split
git commit
git branch -D feature/fair-poison-doom-split
```

The commit message says what changed for players (Poison ticks and Doom kills shared by share of the pile, whole points, ties take turns), notes that old runs are unchanged, and ends with the Co-Authored-By line.

- [ ] **Step 3: Report what isn't verified**

The unit tests can't check the wiring into the game. The owner needs a co-op fight where two players stack Poison on one enemy. Afterwards, `events.log` should show one `Power:POISON_POWER` line per player on each tick. Deploying first means removing the old `mods\RunRecap` folder, which is the owner's call.
