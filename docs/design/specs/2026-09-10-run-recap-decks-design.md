# Run Recap — Decks

- **Date:** 2026-09-10
- **Status:** approved in chat. The option chosen was "real cards in panel, list in PNG". Built while the owner plays; installed afterwards.
- **Builds on:** `2026-09-10-run-recap-design.md`, `2026-09-10-run-recap-visual-design.md`

## What

Show every player's current deck.

**In the panel:** a 5th tab, **Decks**, with player chips (like Sources) and a scrollable grid of the **game's own rendered cards**. Six fit per row, each at 0.44 scale (132×186 px). Duplicates are grouped into one card per card id:
- a **×N** badge shows the copies
- a "**N+**" (or "**+**" when every copy is upgraded) marks upgraded copies
- a **damage badge** shows what that card id dealt this run, and only appears when it's above 0

**In the exported PNG:** a compact **Decks** section with one column per player (up to 4). Each column has a header (icon, name, "N cards"), then **ATTACKS / SKILLS / POWERS / OTHER** subheads. Each line holds:
- the card name, colored by rarity (Basic muted, Common cream, Uncommon blue, Rare gold, Curse purple, Status faint, other teal)
- ×N, then the upgrade marker in green
- damage, right-aligned ("—" when 0)

**Ordering:** Attack → Skill → Power → everything else. Within a type, damage descending, then name.

**Which deck:** the deck at the moment the panel opens, read live and not saved. At run end that's the final deck. Cards created mid-fight (Shivs) and cards removed from the deck don't appear, but their damage still shows under Sources.

## How

- **Core (pure, tested):**
  - `DeckCard(Id, Label, Type, Rarity, UpgradeLevel)` holds the facts.
  - `DeckEntry(Id, Label, Type, Rarity, Count, UpgradedCount, Damage)` is one grouped card id.
  - `DeckView(PlayerId, PlayerLabel, ColorHex, IconKey, CardCount, Entries)` is one player's deck.
  - `DeckBuilder.Build(stats, players, decks)` groups by id, looks up damage from the player's `Card:<id>` source total, and sorts.
  - `RecapBuilder.Build(..., decks = null)` puts the result on `RecapView.Decks`, in the same damage order as Overview and Sources.
- **Game:**
  - `GameReader.Decks(run)` reads `player.Deck.Cards` into `DeckCard` facts.
  - `GameReader.DeckCardModel(run, playerId, cardId)` returns the most-upgraded copy, which is the one drawn.
- **Real cards (`UI/CardVisuals`):** these mirror the game's deck viewer.
  - `NCard.Create(model, Visible)` is added inside a fixed-size holder once the slot enters the scene tree (`Ready`), then `UpdateVisuals(PileType.Deck, CardPreviewMode.Normal)` is called. After that, `Scale = 0.44` and `Position = -PivotOffset × (1 − 0.44)` so the scaled card sits at the slot's top-left.
  - **Pool safety:** cards come from the game's `NodePool`. Every card handed out is tracked, and on tab/player switch and panel close `ReleaseAll()` restores its original `MouseFilter` and calls the game's `QueueFreeSafely()`. That removes the card immediately and returns it to the pool. The game itself resets position, rotation, and scale when it next hands the card out (`OnReturnedFromPool`). Anchors are never touched.
  - **Mouse:** the card's `MouseFilter` is `Ignore` while shown, so the wheel scrolls the grid. It's restored on release.
  - **Fallback:** if a model can't be found or drawing fails, a text tile appears (name + type, border in the rarity color). The error is logged and the tab still works.
- **Dev preview:** sample decks come from each vanilla character's `StartingDeck` plus 10 non-basic pool cards. Sample damage is recorded against those cards' ids, so badges show. There are 5 tab screenshots plus `preview-6-export.png`.

## Out of scope

Card hover tooltips (they'd need the game's card-holder wrapper), card counts in Sources, and deck history over the run.
