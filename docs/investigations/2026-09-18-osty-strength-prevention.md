# Investigation: Osty tanking and Strength-reduction prevention

Investigated 2026-09-18 against repository source, the installed Who Carried Workshop DLL, the installed game's v0.111.0 assemblies, and a saved September 16 run. No gameplay code was changed or live game reproduction performed.

Comment under review:

> Cool mod ! Just realised that it doesn't take into account damage tanked by Osty or damage avoided using strength reducing skills such as Enfeebling Touch

## Verdict

**Partly correct.** Osty's absorbed HP damage is not recorded as a defensive contribution. Strength-reduction prevention, including Enfeebling Touch, is implemented and has been recorded in a real saved run. However, a confirmed code-path gap skips that credit when Strength reduction brings the modified attack to zero. The commenter's precise experience cannot be identified without their run/version/logs.

| Claim | Assessment | Evidence |
|---|---|---|
| Osty's tanking is omitted | **Confirmed as missing contribution credit** | Player damage-taken history excludes Osty's HP loss; our incoming-damage handler records block but no pet HP absorption |
| Strength reduction is not counted at all | **Disproved** | Existing calculation, UI path, and real saved prevention events |
| Enfeebling Touch is not supported | **Disproved as a general claim** | Two logged events of 9 prevented damage and 18 stored under Enfeebling Touch |
| Some fully suppressed attacks receive no prevention credit | **Confirmed by shipped control flow** | Game clamps modified damage to zero; tracker returns before computing Strength prevention |

## Osty: what is missing

The game's `DieForYouPower.ModifyUnblockedDamageTarget` redirects a powered attack from the player to living Osty. In `CreatureCmd.Damage`, player block is consumed first, remaining damage goes to Osty, and any overkill spills back to the original target. Each actual receiver gets its own damage result.

The game's per-floor `DamageTaken` counter is updated only through `receiver.Player`. Osty has `PetOwner`, not a `Player` of its own, so Osty's HP loss does not enter that player counter. Who Carried's `GameReader.Defense` reads those player counters directly (`GameReader.cs:183`).

Who Carried can resolve a pet to its owner (`FactsExtractor.PlayerIdOf`), but `Tracker.OnDamage` records only `facts.Blocked` for friendly targets (`Tracker.cs:107`, especially line 127). It does not accumulate `facts.HpRemoved` into an Osty/pet absorption stat. `DefenseRow` has taken, blocked, healed, and debuff-prevented totals, but no pet-tanking field (`Core/RecapBuilder.cs:38`).

Illustrative uncomplicated hit: an attack for 12, 2 player block, and an Osty with 7 HP produces 2 blocked, 7 Osty HP lost, and 3 player HP lost. The recap can show the 2 blocked and 3 player damage taken; it has no separate credit for Osty's 7. This is an example derived from the inspected code, not a newly recorded fight.

Osty still indirectly lowers the player's displayed damage taken because the player genuinely lost less HP. That is not the same as showing Osty's defensive contribution. Osty's outgoing damage and pet-via-card attribution are also separate, existing features.

## Strength reduction: existing support and direct evidence

`EnfeeblingTouch` applies `EnfeeblingTouchPower` with the playing character as applier. Its base value is 8 (11 upgraded) in the inspected game version. `EnfeeblingTouchPower` inherits `TemporaryStrengthPower` and declares itself negative; it is therefore a debuff. The temporary-power implementation applies negative `StrengthPower` internally and later restores it.

Who Carried's path is:

1. `Tracker.OnPowerChanged` records debuff applications and contributor data.
2. `DebuffBonusTracker.TemporaryStrengthLoss` finds debuff instances derived from `TemporaryStrengthPower` (`DebuffBonusTracker.cs:105`), including Enfeebling Touch.
3. `Tracker.CreditStrengthLoss` estimates the HP that would have been lost without the reduction, accounting for multipliers, block and an HP cap, and records prevention (`Tracker.cs:224`).
4. `RecapBuilder` sums `DebuffPrevented` into each defense row (`RecapBuilder.cs:144`). The Defense tab labels it **kept off the team**; the Debuffs tab displays the effect's own name and **Kept N damage off the team**.

