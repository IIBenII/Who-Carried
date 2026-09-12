# Run Recap: Debuffs

The owner asked for "graphs and stats for debuffs as well" (2026-09-10). Claude made the design calls.

## What is counted

- **Capture:** a read-only Harmony prefix on `Hook.AfterPowerAmountChanged(combatState, choiceContext, power, amount, applier, cardSource)`. It fires after a new application and after stacking, with the amount that actually landed (after Artifact, other modifiers and multiplayer scaling).
- **Only debuff-type powers** (`power.Type == PowerType.Debuff`) with a positive change. Buffs, reductions and duration ticks are ignored.
- **Strength-down cards** (Piercing Wail, Dark Shackles) apply their own debuff-type power, which internally lowers Strength. The debuff is counted and the Strength change is not, so nothing is counted twice. Malaise's direct negative Strength is not counted; its Weak is.
- **Applied to enemies:** credited by `Attribution.ResolveApplier` — a player applier (pets resolve to their owner) wins; an enemy applier credits nobody; no applier falls back to the card's owner, then the owner of the model on top of the choice-context stack. Unattributed applications are logged, not counted.
- **Received:** debuffs landing on a player's creature from anything that isn't a player (self-inflicted debuffs are excluded).
- **Units:** stacks as the game reports them (turns for Vulnerable/Weak, amount for Poison/Doom). Debuffs are never summed across types in the UI.

## Where it shows

- **Debuffs tab** (after Sources): legend, "Applied to enemies" as one group per debuff (power icon, name, total stacks, a bar per player relative to that debuff's leader), top 6 plus "+N more"; then "Received from enemies" per player (total stacks and the three most common).
- **Saved image:** a "Debuffs" section between "Damage per fight" and "Defense", same groups in three columns, plus the received table.
- Stored in `PlayerTotals.DebuffsApplied` / `DebuffsReceived` in `current_run.dat`, so Save & Quit resumes them.

## Vulnerable bonus damage ("1,000 (+100)")

The owner asked that extra damage a teammate deals because of your Vulnerable count towards you, shown as "(+N)".

- **Measure:** a prefix on `Hook.BeforeDamageReceived` records the hit's final damage (before block) and every debuff-type power on the enemy whose `ModifyDamageMultiplicative` is above 1 for this hit (Vulnerable at 1.5, Paper Phrog/Cruelty/Debilitate variants, and modded equivalents). Kills are processed after `AfterDamageGiven`, so the power is still there.
- **Bonus per hit** (`DebuffBonus.Bonus`): HP actually removed minus the HP the hit would have removed without that multiplier, with the same block and never more than the HP actually removed (killing blows and Intangible-style caps give no bonus). Each amplifier is measured on its own.
- **Credit:** split by stacks each player put into that debuff instance (`DebuffBonus.Split`, whole numbers that add up); if none were seen (Save & Quit mid-fight) the power's applier gets it all. The hitter's own share is dropped: it's already in their damage, and "(+N)" is only what you enabled for teammates. Solo runs therefore show no "(+N)".
- **Not double counted:** own damage stays "HP actually removed"; share % and team damage ignore the bonus.
- **Shown:** "(+N)" in purple after the damage number on Overview and the saved image, with a note naming the debuffs; "+N dmg" per player on the Vulnerable group of the Debuffs tab and image. Stored as `PlayerTotals.DebuffBonus` by power.

## Weak damage prevented

- **Measure:** the same `Hook.BeforeDamageReceived` prefix. When an enemy hits a player or a pet, every debuff-type power on that enemy whose `ModifyDamageMultiplicative` is between 0 and 1 for this hit (Weak at 0.75, 0.6 with Paper Krane, Debilitate variants, modded equivalents) is a reducer.
- **Prevented per hit** (`DebuffBonus.Prevented`): HP the hit would have removed without the reduction minus HP it will remove, both after the target's current block (pets use their owner's block) and capped at the target's HP. Same "HP, not raw damage" rule as everything else: a hit your block would have soaked anyway prevents nothing.
- **Credit:** split by stacks each player put into that Weak instance, like Vulnerable. Everyone gets their share, including the player who was hit: preventing damage to yourself isn't counted anywhere else.
- **Known gap:** HP-loss effects applied after block (Intangible, Buffer) aren't simulated, so a hit they cap can overstate prevention slightly. Tungsten Rod-style flat reductions cancel out.
- **Shown:** a teal "Prevented" column on the Defense tab and image (only when anyone prevented something), with a note naming the debuffs; "−N dmg" per player and "N prevented" on the Weak group of the Debuffs tab and image. Stored as `PlayerTotals.DebuffPrevented` by power.

## Not included

- Additive damage-boosting debuffs (only multipliers are measured).
- Debuffs per fight on the timeline.
