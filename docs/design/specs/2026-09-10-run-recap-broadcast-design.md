# Run Recap: "Post-match broadcast" redesign

The owner asked for "exceptional UI" and picked direction C of four mockups (`docs/mockups/`: A card reward, B the ascent map, C post-match broadcast, D grimoire). They declined bundling downloaded fonts, so the build uses only fonts the game ships.

## Tokens

- **Stage:** plum `#1b1331` with a soft glow `#2d2054` (radial, left of centre) and faint 115° pinstripes.
- **Chrome:** panel `#251b40`, edge `#3c2f63`, bar track `#33275a`.
- **Text:** light `#f2eefc`, muted `#9b92b8`, faint `#6e6590`, dark ink `#160d27` on bright colours (`InkOn` switches to light ink for dark modded character colours).
- **Accents:** the players' character colours only. Result chip: defeat `#ff5555`, victory `#ffd35c`. Stats: taken `#ff6b6b`, blocked `#7cc8ff`, healed `#8fe36b`, prevented `#5dcaa5`, bonus `#d9a3ff`.
- **Type:** Fira Sans Extra Condensed Bold (shipped with the game for Russian, `res://themes/fonts/rus/…`) for numbers, names, headings and buttons; Kreon for sentences and small labels. No synthetic emboldening (it notches digits).
- **Shape:** everything interactive or ranked leans 8° (`StyleBoxFlat.Skew = 0.1405`); slabs are gradient parallelograms drawn with `DrawPolygon` from the `Draw` signal (no script overrides needed).

## Layout

Full-screen stage (not a floating panel), margins 80/40. Header: "Run recap", result chip ("Defeat on floor 37"), status, Save image, Close. Slanted view tabs:

1. **Scoreboard:** a slab per player, as tall as their damage (min height so text fits), with rank, name, character, a huge number, share and bonus damage; top 3 sources per player on the right; "Damage per fight" stacked columns with act dividers, floor numbers, tags on the biggest and last fight, and a hover readout.
2. **Sources:** a column per player, slanted colour header with total, then up to 7 sources with slim bars.
3. **Debuffs:** cards per debuff (icon, stacks, bonus or prevented line, a bar per player), then received-from-enemies cards.
4. **Timeline:** line chart with act lines, floor axis and a hover readout that snaps to the nearest fight.
5. **Defense:** a slanted band per player with big numbers and a bar per stat; "Prevented" only when someone prevented damage.
6. **Decks:** player chips and the game's real cards at half size.

The saved image (1200 px wide) stacks the same pieces: header, slabs, source columns, fight strip, debuff cards (2 columns), received, defense, deck lists.

## Live updates (2026-09-11)

The open recap updates in place instead of being rebuilt:

- `Tracker.Changed` fires after anything is counted; `RecapUi` refreshes at most every 0.25 s, plus an idle refresh every 1.5 s for floor, deck and HP (which raise no events). Each refresh builds a new `RecapView` and hands it to the widgets through `Live`.
- Numbers count to their new value (`LiveNumber`), bars slide (`LiveBar`), slabs grow and reorder, and lists keep row identity by key (`KeyedRows`), so new sources, debuffs and fights appear as new rows or columns.
- The fight strip and the line chart are drawn in one pass each and interpolate from the shown values to the new ones; new fights grow in from zero. Hover readouts refresh with the new numbers.
- Rebuilds happen only where the structure really changes: the Defense table when the Prevented column first appears, and the Decks grid when the chosen deck gains, loses or upgrades a card (otherwise only its damage badges change).
- Save image exports the latest view.

## Extra acts (Heart of the Spire mod)

The mod adds a real fourth `ActModel` (`CorruptHeartAct`), so fights there get act 4 from `CurrentActIndex` and every chart labels them "Act 4"; nothing assumes three acts. The Corrupt Heart's damage cap (`HeartInvinciblePower`) is honoured because damage is HP actually removed, and a capped hit gets no Vulnerable bonus. Beat of Death hurts players, so it shows in damage taken.

## Feedback round after the 4-player Heart run (2026-09-11)

