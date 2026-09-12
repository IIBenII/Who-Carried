# Run Recap — visual redesign + PNG export

- **Date:** 2026-09-10
- **Status:** approved in chat (style "A · Game-native", export included in this round)
- **Builds on:** `2026-09-10-run-recap-design.md`

## Problem

In-game, the first build looks like Godot's default theme. It uses the engine's generic font rather than the game's, flat bars with no track, grey boxed tabs, and a fixed 960×620 box that is mostly empty. There's also a bug: in solo play the player name shows as "1".

## Look (game-native)

- **Fonts:** the game's own **Kreon Bold / Regular**, loaded from `res://themes/kreon_{bold,regular}_shared.tres`, falling back to `res://fonts/kreon_*.ttf`, then to the engine default.
- **Palette** (from the game's `StsColors`):

  | Role | Color |
  |---|---|
  | Text | cream `#FFF6E2` |
  | Titles, active tab | gold `#EFC851` |
  | Secondary text | muted `#A89C88` |
  | Panel | `#1C1714` |
  | Border | bronze `#6E5A36` |
  | Bar track | `#2C2420` |
  | Inset | `#151110` |
  | Taken / blocked / healed | red `#FF6B5E`, blue `#87CEEB`, green `#8FD46A` |

- **Frame:** a full-screen 62 % black backdrop (clicking it closes the panel), and a centered 1000 px-wide panel with 14 px radius, 2 px bronze border, and soft shadow. Its height fits the content, with tabs at least 440 px.
- **Header:** "Run recap" in gold Kreon Bold 36 with a dark outline, then the result line (gold on victory, red on defeat, muted in progress), a status hint ("F8 toggles" / "Saved to Pictures\Run Recap"), a **Save image** button, and a **Close** button.
- **Tabs:** text-only tabs in Kreon Bold 19. The active tab is gold with a 3 px gold underline; inactive tabs are muted.
- **Overview:** one row per player: the character icon (46 px), name and character, a rounded bar on a dark track with a lighter top edge, and the value in Kreon Bold 27 with the share %. Below the rows sit three **highlight tiles**: Team damage, Top source, Biggest fight.
- **Sources:** toggle **player chips** (icon + label, bordered in the player's color) replace the dropdown. Each row shows a kind tag (CARD / POWER / RELIC / ORB / POTION / PET), the source name, a slim bar, and the value.
- **Timeline:** a legend with icon, name, and color swatch, then a darker inset chart with "nice" axis gridlines (0/25/50/75/100 %), faint gold act separators, "Act N" labels, 3.5 px rounded lines, and a dot per fight.
- **Defense:** icon + name, then Damage taken / Blocked / Healed as big Kreon numbers under colored headers.
- **Character icons:** `CharacterModel.IconTexture`, the top-bar icon that modded characters also supply, taken from the run's own players. If none resolves, a colored circle with initials is shown instead.

## PNG export

**Save image** renders a separate **summary card**, 1200 px wide, offscreen in a `SubViewport`, and saves it to `%USERPROFILE%\Pictures\Run Recap\run-YYYY-MM-DD_HHmm-<victory|defeat|in-progress>.png`. The card contains:

- the header and date
- the overview rows
- the highlight tiles
- "Top sources" (the top 3 per player, in columns)
- "Damage per fight" (the chart)
- "Defense"
- a small footer

The status label reports whether the save worked. A failure is logged and never thrown.

## Name fix

In solo play (`NetService.Platform == None` with one player), the name comes from Steam via `PlatformUtil.GetPlayerNameRaw(Steam, GetLocalPlayerId(Steam))`. The fallback is "You", or "Player N" in co-op. Names use the *Raw* (unescaped) variant because labels aren't BBCode.

## Core additions (unit-tested)

- `PlayerInfo.CharacterId` (optional, e.g. "IRONCLAD").
- `IconKey` on `BarRow`, `SourcesView`, `TimelineSeries`, and `DefenseRow`, plus a color on `SourcesView`.
- `RecapView` gains `Highlights` and `Victory`.
- `Highlight(Label, Value, Sub)` has three fixed entries:
  - Team damage (value = sum over real players; sub = "N fights")
  - Top source (value = source label; sub = "amount · player")
  - Biggest fight (value = fight total over real players; sub = "encounter · Act N")

  Each shows "—" when there's no data.
- `ChartMath.NiceCeiling(int)` gives the smallest 1 / 2 / 2.5 / 5 × 10ⁿ that is ≥ the value.

## Developer preview (visual verification without a play session)

If `<mod>\data\preview.flag` exists, the mod does the following 10 s after start-up:

1. opens the panel with deterministic sample data (the first three vanilla characters)
2. saves a full-screen screenshot of each tab to `data\preview-N-<tab>.png`
3. exports the summary card to `data\preview-5-export.png`
4. closes the panel

It is inert without the flag file. Claude creates the flag for review loops and deletes it afterwards.

## Units

| File | Responsibility |
|---|---|
| `UI/RecapTheme.cs` | Palette, fonts, stylebox and control factories (text, box, pill, icon, button, chip, tabs) |
| `UI/RecapWidgets.cs` | Data → controls shared by panel and card (player row, source row, bar, highlights, legend, chart, defense grid) |
| `UI/RecapPanel.cs` | Interactive panel (backdrop, header, tabs, chips); returns a `PanelHandle` |
| `UI/SummaryCard.cs` | Export layout |
| `UI/PngExporter.cs` | Offscreen render + save |
| `UI/Later.cs` | Delayed main-thread callbacks (timer-based) |
| `UI/DevPreview.cs` | The flag-file preview |
| `UI/RecapUi.cs` | Orchestration: F8, show/hide, game-over auto-open, export |

## Out of scope

Hover tooltips, animations, a settings UI, clipboard copy, and Run History.
