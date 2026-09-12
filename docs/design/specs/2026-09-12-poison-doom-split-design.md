# Who Carried? — fair Poison and Doom split

- **Date:** 2026-09-12
- **Status:** design approved in chat. Game internals checked against decompiled v0.111.0.
- **Builds on:** `2026-09-10-run-recap-design.md` (counting rules), `2026-09-10-run-recap-debuffs-design.md` (stack tracking)

## Problem

The game keeps **one Poison pile per enemy** and records only who started it: `PowerCmd.Apply` sets `Applier` on the first
application, and later stacks go through `ModifyAmount`, which only changes the amount. Poison ticks arrive with no dealer,
no card and an empty model stack, so the mod credits the pile's starter. That player gets every Poison tick on that enemy
for the rest of the fight, even when most of the pile is someone else's. Doom has the same shape: one pile, first applier
recorded, and the whole Doom kill goes to them.

The mod already knows who put how many stacks into each pile (the Debuffs tab shows it correctly). Only the damage
isn't shared.

## Decisions (the owner, in chat)

- **Poison:** each tick is split by each player's **share of the pile at that moment**. When the pile drops, everyone's
  part shrinks evenly. Rejected: splitting by everything each player ever applied (early stacks that already ticked
  away keep earning), and oldest-first (equal stacks come out 15 vs 40).
- **Doom:** the same rule. Doom never ticks down, so the kill is split by how much Doom each player added.
- **Vulnerable and Weak keep oldest-first.** A stack there is a turn of coverage; once yours runs out, a teammate's newer
  stack really is the one doing the work.
- **Whole points, rounded per tick, ties take turns.** Rejected: a running carry of what each player is owed (more state,
  harder to explain, same results in every worked example), and storing damage as fractions throughout (every view
  would round on its own, so player rows would stop adding up to the team total; a refactor of the whole stats model).
- **New runs only.** Old logs don't record how piles shrank or which of two same-named enemies got the Poison, so
  re-splitting them would be guesswork. Old recaps, images and replays stay as they were.
- **Approach:** reuse the existing capture. Stacks already flow through `Tracker.OnPowerChanged` →
  `DebuffBonusTracker.AddStacks` for every debuff; Poison and Doom get a shared-pile record instead of the oldest-first
  queue.

## Rules

1. **Who owns the pile.** Each player's stacks are their share of it. When the pile shrinks (a tick, a cleanse,
   anything), every share shrinks by the same factor, so the proportions only change when stacks are added.
