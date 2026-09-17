# The recap's controls

The rebindable hotkey works. The control that changes it does not belong in the panel, and
neither does the button beside it.

Three things are wrong, and they are the same thing seen from three sides.

**They don't look like the mod.** The recap is a painted thing: words, a gold stroke that
swipes in under the chosen tab, coins ringed in each player's colour, ribbons, card art, and a
bar that reads the run out as icon-and-value pairs. It contains no buttons anywhere else. The
two controls in the tab row are `res://images/ui/reward_screen/reward_skip_button.png` — the
reward screen's stone, in the reward screen's mint, carried into a room it was not drawn for.

**The export sits under the icon that opens the recap.** The game's own cluster — our podium,
map, deck, settings — is at the top right. `Save image` is directly below it. At 16:9 the podium
lands in the 19px seam between the two controls; on a wider screen the recap is letterboxed and
centred while the game bar is not, so at 2000×1019 the podium sits at x≈1680 and `Save image`
starts at x≈1673. One stray click writes a screenshot, and nothing about the button says it is
about to.

**A setting outweighs an action.** `Change hotkey (F8)` is 248px, the widest control on screen,
wider than the export it sits beside. It is a thing you touch once.

Two defects fall out of the same code. `Save image` passes a raw `Texture2D` to `Button.Icon`
with `ExpandIcon = false` and no `IconMaxWidth`, so the icon draws at native pixel size, inflates
the button's minimum height past the 46 it was given, and does not scale with the screen. And the
`TextureMargin*` values were dropped from `HewnBox`, so `StyleBoxTexture` scales the whole bitmap
instead of stretching its middle: the same art at 248 wide and 170 wide gets differently shaped
end caps. Measured off the render, the two buttons stand 32px and 44px tall. They were never
going to look like a set.

## What replaces them

**The export moves left and becomes a stone we cut ourselves.** It sits after the tabs, reads
**Export as image**, and is drawn by the mod rather than loaded from the game: chamfered corners
instead of the reward screen's pointed banner, a bronze edge taken from the recap's own gold, a
dark face that belongs to the table under it, and a cast shadow so it rests on the panel instead
of floating over it.

**The hotkey stops being a button.** It becomes a key cap and a line of text in the top bar,
after the party — one more readout in a bar that already reads out floor, time, ascension and
team damage. Clicking the cap starts listening, exactly as clicking the old button did.

**The top-right corner is left empty.** Nothing of ours sits under the game's icons.

### Why a key cap rather than the same stone

The cap is the one place the design stops matching itself on purpose. A small slab would tie the
two controls together as one object at two sizes; a key cap says *this is a key on your keyboard*
without being read first. Legibility wins over cohesion here, because the line has a job to do
that the button doesn't: it has to be understood by someone who never wondered whether the recap
had a hotkey.

### Geometry

Design pixels (the 1600×900 space the panel lays out in), derived by measuring the 1440-wide
preview render and dividing by its 0.9 scale.

| | |
|---|---|
| Tab band | `HandLayout.TabsTop` 88 → `TabsBottom` 118, thin rule at `TabLine` 124 |
| `Decks` right edge | 679 |
| Export slab | x 739 (two 30px tab gutters clear of Decks), height 42, top 82, **bottom on `TabLine` 124** |
| Label | 19px, the panel's serif — no mark; the words are the whole face |
| Key cap | height 30, min width 38, label 16px; in the bar row after the party |

Centring the slab on the tab band and resting it on `TabLine` are the same placement — 82 to 124
is centred on 88–118. That shared baseline is what the old buttons never had: they floated at
their own height with nothing in the panel agreeing with them.

The slab carries no icon. The stone already reads as something to press and the words already say
what it does; a mark beside them is a second voice saying the same thing, in a panel whose whole
argument is that it does not need chrome. It also frees the icon slot for the controller glyph,
which is the only thing that ever needs to sit there.

That leaves the slab about 35 design px wider than the `Save image` button it replaces — the
longer label, less the mark — which is harmless now that it lives at x 739 instead of x 1395. The
mis-click this design set out to fix does not come back with the width.

