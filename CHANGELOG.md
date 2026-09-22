# Changelog

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format, [semver](https://semver.org/spec/v2.0.0.html) numbers.

## [Unreleased]

### Added

- Defense: HP a pet loses to enemies (Osty taking hits for the Necrobinder) shows as **tanked by pets**, on its owner's nameplate, in the team totals and on the exported image. It isn't part of the owner's damage taken.
- Experimental, off by default: `"experimentalEffectSources": true` in `settings.json` credits damage a modded effect deals with no dealer and no card (Hextech Runes' Burn, for one) to that effect and whoever applied it, instead of Unknown. Such a tick on a poisoned enemy is no longer counted as Poison. Read at start-up.
- Damage a mod's armour stops between block and HP (Zone the Spire's Marbled) is counted as block: chipped off an enemy it counts as enemy block knocked off, under the card that did it; soaked on a player or their pet it counts as that player's damage blocked. Measured at the game's own HP-loss hook, so any mod's layer counts, named or not.

### Fixed

- Co-op guests no longer lose the recap's earlier fights when the host reloads the run. It hit guests on the game's public branch, the first time a run was reloaded.
- Strength-down (Enfeebling Touch, Piercing Wail, Malaise) gets credit for an enemy attack it takes all the way to 0: the attack's own size, not the Strength removed.
- Changing the hotkey keeps the rest of `settings.json`.

## [1.1.0] — 2026-09-17

### Added

- Rebindable hotkey: click the key on the recap's top bar and press the one you want. Esc cancels, Delete clears. Saved in `settings.json`; still F8 by default.

### Changed

- The recap's controls are drawn by the mod now, not borrowed from the reward screen.
- **Save image** is **Export as image**, and has moved from the top-right corner to beside the tabs.
- The hotkey is a key cap in the top bar; **Close** is a word wearing the game's cancel key.

### Fixed

- Opening the recap no longer risks clicking **Save image** underneath it.
- *Saved to your Steam screenshots* no longer runs under the Close button.
- Buttons keep their shape at every width, and an icon no longer stretches one taller.
- The controller's confirm glyph appears on the export button.

## [1.0.0] — 2026-09-15

First Workshop release: the co-op recap in seven views, live while you play; the podium button and F8; Save image to Steam screenshots; controller support; Poison and Doom split by share of the pile.
