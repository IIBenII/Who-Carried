# Design docs

The specs and plans the mod was built from, in the order they were written (September 2026). They're a record of how it got here, so they aren't updated as the code changes.

**The mod was called "Run Recap" when these were written.** It was renamed **Who Carried?** before release, because another Workshop mod already uses that name. Where the docs say `RunRecap`, the code now says `WhoCarried`: `src\RunRecap\` is `src\WhoCarried\`, `<game>\mods\RunRecap\` is `<game>\mods\WhoCarried\`, and so on. `<game>` is your Slay the Spire 2 folder.

They were written with Claude Code, which is why they mention Claude building, deploying and reading the logs.

| Spec | Plan | What it covers |
|---|---|---|
| [Run Recap — design](design/specs/2026-09-10-run-recap-design.md) | [plan](design/plans/2026-09-10-run-recap.md) | The first version: what gets counted, who gets credit, how events are captured without touching gameplay |
| [Visual redesign + PNG export](design/specs/2026-09-10-run-recap-visual-design.md) | [plan](design/plans/2026-09-10-run-recap-visual.md) | The game's own fonts and art, and saving the recap as an image |
| [Decks](design/specs/2026-09-10-run-recap-decks-design.md) | [plan](design/plans/2026-09-10-run-recap-decks.md) | Each player's deck at the end of the run |
| [Debuffs](design/specs/2026-09-10-run-recap-debuffs-design.md) | | Debuffs each player applied and received, and the extra damage their Vulnerable set up for the team |
| ["Post-match broadcast" redesign](design/specs/2026-09-10-run-recap-broadcast-design.md) | | A sports-broadcast look, later replaced |
| ["Dealt" redesign](design/specs/2026-09-12-run-recap-dealt-design.md) | | The card-table look the mod ships with |
| [Fair Poison and Doom split](design/specs/2026-09-12-poison-doom-split-design.md) | | Sharing Poison ticks and Doom kills by each player's part of the pile, instead of crediting whoever started it |
