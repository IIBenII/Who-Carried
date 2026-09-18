# Generic source attribution feasibility spike

Date: 2026-09-18. Branch: `codex/spike-generic-damage-attribution`, based on `4030854` (`main`).

## Decision

**Generic attribution is feasible enough to justify an opt-in in-game prototype. It is not yet a production fix.** We can observe the actual effect instance as the game calls its hook, preserve it through ordinary .NET asynchronous execution, and capture it when damage starts. This identifies an effect without knowing its name, ID, damage formula, stack decay, or character.

An isolated console probe using the installed Harmony 2.4.2.0 passed **18/18 checks**. With its instrumentation disabled, **7 attribution checks fail as expected** (11/18 pass, exit 1). The shipping mod is unchanged. No live game reproduction, deployment, public-branch verification, or performance measurement was performed.

The original [Hextech Burn investigation](2026-09-18-hextech-burn-attribution.md) is included with this spike. Its logs identify two Unknown hits totaling 17, and the installed Burn code provides a matching source-less damage path. The spike demonstrates a mechanism that could fill that gap; it does not retroactively prove the origin of events that were never recorded with a source.

## Evidence from installed assemblies

Inspected game v0.111.0, HextechRunes 0.9.3 variant 0.111.0, and Oddmelt's installed 0.110.1 implementation. The metadata scan included Oddmelt's BaseLib dependency.

| Assembly | Concrete model types | Distinct implemented `Task` hook methods |
|---|---:|---:|
| sts2 | 1,660 | 688 |
| HextechRunes | 485 | 515 |
| Oddmelt | 149 | 32 |
| BaseLib | 0 | 0 |

There are **1,231 unique candidate declarations** across these assemblies after shared inherited methods are deduplicated. Of these, 53 are named `AfterSideTurnStart`. These are metadata discovery counts, not a claim that all those methods were patched or need to run in each fight. The scan covers non-generic Task-returning overrides rooted in `AbstractModel`; it is not a census of every possible damage-producing method.

Relevant inspected behavior:

- `Hook.AfterSideTurnStart` awaits each **live** listener's `AfterSideTurnStart` method. It supplies no `PlayerChoiceContext` for this hook.
- Hextech's `HextechPowerBase` implements the new game signature and forwards to the older virtual signature overridden by Burn. The root-signature implementation lives on an abstract compatibility base. Discovering only methods declared on concrete model classes would miss it.
- `HextechBurnPower` creates a fresh `ThrowingPlayerChoiceContext` and invokes damage with no card. The game overload derives a null dealer from that null card.
- `PowerModel.Applier` is available on the live instance. The inspected `PowerCmd` assigns it when initially applying a new power; subsequent stacking uses `ModifyAmount`. It is not a general record of every contributing player.
- Who Carried's current `SelfFire` inspects caller **types**, resolves a canonical model, and caches by player for three seconds. It exits when no player dealer exists. Extending only that guard would neither recover the correct live instance nor provide ownership for the null-dealer path.
- `Tracker.OnDamage` runs at the prefix of `Hook.AfterDamageGiven`, before that hook dispatches its own listeners. This is a useful point to read the damage operation's captured source.

## Approaches considered

| Approach | Benefit | Limitation | Assessment |
|---|---|---|---|
| Extend caller-stack lookup and match against live models | Small extension to existing fallback | A stack identifies a type, not an instance; identical powers on several creatures are ambiguous, and async helpers can obscure the caller | Useful diagnostic or conservative fallback; insufficient as the main solution |
| Observe live effect hooks and capture source per damage operation | Preserves actual instance and ordinary async/nested execution without formulas | Needs runtime registration, careful lifetime handling, many potential patch targets, and game validation | **Preferred direction; mechanism demonstrated by the probe** |
| Instrument model calls inside the game's central dispatchers | Could reduce the number of patched content methods | Requires rewriting version-dependent dispatcher/async state-machine IL and managing conflicts with other mods | Keep as an alternative if broad registration proves too costly |

## Prototype mechanism

1. Discover concrete implementations of Task-returning virtual hooks originating on the shared model base. Normalize inherited reflection results to the **declaring method** and deduplicate them. This reaches an inherited compatibility bridge without naming its mod.
2. A Harmony prefix stores the actual `__instance` in an `AsyncLocal` frame. No game context or model fields are edited.
3. When the hook returns its Task, a finalizer restores the caller's previous frame immediately. Continuations already captured the effect's frame through their execution context.
4. The frame retains the **original Task** as its lifetime boundary. It becomes unusable once that Task finishes, faults, or cancels. The Task is not replaced or awaited by a wrapper.
5. A damage-command prefix captures the active effect into a separate operation frame. The probe's command returns `Task<IReadOnlyList<Hit>>`, exercising the generic Task return shape used by game damage commands. Listener effects cannot replace the outer hit's captured source; nested damage gets its own frame.
6. Explicit sources/dealers retain precedence in the probe. Missing ownership is allowed: a named source can remain unassigned to a player. A combat generation invalidates frames from older fights.
7. An inactive top frame resolves to unknown. Searching backward to an active parent would incorrectly credit a detached child to the parent. Suppressed execution-context flow also remains unknown.

These are prototype rules, not finalized game precedence rules. In-game integration must distinguish a causal source from incidental observer/listener contexts, handle the game's multiple command overloads, and verify the exact point at which source data is read.

## Behavioral results

All checks run in a console process against real Harmony patches on deliberately small synthetic types. They are executable characterization tests of the proposed boundaries, not mocks that simply return the expected source.

