# Who Carried? — controller support

- **Date:** 2026-09-13
- **Status:** design approved in chat, section by section; built on `feature/controller-support` (plan: `docs/design/plans/2026-09-13-controller-support.md`). Game internals checked against decompiled v0.111.0.

## Problem

The recap only works with a mouse and keyboard. During a run the only ways in are F8 and clicking the podium in the top
bar, and inside it the tabs, buttons, scrolling lists, chart readouts and card lift all need a mouse. On a Steam Deck
there's no hover at all. Worse, while the recap is open, controller presses reach the game underneath: the left bumper
opens the deck view behind it.

The recap already opens by itself 1.5 s after the victory or defeat screen, so that path needs no new way in.

## Decisions (the owner, in chat)

- **Getting in mid-run:** through the top bar's own controller navigation. No new button, no pause-menu entry, no R3.
- **Scope:** everything, including the per-fight and per-card details, not just open/close/tabs.
- **Approach:** the mod's own input handler catches every controller press while the recap is open and translates it,
  and each tab says what the d-pad can select. The selection looks exactly like mouse hover. Rejected: Godot's focus
  system for everything (a chart would need 20–50 focusable points, the game's focus manager grabs focus back, and
  presses could leak to the game's controls behind the panel); a mix of both (two systems for little gain).
- **Button hints:** the game's own controller glyphs (they follow the controller type and rebinding). The no-game-art
  rule covered only the podium icon.

## How the game does controllers (v0.111.0)

- Raw buttons are Godot actions named `controller_*` (`Controller` class). `NInputManager._UnhandledInput` translates
  each into a game action (`MegaInput`: `ui_cancel`, `mega_view_deck_and_tab_left`, …) through its private
  `_controllerInputMap`, which holds the player's rebinding, and re-injects it with `Input.ParseInputEvent`. Keyboard
  shortcuts and Steam Input arrive as the same game actions.
- Default map: A `select`, B `cancel`, X `topPanel`, Y `confirm`/`endTurn`, LB `viewDeckAndTabLeft`, RB
  `viewExhaustPileAndTabRight`, LT/RT draw/discard pile, View `viewMap`, Start `pauseAndBack`, L3 `peek`, d-pad
  `up/down/left/right` (`ui_up` …), right stick `altUp/altDown/altLeft/altRight`. Every button is taken; bumpers double
  as tab left/right on menus.
- Map, Deck and Settings aren't in the d-pad navigation; they're shortcuts. X (`topPanel`) jumps to the top bar, where
  `NTopBar.UpdateNavigation` (private, rerun whenever potion slots, modifiers or the active screen change) chains HP,
  gold, potions, room/floor/boss icons and the modifiers with focus neighbours.
- `NControllerManager.Instance.IsUsingDirectionalNavigation` says whether the game is in controller mode; its
  `ControllerDetected` and `MouseDetected` signals say when that changes. `NInputManager.Instance.GetHotkeyIcon(action)`
  returns the glyph for a game action on the current controller.

## Getting in: the podium

- The podium becomes focusable (`FocusMode.All`) and joins the end of the top bar's chain: a Harmony postfix on
  `NTopBar.UpdateNavigation` sets the last item's right neighbour (the last modifier if any, else the boss icon, else the
  floor icon) to the podium, and the podium's left neighbour back to it. Its up neighbour is itself and its down
  neighbour is the one the game gives the other top-bar items (the first relic). Because the postfix reruns with the
  game's own update, the chain stays right as potions and modifiers change.
- **Focused** looks like hovered: grows 10%, brightens, shows the tooltip. Losing focus settles like the mouse leaving.
- **A** (`select`) on the focused podium opens the recap.
- **Closing** the recap gives focus back to whatever had it: the podium when it opened the recap, the victory or defeat
  screen's button at the end of a run.

## While the recap is open

`UI/PadInput` sits on the recap panel for as long as it's open. The mod can't override Godot's input methods (the game
loads mod DLLs without registering Godot script classes), so the panel **takes key focus** instead: Godot then hands it
every non-mouse event through its `GuiInput` signal, before the game's input manager and hotkeys (`_UnhandledInput`) and
before focus navigation. When the game takes focus away (it does on switching input mode), the panel takes it straight
back. The first controller press after using the mouse only switches the game to controller mode; the game eats that
press everywhere. The panel catches:

- **raw controller events** (`InputEventJoypadButton`, `InputEventJoypadMotion`): translated through the game's own
  `_controllerInputMap` (read by reflection), so rebinding carries over;
- **game actions** (`InputEventAction` and any event matching a `MegaInput` action): keyboard shortcuts and Steam
  Input arrive this way.

Every one of them is marked handled, used or not, so nothing reaches the game underneath. Mouse events and F8 are left
alone.