### The stone

A small generator, not a game texture. `Core/HewnStoneArt.cs` returns RGBA bytes and its 9-slice
margins for a chamfered stone: a two-pixel bronze rim, a dark face, a lit top edge and a shadowed
bottom one, with the corners stepped rather than smoothed. `UI` wraps it in an `ImageTexture` and
a `StyleBoxTexture` with the margins set, so one texture serves every size and nothing stretches
at any width. This is how the mod already makes art it cannot find in the game — `TopBarIconArt`
draws the podium icon and `HardPixels` gives it the game's stepped edges.

Putting the pixel maths in `Core` keeps it in the existing test runner. Nothing about it needs
Godot except the final `ImageTexture`.

The key cap needs no generated art: a `StyleBoxFlat` with a 2px ink border, a 4px bottom border
and a 4px radius is the cap, and a 2px highlight strip inside its top edge is the light on it.

### States

| | Export slab | Key line |
|---|---|---|
| Idle | bronze rim, dark face, shadow 3px below | `F7` on the cap, *toggles the recap* beside it, both dim |
| Hover | rim lights, stone lifts 2px off its shadow | cap and text brighten |
| Press | sinks 2px into the shadow, face dims | — |
| Listening | — | cap hollows to a dashed socket and reads `?`, the line goes gold: *press a key…*, with *esc cancels · del clears* faint beside it |
| Unbound | — | dashed cap reads `—`, the line reads *no key opens the recap* |

Hover and press are the feel `TopBarIconArt` already specifies for the podium
(`HoverBright`, `PressDim`), so the recap's controls and the mod's top-bar icon behave alike.

Close is dim at rest — its cap and word brighten together on hover, and nothing moves. It is a
word, not a stone, so it has nothing to lift.

### The status message

The key control goes into the bar's row directly after the party and before the existing
`Kit.Fill()`, so it reads as one more of the bar's readouts and the spacer still pushes the status
label to the right.

The status label needs a fix it should have had already. The row runs the full width between the
stage margins — at 16:9, x 27 to 1413 — and the status is its last child, so it right-aligns to
1413. Close is not in the row: it is positioned absolutely at x 1323 and added to the bar
afterwards, so it draws over the top. Every status message the export writes is therefore
partly behind the Close button today; *Saved to your Steam screenshots* loses its last third.
The row's right edge moves to Close's left edge less 20 design px, which puts the status between
the key line and Close where this design draws it. It keeps the four-second revert.

### Close

Close loses its stone and becomes a painted word with its key on a cap beside it, at the bar's
right end where it already sits. The recap closes on the game's own cancel binding — `PadInput`
maps both `MegaInput.cancel` and `MegaInput.pauseAndBack` to `PadCommand.Close` — and
`GameCompat.Hotkey` can read what that binding is, so the cap shows the real key rather than a
guess. The bar then says one sentence about how the recap works: the key that opens it at one end,
the key that shuts it at the other, on the same cap.

Close is the one control nobody hunts for, and Esc already does the job. Giving it mass spends
weight on the least important thing in the panel, and it leaves the export as the only stone in
the recap — one action with weight, everything else painted on.

The cap appears only when the key reads back and controller support is live; otherwise the word
stands alone, which is today's behaviour, so the fallback costs nothing. The key it shows is the
game's, not ours: a player who has rebound cancel sees their own key.

With none of the three controls left on a game texture, `GameArt.ActionButton`,
`GameArt.EventButton` and `GameArt.Share` have no readers and come out, along with `HewnButton`,
`HewnBox`, `HewnShadow` and `AnimateHewn` in their current form. `RecapUi.HotkeyName` is already dead and
goes with them.

## What this does not change

The rebind itself. `Core/Settings`, `UI/HotkeyBinding` and `UI/HotkeyRebind` keep their behaviour,
their tests and their failure handling; only the thing that starts them moves from a button in the
tab row to the key cap in the bar. `HotkeyRebind.Attach` still takes the panel handle, still owns
its own `GuiInput` subscription, and `PadInput` still stands aside through `Consumes`.