2. **A tick is split by share.** Each player's exact part is `damage × their share ÷ all players' shares`. Everyone is
   rounded down, and the points left over go to the players with the largest fractions (the ones closest to earning
   them).
3. **Ties take turns.** When players have exactly the same fraction and there aren't enough leftover points for all of
   them, the points go round in turn: the first tie goes to whoever stacked first, and after a tie is decided the turn
   passes to the next player after the one who won it. The turn is per pile.

Examples (all checked by simulation):

- You add 3, Ash adds 5 (pile 8). A tick of 8: you 3, Ash 5. The pile drops to 7, so the shares are 2.625 and 4.375:
  rounded down 2 + 4, and the spare point goes to you (0.625 beats 0.375): you 3, Ash 4.
- You and Jo add 5 each, and the pile ticks 10 down to 1. The odd ticks are exact ties and go you, Jo, you, Jo, you:
  **you 28, Jo 27** of 55. Without turns, the first-listed player wins every tie: 30 vs 25.
- You, Ash and Jo add 3 each (pile 9). Tick of 9: 3 each. Tick of 8: 2.667 each, 2 spare in a three-way tie, so you and
  Ash: 3 / 3 / 2. Tick of 7: 2.333 each, 1 spare, and it's Jo's turn: 2 / 2 / 3. **8 / 8 / 8.**
- You add 5, it ticks 5 and 4, then Jo adds 5 (pile 8: 3 yours, 5 Jo's) and it ticks 8 down to 1: **you 23, Jo 22** of 45.
- Doom: you 30, Jo 10, and the enemy dies with 25 HP left. 18.75 and 6.25, rounded down 18 + 6, and the spare goes to
  you: **19 and 6**.

Also:

- **Stacks no player applied** (applied by an enemy, or already on the pile before the mod saw it) shrink with the rest
  but take no part in the split: the players' shares divide each tick between them. If no player has a share, the tick
  goes to the pile's starter as today, or to "Unattributed" if there's none.
- **Stacks can't appear unseen partway through a pile in the game itself.** Only the game's own power commands change a
  pile's size (`PowerCmd` is the only caller of `SetAmount` in v0.111.0), and every increase fires the event the mod
  listens to. If another mod set a pile's size directly and the pile then shrank before the mod next looked, the mod
  couldn't tell how big the hidden part was, and the split could be off from then on (the points still add up).
- **Amounts.** A tick credits the HP it actually removed (`DamageResult.UnblockedDamage`), which can be less than the pile
  when the enemy dies. A Doom kill credits the enemy's remaining HP, as today.
- **A pile that empties** starts over: the game removes the power at 0, and a later application creates a new one.
- **Nothing is carried between ticks** except whose turn it is for the next tie, and nothing is saved. A pile lives only
  as long as its power on that enemy, within one fight.

What this guarantees:

- every tick's points add up to the damage it dealt
- each player's part of a tick is within 1 point of their exact part
- only players with a share in the pile get any of it
- ties don't favour anyone

Over a long fight with uneven shares, a player's total can end up a point or two away from exact. Nobody is
consistently favoured.

## What changes where

| Unit | Change |
|---|---|
| `Core/SharedPile.cs` (new, pure) | One pile: each owner's share in first-stacked order (null = stacks no player applied), and whose turn it is. `Add(ulong? player, int stacks, int pileAfter)` shrinks to `pileAfter - stacks`, then adds. `Credit(int pileNow, int damage)` → `IReadOnlyDictionary<ulong, int>`: shrinks to `pileNow`, splits `damage` by the players' shares, and returns empty when no player has a share. The split reuses `DebuffBonus.SplitIndexed` unchanged: the players are passed in turn order (starting with whoever's turn it is), so its "ties go to the earlier one" rule gives turns. `Shares()` → `IReadOnlyList<(ulong Player, decimal Share)>` for tests. A pile that shrinks to 0 or below forgets its shares. A sibling of `StackLedger`, not a mode of it: the two rules share no internals. |
| `Core/StackLedger.cs`, `Core/DebuffBonus.cs` | Unchanged. |
| `Game/DebuffBonusTracker.cs` | `AddStacks` sends `PoisonPower` and `DoomPower` stacks to a `SharedPile` per power instance (a second `ConditionalWeakTable`), with `pileAfter = power.Amount`. The game changes the amount before `Hook.AfterPowerAmountChanged`, which is when this runs. Every other debuff keeps its `StackLedger`. New `SplitPile(PowerModel power, int damage)` → the credits, or null when there is no player share. These are the game's own power types, not another mod's, so the no-hardcoding rule holds. |
| `Game/FactsExtractor.cs` | Exposes the Poison power a hit fell back to (no dealer, no card, nothing on the stack, target has Poison), so the Tracker knows when to split. |
| `Game/Tracker.cs` | `OnDamage`: when the hit is a Poison tick, record one Poison entry per player share (`RecordDamage(player, poison, share)`) instead of one for the pile's starter. `OnDoomKill`: the same, for the remaining HP. If the split fails or comes back empty, credit the pile's starter as today, and log any error. |
| Log | One line per share, in exactly today's hit and Doom-kill formats, so `LogReplay` needs no change and replays of new runs get the split. |

Unchanged: the Debuffs tab (it already counts stacks per player), team damage, and share-of-team maths. Each contributor
gets their own "Poison" or "Doom" source row. Awards and biggest-hit stats see each share as that player's own hit.

## Error handling

Everything runs inside the existing patch bodies, which catch and log. A failed split falls back to the old
single-player credit, so damage is never lost.

## Testing

Unit tests on `SharedPile` (pure; no game needed), in `tests/WhoCarried.Tests/SharedPileTests.cs`:

- **Worked examples:** every example under Rules, with the exact numbers shown there, including the equal-stack pile
  with the players stacked in the other order.
- **Turns:** a two-way tie alternates. A three-way tie with one spare point goes round all three. When a tie is decided,
  the turn passes to the player after the winner. A tie where everyone tied gets a point doesn't move the turn.
- **Edge cases:**
  - enemy-applied or unseen stacks shrink but don't take a share
  - a pile with no player share returns nothing
  - damage below the pile (an overkill tick)
  - a cleanse
  - a pile emptied and restarted
  - zero damage
- **Stress test.** 500 fights from fixed seeds: 1 to 4 players, random stacks (including enemy-applied ones and unseen
  ones already on a pile before the mod saw any land), 1 to 3
  ticks a turn (the Accelerant power makes Poison tick more than once), overkill ticks, cleanses, empty-and-restart,
  and Doom-style piles that never decay. Each fight runs beside an independent **exact model**: fractions, no rounding,
  shrinking on every single tick, where `SharedPile` only catches up when stacks land or damage is credited. Checked on
  every tick:
  - the points add up to the damage
  - each player's points are within 1 of the exact model's part for that tick
  - nobody gets negative points, or points from a pile they have no share in
- **Fairness.** On a pile where every player adds the same stacks at the same moments, every player's total ends within
  1 point of every other's. (A pile that runs out and restarts begins its turns again with whoever stacks first.)
- **Replay.** A log with one Poison line per share replays into the split totals.
- All existing tests stay green, including the oldest-first ones in `DebuffSharingTests`.

Not covered by unit tests: the wiring into the game (hook timing, power instances). It's checked by reading `events.log`
after a real co-op fight where two players stack Poison on the same enemy.

## Follow-up (not in this change)

The Vulnerable and Weak splits give exact ties to the first-listed player every time. They could take turns the same
way.