- **Damage dealt = HP removed; block removed is its own stat** (revised 2026-09-11 after trying "health + block"). Every damage number (slabs, shares, ranking, sources, fight strip, timeline, deck badges, team total) is HP only. Enemy block knocked off is shown underneath: "M block removed" on the slab and on source rows (hidden on narrow four-player columns). Stored as `SourceTotal.BlockRemoved` / `PlayerTotals.BlockRemoved`; fight buckets store HP only. The Vulnerable bonus is extra HP only.
- **Pets split by trigger.** A pet's hit is listed as "Osty via Unleash" (source id `OSTY>UNLEASH`), and the triggering card's deck badge includes it.
- **Strength loss counts as prevented.** On an enemy's attack: temporary Strength-down debuffs (subclasses of `TemporaryStrengthPower`, e.g. Piercing Wail, Dark Shackles) and lasting Strength a player removed (Malaise; kept per enemy, with each temporary debuff's own amount taken back out so nothing counts twice) are added back, times the hit's multipliers from the game's own `Hook.ModifyDamage` (multiplicative only). The HP difference is shared by how much Strength each player removed. Lasting loss shows as a "Strength loss" debuff with the Strength icon.
- **Cards created** per player from `Hook.AfterCardGeneratedForCombat` (enemy-added statuses have no creator and are skipped; cards transformed mid-fight count as created by their owner). Shown on the Sources tab and image.
- **Every debuff shown**, and each debuff card lists only the players who used it.
- **"What enemy debuffs cost you"** replaces stacks received (identical for everyone in co-op): extra HP taken from Vulnerable-style debuffs on the victim, damage not dealt from Weak-style debuffs on the attacking player, block not gained from Frail-style debuffs (measured in `Hook.BeforeBlockGained`, so previews aren't counted).
- **Unknown sources:** when the model stack is empty, attribution now falls back to the hook context's own model (`HookPlayerChoiceContext.Source`) and then `LastInvolvedModel`; unverified until the next run shows whether Ironside's 12 unnamed hits get a name.

## Awards and game badges (2026-09-11)

Chosen from the audit so support players get credit the damage ranking can't give them.

- **Awards** (`Core/AwardBuilder.cs`), each to the one leader (ties: higher rank), left out when nobody earned it. In order: Heavy hitter (biggest single hit, names the source), Enabler (bonus damage), Clutch (lowest share of max HP lived through, ≤ 30%), Protector (damage prevented), Wall (blocked), Shield breaker (block removed), Fight leader (fights out-damaging everyone, "10 of 23"), Jack of all trades (different damage sources, ≥ 5), Card factory (cards created, ≥ 5), Untouchable / Punching bag (least / most damage taken). Solo runs get only Heavy hitter and Clutch. A player's first award is their headline: a gold-text chip under the character name on the slab.
- **Clutch never counts going down.** Live: each fight's lows are kept only for players who finish it standing (a co-op revive at 1 HP isn't a close call). From the game's per-floor history (`Core/FloorLows.cs`): floors where the damage taken used up the HP the player came in with are skipped.
- **Game badges** are the game's own end-of-run badges, asked of the game when the run ends (`ScoreUtility.GetBadges` on the `SerializableRun` that `RunManager.OnEnded` returns), for every player, so guests get them too; abandoned runs get none, as in the game. Rebuilds read them from the saved run's `players[].badges`. Names and descriptions come from the game's `badges` localization table (rarity-specific wording first); pictures are `ui/game_over_screen/badge_<id>.png` inside `badge_<rarity>.png`.
- **Where:** badges stand on top of each slab (as many as fit, then "+N"); a new **Awards** tab (second) shows award cards (title on a gold chip, winner, number, meaning) and each player's badges with descriptions; the image gets an Awards grid and a compact badge shelf after the slabs. Mid-run the badge shelf says they come when the run ends.
- Dark player colours now lighten to luminance 0.45 (was 0.34) for text and bars, so The Tailor's brown reads on the plum panels.

## Godot gotchas found while building

- A label only measures itself once it's in the tree; layouts built before that must measure text with `Font.GetStringSize` (`RecapTheme.Measure`).
- `TextOverrunBehavior.TrimEllipsis` makes a label's minimum width 0; give fitted labels an explicit minimum width.
- Huge numbers sit in a line box ~1.2 em tall; `RecapTheme.Tight` crops to the digits.