## Controller and Steam Deck

Neither control is on the controller's path to begin with. `PadInput.Do` calls `_panel.Close()`
for `PadCommand.Close` and `_panel.Save()` for `PadCommand.Save` directly, driven by the game's own
cancel, back and confirm actions. Closing and exporting with a pad never touch these controls'
focus or their looks, so restyling them cannot break either.

What changes is which hint each one wears, and `PadHints` already swaps hints live on the game's
`ControllerDetected`, `MouseDetected` and `InputRebound` signals:

- **The export** stays a `Button`, and `PadHints.OnButton` keeps working with no icon at all: it
  records the button's mouse icon — here, nothing — and swaps the confirm glyph into the empty
  slot when a controller appears. `IconMaxWidth` is still set, and still matters: the glyph is a
  texture like any other, so without it the height defect would simply come back on a pad.
  Dropping the mark also leaves `GameArt.Share` with no readers.
- **Close's cap and its glyph are the same idea for two devices.** `PadHints.Glyph` already shows a
  `TextureRect` only in controller mode; the cap needs the inverse, so `PadHints` gains a
  `MouseOnly(Control)` that hides a control on a pad. Three lines in the same `Update` loop.
  A pad player sees the B glyph where a keyboard player sees `Esc`.
- **The hotkey line keeps its cap in both modes.** Rebinding stays mouse-and-keyboard only, as the
  hotkey design already said, and the cap still tells a pad player the truth about their keyboard.
  This is the one place the bar shows a key and a glyph side by side, and it is deliberate: that
  key is ours and is not a controller action, while Close is whatever you are playing with.

## Error handling

Unchanged in kind: every entry point stays wrapped, a failure logs one line through
`Tracker.LogError` and leaves the recap working. The generated stone is the one new failure mode —
if the texture cannot be built, the controls fall back to the existing `RecapTheme.Box` styling,
which is what `HewnBox` already does when a texture is missing.

## Testing

`Core/HewnStoneArt` is plain pixel maths and gets unit tests beside `HardPixelsTests`: the rim
colour on the outside edge, the chamfer leaving the corner pixels transparent, the margins
dividing the image into a 9-slice with a stretchable middle, and a bad size returning nothing
rather than throwing.

The rest needs the game. The dev preview renders the panel and can show the new controls in their
idle state; hover, press and the listening prompt need a real run. Check on both supported
branches, and specifically:

- At a wide aspect ratio, with the recap open, nothing of ours is under the podium.
- Esc closes the recap and the cap beside Close names the key that did it.
- Picking up a controller swaps Close's cap for the confirm-and-cancel glyphs and puts the pad
  glyph on the export; putting it down puts the cap and the share arrow back. Both directions,
  without reopening the panel.
- The export and Close still work from a pad with the panel never touched by the mouse.
- With the key binding unreadable — the path `PadInput` already guards with `_failed` — Close is
  a bare word and nothing throws.

## Files

| File | Change |
|---|---|
| `src/WhoCarried/Core/HewnStoneArt.cs` | New. The chamfered stone as RGBA bytes plus its 9-slice margins. |
| `src/WhoCarried/UI/HewnStone.cs` | New. Texture, style boxes, the lift-and-sink behaviour, the key cap. |
| `src/WhoCarried/UI/RecapPanel.cs` | Export slab after the tabs; key line in the bar; Close restyled; old hewn helpers removed. |
| `src/WhoCarried/UI/HotkeyRebind.cs` | Starts from the key cap; drives the cap's states and the line's text. |
| `src/WhoCarried/UI/RecapUi.cs` | `HotkeyName` removed; export status text unchanged. |
| `src/WhoCarried/UI/PadHints.cs` | `MouseOnly(Control)`: hide a control in controller mode, the inverse of `Glyph`. |
| `src/WhoCarried/UI/GameArt.cs` | `ActionButton`, `EventButton` and `Share` entries removed. |
| `tests/WhoCarried.Tests/HewnStoneArtTests.cs` | New. |
| `README.md` | The hotkey line, and the button's new name. |