The saved run under `runs/2026-09-16_1835_F9RL6739CFWX/` contains an application of 8 Enfeebling Touch and two prevention entries. Sanitized excerpts (player names omitted):

```text
[F31 A2] <player> prevented 9 via ENFEEBLING_TOUCH_POWER (Enfeebling Touch)
  ... hit <player> for 12.375, 8 Strength removed (x1.125, block 12)
[F31 A2] <player> prevented 9 via ENFEEBLING_TOUCH_POWER (Enfeebling Touch)
  ... hit <player> for 12.375, 8 Strength removed (x1.125, block 0)
```

The corresponding `current_run.dat` contains **18** in `DebuffPrevented["Power:ENFEEBLING_TOUCH_POWER"].Amount`, and 8 applied stacks. A read-only check independently summed the two matching log events and compared the sum with saved stats: **2 events, 18 logged, 18 saved**. Evidence locations: `events.log:1749`, `events.log:1780`, `events.log:1783`, and `current_run.dat:1139`. These original run files remain git-ignored.

The metric estimates HP saved after block, not simply the enemy's intent reduction. If both the original and reduced attack are fully blocked, it intentionally awards zero immediate HP prevention. The same metric also has game-state limitations; these examples establish that the feature operates, not that every counterfactual is exact.

## Confirmed gap: attacks reduced to zero

The installed game calls `Hook.ModifyDamage`, which returns `Math.Max(0m, num)`, before `Hook.BeforeDamageReceived`. Our Harmony prefix receives that already-modified amount. The shipped `Tracker.OnBeforeDamage` contains:

```csharp
if (dealer == null || amount <= 0m) return;
```

That guard is at `Tracker.cs:174`, before `CreditStrengthLoss` at line 209. Thus a powered enemy attack reduced to zero receives no Strength-prevention credit from this path, even if it would otherwise have removed HP.

Concrete arithmetic example: enemy attack 6, Strength reduction 8, no block, sufficient player HP. The game clamps the resulting -2 to zero. The player avoided 6 HP loss, but this handler returns before recording prevention. This is a code-path counterexample, not a claim that it was the commenter's setup.

Simply deleting the guard is not enough to calculate that example correctly: adding 8 back to the clamped zero would claim 8 prevented instead of 6. A proper fix needs enough pre-clamp/counterfactual information to reconstruct the attack without the credited reduction. It should also preserve the intentional zero credit for attacks that block would have fully absorbed anyway.

## Scope, proposed follow-up, and reply

The installed Workshop and local Release DLLs have identical SHA-256 `00C6133C11AE3FEC4EF3602D62C49EEAF6E1A60BFDABEFAA548D6E9B82098B0A`. The installed DLL's `Tracker` was decompiled separately to confirm the guard and friendly-damage branch, rather than assuming current source had shipped. The game's hash is recorded in the [generic attribution spike](2026-09-18-generic-attribution-spike.md).

Full local decompilation evidence is under `runs/2026-09-18-defense-comment/`, with shared `CreatureCmd`, `Hook`, and `PowerCmd` evidence under `runs/2026-09-18-hextech-burn/`. No binaries or full game decompilations are tracked. This investigation makes no claim about the commenter's exact game branch or mod version.

Follow-up should distinguish two changes: a pet-absorption contribution metric, and correct prevention credit for fully suppressed attacks. For pet absorption, measure actual pet HP removed without counting player block or overkill twice, and label the contribution separately from player injury. For Strength prevention, validate positive remaining damage, exact zero, excess reduction below zero, sufficient block, multipliers and multi-hits. These changes are not implemented here.

Suggested public reply (draft only; not posted):

> Thanks for flagging this! You're right that Osty's absorbed damage isn't currently shown as a defensive contribution. Strength reduction, including Enfeebling Touch, is tracked under damage “kept off the team,” but I found a case where attacks reduced all the way to zero miss that credit. Thanks for drawing attention to it.
