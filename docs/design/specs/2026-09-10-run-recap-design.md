# Run Recap — design

- **Date:** 2026-09-10
- **Status:** design approved in chat. Game internals verified against decompiled v0.111.0.
- **Game:** Slay the Spire 2 Early Access **v0.111.0** (Godot 4.5, C#/.NET 9, Harmony bundled)

## Goal

A Slay the Spire 2 mod that shows per-player damage and defense charts for the current run:

- automatically on the victory/defeat screen, and
- on demand with **F8** at any point during the run.

It must work in co-op and with modded characters and cards (Into the Spireverse, The Tailor + MinionLib, Watcher). Run Recap hard-codes no characters.

## Constraints

- **Personal use** for one co-op group. No Workshop page, settings menu, or localization (English only).
- **No gameplay impact.** Read-only: never runs game commands, mutates game state, or registers models. Manifest sets `affects_gameplay: false`.
- **Works if only one player has it installed.** Verified: `HandshakeManager` rejects only *gameplay* mod mismatches; a non-gameplay mismatch only logs a warning.
- **No BaseLib dependency.** No `.pck` (`has_pck: false`). No NuGet packages: the build references the game's own DLLs, and tests use a tiny built-in runner.
- Early Access updates can break it. Target v0.111.0 and fix forward.

## What the player sees

A panel with a header (Victory/Defeat or "In progress", act, floor) and four tabs:

1. **Overview**: one horizontal bar per player with total damage dealt and share of the team total. Shares are computed over real players only. "Unattributed" appears as an extra grey row only when non-zero.
2. **Sources**: a player dropdown, then that player's top 6 damage sources as bars, with the rest grouped as "Other (n)".
3. **Timeline**: a line chart with one line per player, one point per fight, and act boundaries marked.
4. **Defense**: per player, damage taken, damage blocked, and HP healed.

Players are labelled "player name · character name". Colors come from `CharacterModel.NameColor`, which modded characters define too. Fallback is a fixed palette by player slot.

**Out of scope for v1:** timeline hover tooltips, card play counts, Run History integration, settings UI.

## Counting rules

These apply identically to every character and card, vanilla or modded.

- **Damage dealt** = `DamageResult.UnblockedDamage` for targets on the enemy side (`Creature.IsEnemy`). Verified in `Creature.LoseHpInternal`: `UnblockedDamage = hpBefore - hpAfter`, so enemy block and overkill are already excluded.
- **Attribution**, i.e. who gets credit and under what source:

  | Damage came from | Credited player | Source label |
  |---|---|---|
  | A pet or minion (dealer has `PetOwner`) | the pet's owner | the pet (`Creature.Name`) |
  | A card (`cardSource`) | dealer's player, else the card's owner | the card (unupgraded title, merged by card id) |
  | A power, relic, orb, or potion (top of `choiceContext.ModelStack`) | dealer's player, else the model's owner (power → `Applier`) | that model's title |
  | Poison (dealer null, empty stack, target has `PoisonPower`) | `PoisonPower.Applier`'s player | "Poison" |
  | Player known, source unknown | that player | "Unknown" |
  | Player unknown | "Unattributed" pseudo-player | whatever source is known |

  Poison is the only vanilla damage-to-enemies that runs with a fresh, empty context. Everything else is covered by `cardSource` or the model stack: `OrbCmd`, `PotionModel`, and `CardModel` push themselves, and so do the choice-context hook dispatchers such as `AfterCardExhausted`, `AfterDamageReceived`, and `AfterPlayerTurnStart`.
- **Damage taken** and **Healed**: summed from the game's own per-floor, per-player record (`runState.MapPointHistory[..][..].PlayerStats`: `DamageTaken`, `HpHealed`). It is exact, covers events and rest sites, and is saved with the run. **Healed skips the run's first map point**, where the game records the starting HP as "healed" (0 to start HP). Verified against Run History files on 2026-09-10.
- **Blocked** = `DamageResult.BlockedDamage` for damage whose target is a player's creature or that player's pet (pets use the owner's block).
- **Timeline bucket** = one fight, from `BeforeCombatStart` to `AfterCombatEnd`. Each bucket records act, floor, and encounter title.

## Architecture

The mod folder is `<game>/mods/RunRecap/`, containing `RunRecap.dll` and `RunRecap.json`.

**How events are captured:** read-only Harmony **prefixes on the game's public hook dispatchers** (`MegaCrit.Sts2.Core.Hooks.Hook`), plus two `RunManager` taps. We deliberately do *not* subscribe a listener model via `ModHelper.SubscribeForCombatStateHooks`. Any `AbstractModel` subclass in a mod is auto-registered in `ModelDb` and the network ID cache, and adds itself to every hook iteration. Prefixes see the same arguments with none of that surface.

| Tap | Signature (v0.111.0) | Used for |
|---|---|---|
| prefix `Hook.AfterDamageGiven` | `(PlayerChoiceContext choiceContext, ICombatState combatState, Creature? dealer, DamageResult results, ValueProp props, Creature target, CardModel? cardSource)` | damage dealt, blocked. Fires for killing blows too (`AfterDamageReceived` does not). |
| prefix `Hook.BeforeCombatStart` | `(IRunState runState, ICombatState? combatState)` | open a timeline bucket |
| prefix `Hook.AfterCombatEnd` | `(IRunState runState, ICombatState? combatState, CombatRoom room)` | close the bucket, save |
| event `RunManager.Instance.RunStarted` | `Action<RunState>`, fires on launch of new *and* continued runs | load or reset stats |
| postfix `RunManager.OnEnded` | `(bool isVictory)` | record the result, save |
| postfix `NGameOverScreen._Ready` | private field `_runState` | open the panel on the end screen |

| Unit | Responsibility | Depends on |
|---|---|---|
| `Core/*` (pure) | `Attribution`, `RunStats`, `RecapBuilder` (view-model for the four tabs), `RunStatsStore` (JSON), `EventLog`. | none (plain C#) |
| `Game/FactsExtractor` | Turns hook arguments into a pure `DamageFacts` record: dealer and pet owner, card, stack-top model and its owner, poison fallback. | game API |
| `Game/GameReader` | Player infos (name, character, color), defense sums from `MapPointHistory`, run key. | game API |
| `Game/Tracker` | Wires taps → `FactsExtractor` → `Attribution` → `RunStats`/`EventLog`. Save points. | Core, Game |
| `Game/Patches` | The Harmony prefixes and postfixes. Every body is wrapped in try/catch. | Harmony, Tracker |
| `UI/RecapPanel` | Builds the panel from a `RecapView` using **stock Godot controls only** (`CanvasLayer`, `PanelContainer`, `TabContainer`, `ColorRect` bars, `Line2D` timeline). No custom Godot node classes, so no Godot source generators are needed. | Godot |
| `ModEntry` | `[ModInitializer]`. Applies patches, subscribes `RunStarted`, and polls F8 via `SceneTree.ProcessFrame`. | all |

**Data flow:** hook → prefix → `FactsExtractor` → `Attribution` → `RunStats` (+ `EventLog`) → `RunStatsStore` at save points → `RecapBuilder` (+ defense from `GameReader`) → `RecapPanel`.

## Persistence (Save & Quit)

- The run key is `Rng.StringSeed` + `RunManager._startTime`, the latter read via Harmony `AccessTools`.
- **Save points:** `AfterCombatEnd` and `OnEnded`. File: `<game>/mods/RunRecap/data/current_run.dat`.
- On `RunStarted`: if the file's key matches and it's not finished, load it. Otherwise start fresh.
- Damage from a fight interrupted by Save & Quit is dropped rather than double-counted when the fight replays. Undercounting by one partial fight is acceptable.

## Multiplayer

Co-op is lockstep. Every client executes the same `GameAction`s, so `Hook.AfterDamageGiven` fires for every player's damage on each client. One client builds stats for the whole team with no networking. Players are keyed by `Player.NetId`, and names come from `PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, netId)`.

## Error handling

- Every Harmony patch body and the F8 handler catch all exceptions. Errors go to `EventLog` and the game log, and the event is skipped. Nothing ever propagates into game code.
- If a patch target is missing after a game update, that patch is skipped with a logged error. The rest of the mod still works.
- A missing, corrupt, or foreign-run `current_run.dat` is ignored and replaced.
- Models without a readable title fall back to their model id entry.

## Debug log

`<game>/mods/RunRecap/data/events.log` is reset at each run start. It gets one line per damage event:

```
[F12 A1] Ash (IRONCLAD) <- CARD:BASH  8 hp  (target: JAW_WORM, blocked 0)
[F12 A1] UNATTRIBUTED <- POWER:SOME_MOD_BLEED  3 hp  (dealer=null, stack=[])
```

It also records fight start and end lines and errors. Claude reads it after each test run.

## Testing

- **Unit tests** (`tests/RunRecap.Tests`, a console app with a small assert runner, no NuGet) cover the pure `Core/*`: attribution precedence table, `RunStats` aggregation and buckets, `RecapBuilder` (sorting, shares, top-6 + Other, Unattributed row), and the `RunStatsStore` round-trip plus foreign-key rejection.
- **Build check:** Claude builds, deploys to `mods/`, launches the game, and confirms in `godot.log` that the mod loaded and patches applied.
- **In-game checks** (the owner), with Claude reading `events.log` after each one:
  1. Solo vanilla run through the first boss. Open F8 mid-run.
  2. Die on purpose, and check that the defeat screen opens the panel.
  3. Save & Quit mid-run, continue, and check that the stats are intact with no double counting.
  4. Solo with an Into the Spireverse character.
  5. The Tailor (minion damage credited to the owner).
  6. Co-op run with at least one friend who does **not** have the mod.

  In each case, check that "Unattributed" and "Unknown" are near zero.

## Known risks

- **Modded DoT powers that deal damage with a fresh context** (like vanilla poison) will show as Unattributed until a rule is added. `events.log` will show them.
- **MinionLib minions** may not set `PetOwner`. If their damage shows as Unattributed, add a rule.
- **Stock controls might not pick up the game's theme and font.** If not, copy the theme from an existing game `Control`.
- **Harmony on `async` static methods:** prefixes run when the dispatcher is invoked, which is exactly when we want them.

## Project layout

```
RunRecap\
  RunRecap.sln
  src\RunRecap\            mod project (net9.0), Core\ Game\ UI\ ModEntry.cs, RunRecap.json
  tests\RunRecap.Tests\    console test runner, compiles src\RunRecap\Core\*.cs directly
  tools\deploy.ps1         build + copy to <game>\mods\RunRecap\
  docs\design\             specs and plans
```

Decompiled game source lives outside the project folder (a separate `sts2-decompiled\` folder) and is never shipped.
