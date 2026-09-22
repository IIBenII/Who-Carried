# Investigation: Zone the Spire's Hallowed gets no credit for its kills

Investigated 2026-09-23 from a live run's `events.log` and the game's `godot.log` (Who Carried 1.1.0 from the Workshop, game v0.111.0, Zone the Spire as installed that day), a decompile of Zone the Spire into a scratch folder, and a metadata scan of the game and every mod loaded that session.

Report:

> My friend has a zone mod I'm playing with. He added a new effect called Hallowed, it acts like Doom. It's not appearing in logs, and it's not attributing.

## Verdict

**Confirmed for the kill, not for the stacks.** Every Hallowed application was logged and counted (`You applied 54 ZONETHESPIRE-HALLOWED_POWER (Hallowed) | target BATTLE_FRIEND_V3, applier player You`). What went missing was the judgement: `godot.log` shows `Blinding Hallowed: judgement on The Insatiable` (floor 33) and `judgement on Battle Friend V3.0` (floor 37), each ending its fight, and `events.log` has nothing at either moment.

## Why

Hallowed works like Doom. At the end of a turn, an enemy with at least as much Hallowed as HP is judged, and the mod removes it with `CreatureCmd.Kill`. A direct kill takes the creature's remaining HP without the damage hooks, so the tracker never sees a hit.

The game's own Doom kills the same way. Who Carried catches that one with a patch on `DoomPower.DoomKill`, which names Doom, so nothing covered a mod's copy.

## Fix

Credit any direct kill that some content's **turn-start or turn-end hook** starts:

- The kill command (`CreatureCmd.Kill`, the list overload the single one hands to) now pins the effect running when it starts, the same way the experimental damage attribution pins it for damage (`EffectScopes.EnterKill`). Watching turn hooks and the kill command is on for everyone. Damage attribution stays behind `experimentalEffectSources`.
- Each killed enemy's remaining HP counts as that effect's damage. `Tracker.OnDirectKill` gathers the facts and `EffectCredit.ForKill` decides, with tests. If the enemy carries its own copy of the effect (every judged enemy has its own Hallowed), those stacks split the HP by who applied them. Otherwise the effect itself (a player's power or relic) credits its player.
- Nothing is counted for:
  - players and pets
  - Doom, which `OnDoomKill` has already counted
  - a kill with no effect running (a card, an enemy's own move)
  - a kill inside another kill (minions dying with their leader), which a card's kill wouldn't credit either
  - a debuff no player applied
- The log line is the Doom kill's with `direct kill` at the end, and `LogReplay` reads it back. A kill with an effect but no player behind it gets a `direct kill of … : no player behind it` note.

## What else this credits

A scan of IL metadata, nothing executed, found every call to `CreatureCmd.Kill` or `DoomPower.DoomKill` in `sts2.dll` and the 16 mods loaded that session. It walked each call up the call graph to see whether a turn hook reaches it.

| Content | Kills | From a turn hook? | Credited now? |
|---|---|---|---|
| Game: Doom | enemies at the end of a turn | yes | no change: already counted, and not counted twice |
| Game: everything else (Sacrifice, Bone Shards, End of Days, Gas Bomb and Waterfall Giant exploding, dev console) | various | no | no |
| Zone the Spire: **Hallowed** | judged enemies | yes | **yes** |
| Zone the Spire: Worldly Attachment | its own player | yes | no: not an enemy |
| Zone the Spire: Deva's Blessing | a blessed enemy after its move | no (a patch on `TakeTurn`) | no |
| Zone the Spire: Infestation, Phantasm | minions left once their leader dies, as the game does | no | no |
| Acts from the Past: Fading | the enemy carrying it | yes | no: an enemy applies it to itself, so no player has a stack |
| The Kin: pack dispatch | the player's pack followers | yes | no: not enemies |
| The Tailor: Minion Critic | the player's minion | yes | no: not an enemy |
| Other mods' splits, explodes and deaths (Acts from the Past, Downfall, The Kin, The Tailor, Watcher, Heart of the Spire) | various | no | no |

So on that mod list, Hallowed is the only thing that gains credit. The scan can't follow virtual calls, events or Harmony patches from one mod into another. The rule doesn't depend on the scan, though; it only decides what gets credited.

## Hallowed turning into Doom

An enemy carrying both converts half its Hallowed into Doom at the end of its turn (`BeforeSideTurnEndVeryEarly`). Zone the Spire names the enemy itself as that Doom's applier. So the converted stacks joined the Doom pile as nobody's, and a Doom kill split its HP among the players who applied Doom directly. In co-op, whoever applied the Hallowed got nothing for them. Solo it made no difference, because a pile's no-player stacks take no part in the split.

**Fix:** stacks that land on an enemy with no player and no card behind them (the enemy itself, or no one, as applier), while *another debuff on that same enemy* is acting in a turn hook, are that debuff handing part of itself on. They belong to whoever owns it, by the same shares: `DebuffBonusTracker.PassOn` for the split, `SharedPile.Add` with several owners to land it, and `EffectCredit.ForHandedOn` for the rule. A Doom kill then credits whoever applied the Hallowed.

- **Not counted as applied.** Converted Doom doesn't count as Doom those players applied on the Debuffs tab: they applied Hallowed, which is already counted. The log line starts `enemy applied …` and ends `passed on from ZONETHESPIRE-HALLOWED_POWER: You 5`, so a replay skips it the same way.
- **An enemy's own debuff hands nothing on.** If no player owns the debuff that's acting, the stacks stay nobody's, as before.

**What else it touches.** A second scan listed every debuff, in the game and the same 16 mods, whose turn hooks go on to add stacks (`PowerCmd.Apply` or `ModifyAmount`):

| Debuff | Adds | Changed? |
|---|---|---|
| Zone the Spire: **Hallowed** | Doom to its own enemy | **yes** |
| Game: Neurosurge | Doom to its own creature, which is a player | no: only enemies |
| Game: Wraith Form, Biased Cognition | minus Dexterity or Focus to their player | no: a player, and a loss |
| Game: Tender | Strength and Dexterity, which aren't debuffs | no |
| The Runesmith 2: Ice Cold | lowers itself | no: a loss |

## Left as is

- **Death prevention.** The HP is counted as the kill starts, as with Doom. If something stops the death, the HP was still taken (the game drops it to 0 first), so that stays right.
