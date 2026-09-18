# Hextech Burn damage attribution investigation

Follow-up: [generic attribution feasibility spike](2026-09-18-generic-attribution-spike.md), with an executable standalone probe. The findings below describe the original log and binary investigation.

## Finding

The screenshot's 17 Unknown damage matches two recorded hits of 8 and 9 against Fragment of the Universe on floor 2. Both have a null dealer and an empty model stack. Hextech Burn is the strongly supported source: both hits follow player-applied Burn, and the installed Burn implementation generates precisely this source-less damage path.

This is a concrete attribution gap. It does not establish the cause of the separate Workshop comment about custom characters breaking the mod.

## Evidence

Local snapshots are preserved under `runs/2026-09-18-hextech-burn/` (git-ignored). They include `events.log`, `current_run.dat`, the decompiled installed Burn implementation, the shipped Who Carried facts extractor, and the game's damage command. No gameplay code or installed mods were changed.

Relevant event sequence, with unrelated details omitted:

```text
You applied 8 HEXTECH_BURN_POWER (Burn)
UNATTRIBUTED <- Unknown:UNKNOWN (Unknown) 8 hp | dealer null, stack []
You <- Power:ODDMELT-COUNTER_ATTACK (Lament Strike) 12 hp
You <- Card:ODDMELT-STRIKE_ODDMELT (Strike) 3 hp
You applied 8 HEXTECH_BURN_POWER (Burn)
You <- Card:ODDMELT-SHANG_TONG_FAN_YING (This Life's Cut) 6 hp
You applied 6 ODDMELT-NOTCH (Notch)
UNATTRIBUTED <- Unknown:UNKNOWN (Unknown) 9 hp | dealer null, stack []
```

The saved stats agree: 71 player damage plus 17 unattributed damage. Oddmelt's Strike, This Life's Cut, and Lament Strike are named and credited. Burn applications are also credited to the player; its subsequent damage is where attribution is lost.

Inspected installed HextechRunes 0.9.3, variant `lib/0.111.0/HextechRunes.dll`. The latest nonempty game log reports game v0.111.0 and selection of that variant. That log ends at shutdown and is not a live trace of the screenshot's fight; the current `godot.log` was empty during inspection.

The Workshop Who Carried manifest is 1.1.0, although the embedded diagnostic version still says 0.1.0. Workshop and local Release DLLs have identical SHA-256:

```text
00C6133C11AE3FEC4EF3602D62C49EEAF6E1A60BFDABEFAA548D6E9B82098B0A
```

The shipped DLL was also decompiled to verify the relevant fallback directly.

## Mechanism

1. `HextechBurnPower.AfterSideTurnStart` creates a new `ThrowingPlayerChoiceContext` for Burn on enemies, without placing the Burn power on its model stack.
2. `ResolveBurn` calls the game's damage overload with the affected creature as the **target**, and null card source/card play.
3. That overload derives its dealer from `cardSource?.Owner.Creature`, so the dealer is null too.
4. Who Carried's `FactsExtractor.Extract` has no card or stack source. With a null dealer it only tries `PoisonFallback(target)`, which looks specifically for vanilla `PoisonPower`.
5. `SelfFire.OnDamageCommand` returns early without a player dealer, so its caller lookup cannot rescue this event.
6. `Attribution.Resolve` consequently produces a null player and `SourceRef.Unknown`. The UI faithfully displays these saved values.

Burn's damage formula is the larger of its stack count and a percentage of current HP, followed by 10% stack decay rounded up. Actual HP removed is recorded separately from overkill by the game. Thus applying 8 stacks twice does not require the two recorded damage amounts to sum to 16. The precise second tick's pre-hit HP/stack values were not captured, so an overkill explanation for 9 remains an inference.

## Related risk and repair direction

The existing Poison fallback assumes that any null-dealer, source-less hit on a poisoned target is Poison. A Burn tick on a target that also has Poison could therefore receive the wrong source and Poison contribution split. This follows from the code; that combined case has not been reproduced in-game.

A repair should identify the actual executing power and its live instance before resolving ownership, carrying that information for the specific damage operation. Merely choosing a debuff present on the target, assigning all unknown damage to the only player, or broadening a timed fallback would hide ambiguity and can give wrong credit in co-op. Source identification and sharing a stacked debuff between contributors are separate problems.

Suggested verification cases for a future fix: Burn alone; Burn plus Poison; two simultaneous damage-over-time effects; two players contributing to Burn; enemy-applied Burn; nested/repeated damage; and existing Poison/orb/pet attribution. Replay of the old log alone cannot prove the source because it was never recorded.

## Scope and confidence

Confirmed: exact screenshot total in logs and saved stats, successful attribution of the listed Oddmelt effects, and the missing-source path in the installed binaries. Strongly supported: these two particular Unknown hits are Hextech Burn. A fresh instrumented reproduction would provide direct event-to-power confirmation. No fix or live reproduction was performed as part of this investigation.
