# Who Carried? — a button in the top bar

- **Date:** 2026-09-13
- **Status:** design approved in chat (icon and look chosen from mockups). Game internals checked against decompiled
  v0.111.0 and the game's own scene files.

## Problem

The recap opens by itself on the victory or defeat screen, but during a run the only way in is **F8**, and nothing in
the game says so. Players who don't read the mod's description never find it.

## Decisions (the owner, in chat)

- **A button in the game's top bar**, next to the Map, Deck and Settings buttons. F8 keeps working.
- **The icon is the mod's own art**, drawn by hand as SVG: a podium with a crowned winner's block (option A of five).
  No game art in the icon.
- **It must look like the game's own icons.** Same outline (black at 49% opacity, about 3 px at 1080p, not a solid
  dark line), same crunchy pixel edges, same vertical band in the bar.
- **Hover stays straight.** It grows 10% and brightens, like the game's buttons, but doesn't tilt.
- **The tooltip is the game's own**, like the Deck's: "Who Carried? (F8)" plus one line. Only the icon has to be ours.

## Where it goes

The top bar's right-hand group is a single right-aligned `HBoxContainer` (in `scenes/ui/top_bar.tscn`: save
indicator, padding, run timer, Map, Deck, Settings; separation 0; it grows leftwards). The button is added to that
container **just before the Map button**, found as `NTopBar.Map`'s parent, so no node names or coordinates are hardcoded.
The container lays it out, the three game buttons keep their places, and anything the game does to the bar (hiding it,
sliding it away) moves the button too. Another mod adding its own button there doesn't clash: each just takes a slot.

It's added in a Harmony postfix on `NTopBar.Initialize(IRunState)`, which runs once per run, in solo and co-op, on each
player's own bar. Adding it twice does nothing (the button has a fixed node name and the postfix checks for it).

## The button

- An 80×80 slot like the game's buttons. The icon sits in a 72×64 box at the slot's y 8–72, the same box the Map icon
  uses, drawn keep-aspect and centred.
- **Hover:** the icon scales to 1.1 about its centre and brightens (modulate 1.1), like the game's buttons at hover.
  Leaving eases back over about a second. No rotation.
- **Press:** the icon darkens while the mouse is held (the game's buttons dim the same way). **Release over the button**
  opens the recap (`RecapUi.Show`), clears the hover state and removes the tooltip.
- **Tooltip:** the game's `NHoverTipSet`, placed like the Deck's (under the button, right edges aligned). Title
  "Who Carried? (F8)" (the game writes "Deck (D)"), description "View everyone's damage, defense and awards for this
  run." The game's `HoverTip` takes its title from a localization key, and ours isn't in the game's tables, so the tip
  is built from a harmless game key and its `Title`, `Description` and `Id` are then set to ours by reflection.
  **Fallback:** if that fails, a small label in the mod's own style shows the same text in the same place.
- **Keyboard and controller:** the button takes no focus (`FocusMode.None`), so it can't steal the game's controller
  navigation. Controller users still have no way in (F8 is keyboard-only too); focus support is a possible follow-up.

## The icon

`src/WhoCarried/UI/Art/TopBarIcon.svg`, embedded in the DLL. Its canvas is 72×64 units, one unit per screen pixel at
1080p. The visible podium runs from y 1.3 to 62.2, which puts it at y 9.3–70.2 in the slot. The game's icons measure
8.0–71.4 (Map), 9.1–70.3 (Deck) and 9.7–70.3 (Settings); the first draft ran 7.1–74.1 and sat visibly low.

**Why the game's icons look crunchy.** They're painted at about 114 px with nearly hard edges, and the UI atlas they
live in has **no mipmaps**, so when the GPU shrinks them to about 72 px each screen pixel is one bilinear sample and the
pixels in between are skipped. Smooth vector art drawn straight at 72 px looks too clean next to them.

**How the podium gets the same look:**

1. The game's SVG renderer (ThorVG, `Image.LoadSvgFromString`) draws the SVG at 6.5× (468×416 px).
2. Each 4×4 block becomes one pixel of the **most common colour in the block** (`Core/HardPixels.Majority`). The result
   is 117×104 px (1.625×, the Map's art is 114×104) with hard edges and only colours the drawing really uses. Fully
   transparent pixels count as one colour whatever RGB they carry. Rejected: snapping each pixel to the nearest palette
   colour, because a gold–outline blend came out closest to red and left red specks on the crown.
3. That image becomes an `ImageTexture` **without mipmaps**, shown in the 72×64 box, so the game's own filtering shrinks
   it exactly as it shrinks the Deck. On 1440p and 4K screens it's scaled the same way as the game's icons.

The texture is built once and reused. If the SVG can't be drawn, the button isn't added and the log says why (F8 still
works).

## Testing

- **Unit tests** (`HardPixelsTests`): the majority rule; no colour appears that isn't in its block; transparent
  pixels with different RGB vote together; sizes that aren't a multiple of the block size; ties; and a guard that the
  SVG's `viewBox` and the render maths (6.5× divides into whole 4×4 blocks, the result fits the 72×64 box at one pixel
  per unit) stay in step with `Core/TopBarIconArt`.
- **Dev preview:** after its existing screenshots, the preview loads the game's real `top_bar.tscn` at the top of the
  screen, adds the button through the same code the game path uses, and saves `preview-10-topbar.png` (at rest) and
  `preview-11-topbar-hover.png` (hovered, with the tooltip). The icon's measured top and bottom are compared with the
  Map and Deck icons in that screenshot. The bar isn't initialised for a run there, so its labels show placeholders.
- **In game (the owner):** start any run, check the podium sits in line with Map and Deck, hover it for the tooltip,
  click it to open the recap.
