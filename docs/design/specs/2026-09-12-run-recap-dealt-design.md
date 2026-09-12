# Run Recap: "Dealt" redesign

Approved by the owner on 2026-09-11 from the mockups (`docs/mockups/dealt-*.png`; sources in `docs/mockups/src/`).
The run is told in the game's own card language. This replaces the plum "broadcast" look everywhere: the panel's
seven tabs and the saved image. Data, tracking and the live-update behaviour stay as they are.

## Look

- **Table:** dark blue-grey spotlight (`#1c2738` → `#070a0f`), a vignette. Kreon (the game's card font) for all text,
  with dark outlines and drop shadows like the game's labels.
- **Top bar:** the game's `top_bar` texture. "Run recap · Victory" (result in gold), then floor, run time, ascension
  and team damage with the game's top-bar icons, the party's character icons, then status, Save image and Close.
- **Tabs:** plain text; the chosen one is white with a gold brush stroke (`map_circle_0`) under it that slides.
- **The card** (one widget, any width; sizes scale with it like the mockup's `em`):
  portrait (character-select art, or an award's icon on a glow of the player's colour) → `card_frame_ancient_s`
  tinted with the player's colour → optional foil sheen (animated shader) → `ancient_banner` tinted, name on it →
  energy gem top-left (`CardPool.EnergyIconPath`, so modded characters get theirs) with the rank or the player's
  face → type plaque (the headline award, gold on dark) → text box (caller's content) → badges on the bottom edge.
  Drop shadow; the leader's card glows gold.
- **Tip panels:** like the game's hover tips: `rgba(14,20,31,.92)`, 2px `#3f5064` border, radius 10.
- **Colours:** gold `#f3cf6a`, green `#7fe0a0`, teal `#6fd6c0`, taken `#ff7a6a`, blocked `#7cc0ff`, healed `#8fe07a`,
  text `#fbf6ec`, muted `#a9b4c2`, faint `#6f7c8c`. Card-type frames: attack `#c2493d`, skill `#3f9a5a`,
  power `#4a78c0`, relic `#c9a44a`.

## Tabs

1. **Scoreboard:** the hand (1–4 cards fanned, leader first, scaled up, foil + glow). Card text: damage dealt,
   share %, block removed, bonus damage (solo: biggest hit instead of share and bonus). Right: top sources with art
   thumbnails. Bottom: "The climb", stacked damage bars standing on the map's room icons along a dotted path,
   biggest fight named in the heading, player swatches. Solo: one big card plus a "Your run" story panel.
   Unattributed damage and the bonus-damage explanation go in a muted line under the hand.
2. **Awards:** one small card per award (award icon as art, title on the banner, winner's face in the gem, winner's
   name on the plaque, number + meaning in the text box); two rows. Right: "Relics of the run", each player's
   game badges with rarity-coloured names.
3. **Sources:** a column per player: banner header with rank gem and total, then every source with its art
   (card portrait / relic / power icon, framed in its type colour), bar, block removed. Cards created under it.
4. **Debuffs:** keyword-tooltip boxes in three columns (icon + gold name, stacks, what it did, bar per player),
   then "What enemy debuffs cost you" per player. Scrolls when long.
5. **Timeline:** legend, framed line chart with cased lines, gold act labels, room icons along the x-axis,
   tooltip-style readout.
6. **Defense:** a nameplate per player (portrait, name, character, block shield with the number, HP-style bar of
   taken + healed, blocked, kept off the team), then a team totals strip and the explanation.
7. **Decks:** banner tabs per player, type-count chips, the deck as real cards (as now).

## Saved image

1200 wide, top to bottom: top bar with date, the hand, awards as condensed two-column tiles, relics of the run,
top sources (4 columns), the climb, debuffs (3 columns), defense (2 columns), decks as text lists, footer with seed.

## Data additions

- `FightBucket.Room` / `FightPoint.Room`: "monster", "elite", "boss", "unknown" (event fight), from
  `run.CurrentMapPoint.PointType` live, from the saved run's `map_point_history` on replay. Old stats: "".
- `RunFacts(Floor, Ascension, Seconds, Seed)` on `RecapView` for the top bar and footer.
- Source rows carry an art key ("card:ID", "relic:ID", "power:ID", "potion:ID"; pet hits use the card that sent them).
- `DefenseRow` carries the character name and the lowest HP.
- Icon keys "portrait:CHAR" and "energy:CHAR" resolve through `ModelDb.AllCharacters`.
- Award renames: Shield breaker → **Siege breaker**, Untouchable → **Unscathed**.

## Sizing

Layout is authored at 1600×900 like the mockups and multiplied by `S = min(width/1600, height/900)` (no transform
scaling, so text stays sharp). The stage is centred; the table background fills the screen. The saved image uses S = 1
at 1200 wide.

## Motion

Cards are dealt in when the recap opens (≤ 0.7 s total, so preview screenshots are settled). The leader's foil sheen
sweeps every few seconds. Cards lift on hover. Live updates keep counting numbers and sliding bars; when ranks change
the cards glide to their new places. The tab underline slides.

## Progress (2026-09-12, overnight): built and installed

All seven tabs and the saved image are built, installed in the game, and checked against the mockups with the dev
preview (1, 2 and 4 players; the flag file can hold the party size) and a replay of the Heart run. Tests 78/78. The
old `RecapWidgets.cs` is gone (Recycle Bin; also in a local pre-redesign backup).

Motion in: cards dealt in on open, the leader's foil sweep, cards lift on hover (scoreboard and awards), live glides,
the sliding tab brush.

Lessons from the first in-game run:
- A `TextureRect`'s `ExpandMode` must be set before its size, or it can't shrink below the texture (`Kit.Pic`).
- The game's atlas sprites carry a transparent margin; `GameArt.Trim` drops it so icons fill their boxes.
- The card frame's text box is 60% black; `Kit.Dyed` reproduces the mockups' multiply-masked tint so it takes the
  player's colour.
- "What enemy debuffs cost you" hides when nobody has any (runs logged before it was tracked).

## Build stages

1. Theme, game art loader, card widget; data additions and renames; tests.
2. Top bar, tabs, Scoreboard (1–4 players) with the climb and top sources.
3. Awards, Sources, Debuffs.
4. Timeline, Defense, Decks.
5. Saved image.
6. Motion and polish; replay the Heart run and compare with the mockups.