| Game action (default button) | In the recap |
|---|---|
| `viewDeckAndTabLeft` / `viewExhaustPileAndTabRight` (LB / RB) | previous / next tab, wrapping round |
| `cancel` (B), `pauseAndBack` (Start) | close |
| `confirm` (Y) | save image |
| `up/down/left/right` (d-pad, left stick) | move the selection in the current tab |
| `altUp/altDown` (right stick) | scroll the current tab's list |
| everything else (A, X, triggers, View, L3) | nothing, swallowed |

Keyboard keys bound to those game actions do the same inside the recap (the view-deck key switches tabs), as they do on
the game's own tabbed screens.

## What the d-pad selects

Each tab lists what can be selected as **rows**, each with a number of items, and can show item N of row R the way
hover would, clear it, and scroll. The selection looks exactly like mouse hover; nothing new is drawn.

| Tab | Rows | Up/down | Left/right |
|---|---|---|---|
| Scoreboard | 1: the players' cards (lift one, as hover does). 2: the climb chart's fights (readout of who dealt what) | switch rows | step |
| Awards | none | — | — |
| Sources, Debuffs | none | scroll | — |
| Timeline | 1: the chart's fights (readout follows) | — | step |
| Defense | none | — | — |
| Decks | 1: the player banners (picking one shows that deck at once) | scroll | step |

- **Nothing is selected** when a tab opens. The first press selects: the first item of a card or banner row, the
  latest fight of a chart row (the most interesting one mid-run). On Decks the first press lands on the banner already
  shown.
- **Left/right stop at the ends** (no wrap inside a row). Up/down skip rows with no items (an empty chart early on).
- **Holding a direction repeats**: first repeat after 0.4 s, then every 0.08 s, so a 50-fight chart can be swept.
- **Switching tabs** clears the selection.
- **Live updates** (a fight added mid-run) keep the selection on the same item if it still exists, else the nearest.
- **Mouse and controller mixed:** when the game switches to mouse mode (`MouseDetected`) the controller selection is
  cleared and hover works as today; the next d-pad press brings a selection back.
- Mouse hover and the controller call the same "lift this card" and "show this fight" code. The hover handlers are
  reorganised into those entry points (the Timeline already has one for the dev preview, `PreviewShowFight`).

## Button hints

In controller mode the game's glyphs appear: LB and RB at the two ends of the tab bar, B beside Close, Y beside Save
image. They come from `NInputManager.GetHotkeyIcon` for the game actions above, update on `ControllerDetected`,
`MouseDetected` and the input manager's `InputRebound`, and hide in mouse mode. If a glyph can't be found, that hint
is left out. The exported image never shows them.

## Units

| Unit | Does | Depends on |
|---|---|---|
| `Core/PadCursor` (new, pure) | The selection: none or (row, item); moving by direction; row switching that skips empty rows; clamping; where the first press lands; keeping the place when counts change; tab wrap; hold-repeat timing. | nothing |
| `UI/PadInput` (new) | Catches and swallows controller input while the recap is open, turns it into commands, drives `PadCursor` and the panel. | game input types, `PadCursor` |
| `UI/RecapPanel` | Tab switching, close and save by command; button hints. | |
| Tabs (Scoreboard, Timeline, Decks, Sources, Debuffs) and `Climb`, `CardFace` | Say their rows; show/clear an item; scroll. Hover uses the same entry points. | |
| `UI/TopBarButton` + a patch on `NTopBar.UpdateNavigation` | Podium focus, focus look and tooltip, A to open, focus back on close. | |

## Errors

Every handler is wrapped like the mod's other game-facing code. If the controller map can't be read or a hook fails
(for example after a game update renames something), controller support switches off with one log line and the recap
keeps working with the mouse and F8 as today. A podium that can't join the chain stays mouse-only.

## Testing

- **Unit tests** (`PadCursorTests`): stepping and clamping; row switching and skipping empty rows; first-press rules
  (first item, latest fight, the banner already shown); keeping the place when a row grows or shrinks; tab wrap both
  ways; hold-repeat counts at given hold times.
- **Dev preview:** after its existing screenshots, it simulates presses by injecting the game's actions and raw
  joypad events, and saves:
  - `preview-12-pad-tab.png`: RB moved to the next tab;
  - `preview-13-pad-card.png`: the d-pad lifted a scoreboard card;
  - `preview-14-pad-fight.png`: the Timeline stepped to a fight with its readout;
  - `preview-15-podium-focus.png`: the podium focused on the top bar, with its tooltip.
  It also logs what each press did in the recap, and whether the game switched to controller mode (it only does while
  its window has focus).
- **In game (the owner):** with a real controller, reach the podium with X and the d-pad, open with A, switch tabs,
  step through the charts and cards, save, close with B. On a Steam Deck if possible, since Steam Input delivers
  presses differently.

## Not in this change

Steam Deck specifics (where saved images go under Proton, text size on a 7" screen) and the game's keyboard-only mode.