| Case | Instrumented | Patches disabled |
|---|---|---|
| Metadata slot discovery agrees with runtime reflection on probe types | Pass | Pass |
| Fresh context, no dealer, live effect retained | Pass | Expected failure |
| Attribution survives a genuinely suspended await | Pass | Expected failure |
| Inherited bridge preserves the derived live instance | Pass | Expected failure |
| Overlapping instances of the same effect type keep different owners | Pass | Expected failure |
| Nested reaction and outer damage keep their own sources | Pass | Expected failure |
| Explicit source/dealer takes precedence | Pass | Pass |
| Enemy effect is named without giving a player credit | Pass | Expected failure |
| Unobserved damage stays unknown | Pass | Pass |
| Asynchronous failure keeps the exception and clears attribution | Pass | Pass |
| Synchronous throw keeps the exception and restores the caller | Pass | Pass |
| Detached work after parent completion stays unknown | Pass | Pass |
| Completed child does not fall back to its still-active parent | Pass | Pass |
| Old combat generation invalidates suspended work | Pass | Pass |
| Damage captures its source before its own await | Pass | Expected failure |
| Returned Task identity is unchanged | Pass | Pass |
| Cancellation preserves its token and clears attribution | Pass | Pass |
| Suppressed execution-context flow stays unknown | Pass | Pass |

The existing Core test suite passed **167/167** at the start of the spike. It does not exercise game hooks and is not evidence that the live attribution issue is fixed.

An early probe failed because Harmony rejects inherited `MethodInfo` objects reflected via a child class. Normalizing to the actual declaration resolved that failure and is covered by the bridge check. The metadata inspector also needed a single assembly path per identity and a limited virtual-slot walker: `MetadataLoadContext` does not support `GetBaseDefinition`. Runtime patch discovery still uses the real runtime API.

## Scope and unresolved work

**Ownership is separate from source identity.** A live effect exposes its applier/owner only if the game or mod retained it. A shared counter can have several contributors but only one `Applier`. Arbitrary custom mechanics do not tell us how damage should be fairly divided, whether decay removes the oldest contribution, whether stacks have equal weight, or whether damage depends on the final applier's stats. Do not generalize the existing Poison/Doom split to every custom power and call it exact. Naming the source while leaving uncertain ownership unassigned is an acceptable outcome.

**Registration and cost need measurement.** Patching every discovered hook eagerly is a much larger intervention than today's 13 patch classes. The source mechanism itself requires no list of mod mechanics, but selecting appropriate live model declarations, registering after mods finish loading, detecting newly introduced model types, and tolerating failed/conflicting patches are production work. The probe patches only two synthetic hook declarations and one damage method. It makes no claim about startup latency, allocations, or frame time for 1,231 patches.

**Coverage is bounded.** Direct damage outside observed hooks, `Task<T>`/ValueTask-returning model methods, custom schedulers that do not flow execution context, queued callbacks outliving the originating hook, and competing Harmony prefixes that replace calls are not all covered. The conservative outcome should be unknown, with diagnostic provenance, rather than borrowing a recent source. The probe tests `Task<T>` for the damage boundary, not for model-hook discovery.

**Poison requires correction as part of integration.** `FactsExtractor.PoisonTick` currently infers Poison from an empty card/stack plus a poisoned target. Even after generic source discovery, that predicate could incorrectly route a Burn hit through Poison's contribution split unless it checks the identified source instance. Correct source identity must precede any special sharing rule.

**No gameplay mutation is needed by the proposed attribution mechanism**, but Harmony patch conflicts and lifecycle errors still require live testing. The probe's simple player field does not validate the game's real `PowerModel.Applier`, localization, source art, pets, or contributor ledgers.

## Recommended next stage

Build a disabled-by-default diagnostic integration, using the observed live-instance scope and a single well-defined damage boundary. Log source type/ID, instance identity within the run, attribution path, owner evidence, and operation lifetime. Start with the turn-start path that covers Burn, using generic hook discovery and no mod names. Expand only with evidence.

The live matrix should include Burn alone, Burn alongside Poison, two instances of the same power on different enemies, simultaneous player contributors, nested damage reactions, enemy-applied effects, save/resume and combat transitions, existing orb/pet/card attribution, and both supported game branches. Check actual damage totals are unchanged and measure registration cost and per-fight overhead.

Only promote this into a shipping change after that validation. Do not add a Hextech-specific damage formula or silently distribute all unknown damage to the only player.

## Reproduction and evidence

The tracked [probe README](../../tools/spikes/GenericAttribution/README.md) contains commands. Raw logs, complete metadata output, and local decompilation evidence are under the git-ignored `runs/2026-09-18-hextech-burn/`; these are machine-local and not required to read the conclusions. Original source code written for the spike is tracked; game/mod binaries and full decompiled sources are not redistributed.

| Inspected binary | SHA-256 |
|---|---|
| sts2.dll | `0861BFA1DF347538D932F22D580E75420F08082792EB914E53B4882764ACDBE9` |
| HextechRunes.dll, 0.111.0 variant | `9248B9DAE3EDAEE94F114E3EDE63A39C503605B4525045EE31D147B5BC6C1430` |
| Oddmelt.dll, 0.110.1 variant | `6F8257B7E3C57210E7F5CDB4C73598EBFEAF500D813221C068828FD00D9F27E5` |
| BaseLib.dll | `55863CA6ADC30A61A7544874E157A519FFE148CCF2B18025947609C4BFF813CE` |
| 0Harmony.dll, 2.4.2.0 | `EF1898322C9F5C86DC1B0758B272A9C440823B4A41CA9A0B82A3AA6B3D206387` |
