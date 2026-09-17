# The recap's controls — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the reward screen's stone out of the recap: the export becomes a stone the mod draws itself, sitting on the tab row's baseline and clear of the podium column; the hotkey becomes a key cap in the top bar; Close becomes a word wearing the game's cancel key.

**Architecture:** `Core/HewnStoneArt` draws a chamfered, nine-sliceable stone as RGBA bytes — plain pixel maths, no engine types, so the existing test runner covers it. `UI/HewnStone` turns those bytes into one `ImageTexture` and the style boxes, slab, key cap and press behaviour built from it. `RecapPanel` places the three controls; `HotkeyRebind` drives the cap's states instead of a button's label; `PadHints` gains the inverse of `Glyph` so a keyboard cap hides when a controller appears.

**Tech Stack:** C# / .NET 9, Godot 4.5.1 mono, Harmony 2.4.2, `System.Text.Json` source generation (no runtime reflection).

Spec: [`docs/design/specs/2026-09-17-recap-controls-design.md`](../specs/2026-09-17-recap-controls-design.md)

## Global Constraints

- **No new dependencies.** Not BaseLib, not RitsuLib, no NuGet package. The manifest's dependency list stays empty.
- **No new `GameCompat` entries.** `GameCompat.Hotkey` and `GameCompat.Confirm` already exist and are the only game-input readers this work may use.
- **`Core/` has no Godot or game types.** `Godot.Color`, `Key`, `Image` and friends may only appear under `UI/`.
- **Nothing here may take the recap down.** Every entry point stays wrapped; a failure logs one line through `Tracker.LogError` and leaves the recap working.
- **No absolute paths in tracked files.** `GameDir` lives only in untracked `local.props`.
- **Build with the x64 SDK.** An x86 `dotnet.exe` shadows it on PATH — always call `C:\Program Files\dotnet\dotnet.exe` by full path.
- **Never push.** Commit on `feature/rebindable-hotkey`; the owner decides when anything leaves the machine.
- **Don't deploy or launch the game without asking.** The game locks the DLL, and the owner may be playing.
- **Design pixels, not screen pixels.** Positions and sizes in this plan are in the panel's 1600×900 design space and go through `k.U`/`k.V`/`k.F`. The one exception is `TextureMargin*`, which is in texture pixels and must not be scaled.

**Branch:** `feature/rebindable-hotkey`, on top of `2dc9fa0`.

**Commands** (run from the repo root):

| What | Command |
|---|---|
| Build | `& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release` |
| All tests | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests` |
| One test class | `& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests HewnStone` |

The runner takes an optional name filter as its first argument. Tests are `public static` methods marked `[Test]`, asserted with `Check.Equal(expected, actual, label)` and `Check.True(condition, label)`. Copy the shape of [`HardPixelsTests.cs`](../../../tests/WhoCarried.Tests/HardPixelsTests.cs).

**The suite is 162 tests before this work.** Task 1 adds 5, so it is 167 from then on.

## File structure

| File | Responsibility |
|---|---|
| `src/WhoCarried/Core/HewnStoneArt.cs` | **New.** The stone as pixels: chamfer, rim, bevel, face, and the nine-slice margin. Pure maths, unit-tested. |
| `src/WhoCarried/UI/HewnStone.cs` | **New.** The texture and everything built from it: the slab, its shadow and lift, the key cap and its three looks, the plain word button. |
| `src/WhoCarried/UI/RecapPanel.cs` | Places the three controls; `PanelHandle` carries the hotkey line; the bar's row stops short of Close. |
| `src/WhoCarried/UI/HotkeyRebind.cs` | Starts from the cap and drives the line's three states. |
| `src/WhoCarried/UI/PadHints.cs` | `MouseOnly(Control)` — the inverse of `Glyph`. |
| `src/WhoCarried/UI/RecapUi.cs` | `HotkeyName` removed. |
| `src/WhoCarried/UI/GameArt.cs` | `ActionButton`, `EventButton` and `Share` removed. |
| `tests/WhoCarried.Tests/HewnStoneArtTests.cs` | **New.** |
| `README.md`, `docs/README.md` | The hotkey line's new wording; the two spec/plan rows that are missing from the table. |

Seven tasks. Tasks 1 and 2 build the material; 3, 4 and 5 place one control each and each leave the recap working; 6 removes what is now dead; 7 verifies in game.

**Where tests can and cannot go:** only Task 1 has unit tests, because only `Core/HewnStoneArt` is free of Godot. Tasks 2–6 end with a build and the full suite staying green, and Task 7 is where the UI is actually looked at. Do not fake unit tests for the UI tasks by mocking Godot — the repo does not do that anywhere.

---

### Task 1: The stone's pixels

`Core/HewnStoneArt` and its tests. Nothing draws it yet; this task is the shape and its rules, proved by unit tests.

**Files:**
- Create: `src/WhoCarried/Core/HewnStoneArt.cs`
- Test: `tests/WhoCarried.Tests/HewnStoneArtTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `WhoCarried.Core.HewnStoneArt.Rgba(byte R, byte G, byte B, byte A)`, a readonly record struct, with `Rgba.Clear`.
  - `HewnStoneArt.Palette(Rgba Rim, Rgba RimLit, Rgba RimShade, Rgba Face, Rgba FaceLit, Rgba FaceShade)`, with `Palette.Default`.
  - `HewnStoneArt.Result(byte[] Pixels, int Width, int Height, int Margin)`.
  - `const int Chamfer = 6, Rim = 2, Margin = 10, Side = 22`.
  - `static Result? Draw(Palette palette)` and `static Result? Draw(Palette palette, int side)` — null when `side` is too small to hold two corners, never throwing.

- [ ] **Step 1: Write the failing tests**

Create `tests/WhoCarried.Tests/HewnStoneArtTests.cs`:

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The recap's own button stone: a chamfered, nine-sliceable tile drawn in whole pixels.</summary>
public static class HewnStoneArtTests
{
    private static HewnStoneArt.Rgba At(HewnStoneArt.Result tile, int x, int y)
    {
        int i = (y * tile.Width + x) * 4;
        return new HewnStoneArt.Rgba(tile.Pixels[i], tile.Pixels[i + 1], tile.Pixels[i + 2], tile.Pixels[i + 3]);
    }

    private static HewnStoneArt.Result Tile()
    {
        HewnStoneArt.Result? drawn = HewnStoneArt.Draw(HewnStoneArt.Palette.Default);
        Check.True(drawn != null, "the default tile draws");
        return drawn!.Value;
    }

    [Test]
    public static void TheTileIsSquareAndLeavesAMiddleToStretch()
    {
        HewnStoneArt.Result tile = Tile();

        Check.Equal(tile.Width, tile.Height, "square");
        Check.True(tile.Margin * 2 < tile.Width, "the nine-slice keeps a middle to stretch");
        Check.Equal(tile.Width * tile.Height * 4, tile.Pixels.Length, "four bytes a pixel");
    }

    [Test]
    public static void TheCornersAreCutAway()
    {
        HewnStoneArt.Result tile = Tile();
        int last = tile.Width - 1;

        foreach ((int x, int y) in new[] { (0, 0), (last, 0), (0, last), (last, last) })
            Check.Equal(HewnStoneArt.Rgba.Clear, At(tile, x, y), $"the corner at {x},{y} is cut");
    }

    [Test]
    public static void TheRimIsLitOnTopAndShadedUnderneath()
    {
        HewnStoneArt.Result tile = Tile();
        int middle = tile.Width / 2, last = tile.Width - 1;

        Check.Equal(HewnStoneArt.Palette.Default.RimLit, At(tile, middle, 0), "the top edge catches the light");
        Check.Equal(HewnStoneArt.Palette.Default.RimShade, At(tile, middle, last), "the bottom edge is in shadow");
    }

    [Test]
    public static void TheFaceIsBevelledInsideTheRim()
    {
        HewnStoneArt.Result tile = Tile();
        int middle = tile.Width / 2, last = tile.Width - 1;

        Check.Equal(HewnStoneArt.Palette.Default.FaceLit, At(tile, middle, HewnStoneArt.Rim), "just under the top rim");
        Check.Equal(HewnStoneArt.Palette.Default.FaceShade, At(tile, middle, last - HewnStoneArt.Rim), "just above the bottom rim");
        Check.Equal(HewnStoneArt.Palette.Default.Face, At(tile, middle, middle), "the face itself");
    }

    [Test]
    public static void ATileTooSmallForItsCornersDrawsNothing()
    {
        Check.True(HewnStoneArt.Draw(HewnStoneArt.Palette.Default, 8) == null, "8px can't hold two 10px corners");
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests HewnStone
```

Expected: a build error — `HewnStoneArt` does not exist in `WhoCarried.Core`.

- [ ] **Step 3: Write `Core/HewnStoneArt.cs`**

```csharp
namespace WhoCarried.Core;

/// <summary>
/// The recap's own button stone, drawn rather than loaded from the game: a slab with its corners chamfered away, a
/// bronze rim lit along the top and shadowed along the bottom, and a bevel just inside it. Whole pixels only, so the
/// edges step like the game's hand-drawn art (the same reason <see cref="HardPixels"/> exists).
///
/// The tile is square and nine-sliceable: the <see cref="Result.Margin"/> corners hold their shape while the middle
/// stretches, so one texture dresses a button at any width. That is the defect this replaces — the old stone scaled
/// whole, so the same art came out a different shape on a wide button than on a narrow one.
/// </summary>
public static class HewnStoneArt
{
    public readonly record struct Rgba(byte R, byte G, byte B, byte A)
    {
        public static readonly Rgba Clear = new(0, 0, 0, 0);
    }

    public readonly record struct Palette(Rgba Rim, Rgba RimLit, Rgba RimShade, Rgba Face, Rgba FaceLit, Rgba FaceShade)
    {
        /// <summary>Bronze drawn from the recap's gold, over the table's own dark.</summary>
        public static readonly Palette Default = new(
            Rim: new Rgba(0xB9, 0x8F, 0x3F, 0xFF),
            RimLit: new Rgba(0xE8, 0xC2, 0x68, 0xFF),
            RimShade: new Rgba(0x6E, 0x53, 0x22, 0xFF),
            Face: new Rgba(0x16, 0x23, 0x2F, 0xFF),
            FaceLit: new Rgba(0x24, 0x36, 0x47, 0xFF),
            FaceShade: new Rgba(0x0C, 0x14, 0x1D, 0xFF));
    }

    public readonly record struct Result(byte[] Pixels, int Width, int Height, int Margin);

    /// <summary>How far the corner is cut back, in pixels, measured along each edge.</summary>
    public const int Chamfer = 6;

    /// <summary>The bronze edge's thickness.</summary>
    public const int Rim = 2;

    /// <summary>The nine-slice margin: the corner block that must keep its shape.</summary>
    public const int Margin = Chamfer + Rim + 2;

    /// <summary>The tile's side: two corners, plus two pixels in the middle for the stretch to work from.</summary>
    public const int Side = 2 * Margin + 2;

    public static Result? Draw(Palette palette) => Draw(palette, Side);

    /// <summary>The tile, or null if <paramref name="side"/> is too small to hold two corners. Never throws.</summary>
    public static Result? Draw(Palette palette, int side)
    {
        if (side < 2 * Margin + 1) return null;
        var pixels = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                Rgba colour = ColourAt(x, y, side, palette);
                int i = (y * side + x) * 4;
                pixels[i] = colour.R;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.B;
                pixels[i + 3] = colour.A;
            }
        return new Result(pixels, side, side, Margin);
    }

    /// <summary>
    /// How deep a pixel sits decides what it is: outside the chamfer it's nothing, the outermost ring is the lit or
    /// shadowed rim, the rest of the rim is bronze, the ring under it is the bevel, and everything deeper is the face.
    /// </summary>
    private static Rgba ColourAt(int x, int y, int side, Palette p)
    {
        int left = x, right = side - 1 - x, top = y, bottom = side - 1 - y;
        int edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        int corner = Math.Min(Math.Min(left + top, right + top), Math.Min(left + bottom, right + bottom)) - Chamfer;
        int depth = Math.Min(edge, corner);
        bool lit = top < bottom;

        if (depth < 0) return Rgba.Clear;
        if (depth == 0) return lit ? p.RimLit : p.RimShade;
        if (depth < Rim) return p.Rim;
        if (depth == Rim) return lit ? p.FaceLit : p.FaceShade;
        return p.Face;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests HewnStone
```

Expected: 5 passed. Then the whole suite:

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 167 passed (162 before this change, plus 5).

- [ ] **Step 5: Commit**

```
git add src/WhoCarried/Core/HewnStoneArt.cs tests/WhoCarried.Tests/HewnStoneArtTests.cs
git commit -m "Draw the recap's own button stone"
```

---

### Task 2: The stone as a control

`UI/HewnStone`: one texture, the style boxes made from it, and the three things the panel builds — a slab, a key cap, and a plain word. Nothing uses them yet, so this task ends at a clean build.

**Files:**
- Create: `src/WhoCarried/UI/HewnStone.cs`

**Interfaces:**
- Consumes: `HewnStoneArt.Draw`, `HewnStoneArt.Margin`, `HewnStoneArt.Palette.Default` from Task 1.
- Produces:
  - `HewnStone.Slab(Kit k, string text, float height)` → `Button`, sized to its label, `height` design px tall.
  - `HewnStone.Shadow(Kit k, Control control, float drop = 3)` → `Panel` behind a slab.
  - `HewnStone.Lift(Button button, Kit k)` — hover raises the button 2px, press sinks it 2px. The shadow never moves.
  - `HewnStone.Cap(Kit k, string text)` → `Button`, the key cap.
  - `HewnStone.CapLook` — `Bound`, `Listening`, `Unbound`.
  - `HewnStone.Dress(Button cap, Kit k, CapLook look)` — re-dresses a cap.
  - `HewnStone.Word(Kit k, string text)` → `Button`, a painted word with no chrome.

- [ ] **Step 1: Write `UI/HewnStone.cs`**

```csharp
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The recap's controls. The one action worth pressing gets a stone this mod draws (<see cref="HewnStoneArt"/>),
/// nine-sliced so it holds its shape at any width; the keys the bar names get caps; everything else is a painted word.
/// Nothing here loads a texture from the game, so nothing here can break when the game moves its art.
/// </summary>
internal static class HewnStone
{
    /// <summary>The slab's height in design pixels: centred on the tab band, resting on HandLayout.TabLine.</summary>
    public const float SlabHeight = 42;

    private static ImageTexture? _texture;
    private static bool _drawn;

    /// <summary>The stone, drawn once. Null if it couldn't be built — callers fall back to a plain box.</summary>
    public static ImageTexture? Texture
    {
        get
        {
            // The game disposes textures it unloads; a dead handle has to be redrawn like GameArt re-looks-up its own.
            if (_drawn && (_texture == null || GodotObject.IsInstanceValid(_texture))) return _texture;
            _drawn = true;
            try
            {
                _texture = HewnStoneArt.Draw(HewnStoneArt.Palette.Default) is HewnStoneArt.Result tile
                    ? ImageTexture.CreateFromImage(Image.CreateFromData(tile.Width, tile.Height, false, Image.Format.Rgba8, tile.Pixels))
                    : null;
            }
            catch (Exception e)
            {
                _texture = null;
                Tracker.LogError("drawing the button stone (a plain box instead)", e);
            }
            return _texture;
        }
    }

    /// <summary>
    /// The stone as a style box. TextureMargin is in texture pixels and must not be scaled — that is the whole point
    /// of a nine-slice, and dropping it is what made the old buttons stretch. ContentMargin is on screen, so it is.
    /// </summary>
    private static StyleBox Box(Kit k, Color tint)
    {
        if (Texture is Texture2D stone)
            return new StyleBoxTexture
            {
                Texture = stone,
                ModulateColor = tint,
                TextureMarginLeft = HewnStoneArt.Margin,
                TextureMarginRight = HewnStoneArt.Margin,
                TextureMarginTop = HewnStoneArt.Margin,
                TextureMarginBottom = HewnStoneArt.Margin,
                ContentMarginLeft = k.U(18),
                ContentMarginRight = k.U(18),
                ContentMarginTop = k.U(6),
                ContentMarginBottom = k.U(8),
            };
        return RecapTheme.Box(RecapTheme.Table, k.U(4), RecapTheme.Gold, k.U(2), k.U(18), k.U(7));
    }

    /// <summary>The panel's one real button: a stone that lights on hover and sinks when pressed.</summary>
    public static Button Slab(Kit k, string text, float height)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            ExpandIcon = false,
            // Nothing is in the icon slot in mouse mode; PadHints puts the controller glyph there. Pinning its width
            // is what stops it inflating the button's height, which is the defect the old Save image button had.
            IconMaxWidth = k.F(26),
            CustomMinimumSize = new Vector2(0, k.U(height)),
        };
        if (RecapTheme.Bold is Font font) button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", k.F(19));
        button.AddThemeConstantOverride("h_separation", k.F(11));
        button.AddThemeConstantOverride("outline_size", k.F(3));
        button.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        button.AddThemeColorOverride("font_color", RecapTheme.Text);
        button.AddThemeColorOverride("font_hover_color", new Color("fff7e6"));
        button.AddThemeColorOverride("font_pressed_color", RecapTheme.Muted);
        button.AddThemeColorOverride("font_hover_pressed_color", RecapTheme.Text);
        button.AddThemeStyleboxOverride("normal", Box(k, new Color(1, 1, 1)));
        button.AddThemeStyleboxOverride("hover", Box(k, new Color(1.12f, 1.12f, 1.12f)));
        button.AddThemeStyleboxOverride("pressed", Box(k, new Color(0.78f, 0.78f, 0.78f)));
        button.AddThemeStyleboxOverride("hover_pressed", Box(k, new Color(0.9f, 0.9f, 0.9f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Size = new Vector2(button.GetCombinedMinimumSize().X, k.U(height));
        return button;
    }

    /// <summary>The stone's shadow: the same silhouette behind it, so it rests on the panel instead of floating.</summary>
    public static Panel Shadow(Kit k, Control control, float drop = 3)
    {
        var shadow = new Panel
        {
            Position = control.Position + k.V(0, drop),
            Size = control.Size,
            CustomMinimumSize = control.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shadow.AddThemeStyleboxOverride("panel", Box(k, new Color(0.08f, 0.1f, 0.13f, 0.75f)));
        return shadow;
    }

    /// <summary>
    /// Hover lifts the stone off its shadow, press drops it into it. The shadow stays where it is — that is what makes
    /// the movement read as the stone moving rather than the whole control sliding.
    /// </summary>
    public static void Lift(Button button, Kit k)
    {
        Vector2 rest = button.Position;
        bool down = false;
        void Place(float y) => button.Position = rest + k.V(0, y);
        button.MouseEntered += () => Place(down ? 2 : -2);
        button.MouseExited += () => { down = false; Place(0); };
        button.ButtonDown += () => { down = true; Place(2); };
        button.ButtonUp += () => { down = false; Place(-2); };
    }

    public enum CapLook { Bound, Listening, Unbound }

    /// <summary>A key cap: how the bar names a key. Dark face, ink edge, a heavy bottom lip.</summary>
    public static Button Cap(Kit k, string text)
    {
        var cap = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = k.V(38, 30) };
        if (RecapTheme.Bold is Font font) cap.AddThemeFontOverride("font", font);
        cap.AddThemeFontSizeOverride("font_size", k.F(16));
        cap.AddThemeConstantOverride("outline_size", k.F(2));
        cap.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        Dress(cap, k, CapLook.Bound);
        return cap;
    }

    /// <summary>Re-dresses a cap: a key on it, an empty socket waiting for one, or an empty socket with none.</summary>
    public static void Dress(Button cap, Kit k, CapLook look)
    {
        Color face = look == CapLook.Bound ? new Color("1f2d3a") : new Color("121b25");
        Color edge = look == CapLook.Listening ? RecapTheme.Gold : RecapTheme.Ink;
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            StyleBoxFlat box = RecapTheme.Box(face, k.U(4), edge, k.U(2), k.U(8), k.U(3));
            box.BorderWidthBottom = Math.Max(2, k.F(4));
            cap.AddThemeStyleboxOverride(state, box);
        }
        cap.AddThemeColorOverride("font_color", look == CapLook.Bound ? RecapTheme.Text : RecapTheme.Faint);
        cap.AddThemeColorOverride("font_hover_color", RecapTheme.Gold);
        cap.AddThemeColorOverride("font_pressed_color", RecapTheme.Gold);
    }

    /// <summary>A painted word that does something: the panel's default, used for everything that isn't the export.</summary>
    public static Button Word(Kit k, string text)
    {
        var word = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None };
        if (RecapTheme.Bold is Font font) word.AddThemeFontOverride("font", font);
        word.AddThemeFontSizeOverride("font_size", k.F(19));
        word.AddThemeConstantOverride("outline_size", k.F(3));
        word.AddThemeColorOverride("font_outline_color", RecapTheme.Ink);
        word.AddThemeColorOverride("font_color", RecapTheme.Text);
        word.AddThemeColorOverride("font_hover_color", RecapTheme.Gold);
        word.AddThemeColorOverride("font_pressed_color", RecapTheme.Muted);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            word.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        return word;
    }
}
```

- [ ] **Step 2: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors.

**If `IconMaxWidth` doesn't compile** on this Godot binding, remove that line and instead set `ExpandIcon = true`; the glyph then scales into the button rather than setting its height. Say which you did in the commit message, because Task 3's manual check looks at exactly this.

- [ ] **Step 3: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 167 passed. Nothing should have moved — this task adds no testable logic.

- [ ] **Step 4: Commit**

```
git add src/WhoCarried/UI/HewnStone.cs
git commit -m "Build the recap's controls from the drawn stone"
```

---

### Task 3: The export button

`Save image` becomes `Export as image`, in the new stone, moved out of the corner and onto the tab row's baseline. The old `Change hotkey` button is left alone this task — Task 4 replaces it.

**Files:**
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (the `save` button in `Create`, and `Nav`)

**Interfaces:**
- Consumes: `HewnStone.Slab`, `HewnStone.Shadow`, `HewnStone.Lift`, `HewnStone.SlabHeight` from Task 2.
- Produces: nothing new. `PanelHandle` is unchanged in this task.

- [ ] **Step 1: Build the export as a slab**

In `src/WhoCarried/UI/RecapPanel.cs`, in `Create`, replace the two control lines:

```csharp
        Button hotkey = HewnButton(k, HotkeyButtonText, null, 248, 46, GameArt.ActionButton);
        Button save = HewnButton(k, "Save image", GameArt.Get(GameArt.Share), 170, 46, GameArt.ActionButton);
```

with:

```csharp
        Button hotkey = HewnButton(k, HotkeyButtonText, null, 248, 46, GameArt.ActionButton);
        Button save = HewnStone.Slab(k, "Export as image", HewnStone.SlabHeight);
```

- [ ] **Step 2: Put it on the tab row's line**

In `Nav`, replace the block that positions the two controls and their shadows:

```csharp
        // Match the selected 1600×900 mockup: the nav starts at (40, 88), so these controls begin at (1134, 78) and (1395, 78).
        hotkey.Position = k.V(1094, -10);
        save.Position = k.V(1355, -10);
        Panel hotkeyShadow = HewnShadow(k, hotkey, GameArt.ActionButton);
        Panel saveShadow = HewnShadow(k, save, GameArt.ActionButton);
        nav.AddChild(hotkeyShadow);
        nav.AddChild(saveShadow);
        nav.AddChild(hotkey);
        nav.AddChild(save);
        AnimateHewn(hotkey, hotkeyShadow, k);
        AnimateHewn(save, saveShadow, k);
```

with:

```csharp
        // The tabs run to Decks at x 679 and the stroke under the chosen tab finishes on HandLayout.TabLine. The export
        // starts two tab gutters clear of Decks and its bottom edge lands on that same line, so the row shares a
        // baseline. Nav's own origin is (40, TabsTop), which is what these numbers are relative to.
        save.Position = k.V(699, HandLayout.TabLine - HewnStone.SlabHeight - top);
        hotkey.Position = k.V(1094, -10);
        Panel hotkeyShadow = HewnShadow(k, hotkey, GameArt.ActionButton);
        Panel saveShadow = HewnStone.Shadow(k, save);
        nav.AddChild(hotkeyShadow);
        nav.AddChild(saveShadow);
        nav.AddChild(hotkey);
        nav.AddChild(save);
        AnimateHewn(hotkey, hotkeyShadow, k);
        HewnStone.Lift(save, k);
```

- [ ] **Step 3: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors.

- [ ] **Step 4: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 167 passed.

- [ ] **Step 5: Commit**

```
git add src/WhoCarried/UI/RecapPanel.cs
git commit -m "Cut the export button from our own stone"
```

---

### Task 4: The hotkey becomes a line in the bar

The `Change hotkey` button goes. In its place, a key cap and a line of text in the top bar, after the party. `HotkeyRebind` drives them.

**Files:**
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (`PanelHandle`, `Create`, `TopBar`, `Nav`)
- Modify: `src/WhoCarried/UI/HotkeyRebind.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `HewnStone.Cap`, `HewnStone.Dress`, `HewnStone.CapLook` from Task 2; `HotkeyBinding.Name`, `HotkeyBinding.Set` unchanged.
- Produces:
  - `internal sealed record HotkeyLine(Button Cap, Label Text, Label Hint, Action<HewnStone.CapLook> Look)` in `RecapPanel.cs`.
  - `PanelHandle` takes `HotkeyLine Hotkey` in place of `Button Hotkey`; every other member is unchanged.
  - `RecapPanel.HotkeyButtonText` is deleted.

- [ ] **Step 1: Declare the line and carry it on the handle**

At the top of `src/WhoCarried/UI/RecapPanel.cs`, replace the `PanelHandle` record with:

```csharp
/// <summary>The bar's hotkey readout: the cap with the key on it, the words beside it, and the faint prompt hint.</summary>
internal sealed record HotkeyLine(Button Cap, Label Text, Label Hint, Action<HewnStone.CapLook> Look);

/// <summary>
/// What the caller needs to drive an open recap: switch views, show a status message, push live updates, and what the
/// controller can do (each view's rows, close, save).
/// </summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status, HotkeyLine Hotkey, Live Live, IReadOnlyList<PadTab> Pads,
                                   Action Close, Action Save);
```

- [ ] **Step 2: Build the line in the bar**

In `TopBar`, replace:

```csharp
        HBoxContainer party = k.Row(-8);
        row.AddChild(Kit.Center(party));
        row.AddChild(Kit.Fill());
        row.AddChild(Kit.Center(status));
```

with:

```csharp
        HBoxContainer party = k.Row(-8);
        row.AddChild(Kit.Center(party));

        // One more of the bar's readouts: it already says floor, time, ascension and damage, so it can say which key
        // opens the thing. The cap is the control — clicking it starts HotkeyRebind listening.
        HBoxContainer keys = k.Row(10);
        Button cap = HewnStone.Cap(k, HotkeyBinding.Name ?? "—");
        Label keyText = k.Text(HotkeyBinding.Name == null ? "no key opens the recap" : "opens the recap", 17, RecapTheme.Faint);
        Label keyHint = k.Caps("", 13, RecapTheme.Faint, 2);
        keys.AddChild(Kit.Center(cap));
        keys.AddChild(Kit.Center(keyText));
        keys.AddChild(Kit.Center(keyHint));
        row.AddChild(Kit.Center(keys));
        hotkey = new HotkeyLine(cap, keyText, keyHint, look => HewnStone.Dress(cap, k, look));
        if (HotkeyBinding.Name == null) hotkey.Look(HewnStone.CapLook.Unbound);

        row.AddChild(Kit.Fill());
        row.AddChild(Kit.Center(status));
```

Change `TopBar`'s signature so it can hand the line back, from:

```csharp
    private static Control TopBar(Kit k, RecapView view, Label status, Action onClose, Live live, float screenWidth,
                                  float stageLeft, PadHints hints)
    {
```

to:

```csharp
    private static Control TopBar(Kit k, RecapView view, Label status, Action onClose, Live live, float screenWidth,
                                  float stageLeft, PadHints hints, out HotkeyLine hotkey)
    {
```

- [ ] **Step 3: Wire it into `Create` and take the old button out of `Nav`**

In `Create`, delete the `hotkey` button line so only the export remains:

```csharp
        Label status = k.Text("", 15, RecapTheme.Faint);
        Button save = HewnStone.Slab(k, "Export as image", HewnStone.SlabHeight);
```

Delete the `HotkeyButtonText` property from the top of the class:

```csharp
    public static string HotkeyButtonText => $"Change hotkey ({HotkeyBinding.Name ?? "unbound"})";
```

The handle is built before `TopBar` runs, so build the bar first and then the handle. Replace:

```csharp
        PadTab[] pads = Views.Select(_ => new PadTab()).ToArray();
        PanelHandle? handle = null;
        void Save() => onSave(handle!);
        handle = new PanelHandle(root, tabs, status, hotkey, live, pads, onClose, Save);
```

with:

```csharp
        PadTab[] pads = Views.Select(_ => new PadTab()).ToArray();
        PanelHandle? handle = null;
        void Save() => onSave(handle!);
        var hints = new PadHints();
        Control bar = TopBar(k, view, status, onClose, live, screen.X, stage.Position.X, hints, out HotkeyLine hotkeyLine);
        handle = new PanelHandle(root, tabs, status, hotkeyLine, live, pads, onClose, Save);
```

and further down, replace the block that used to build the bar and the nav:

```csharp
        var hints = new PadHints();
        save.Pressed += Save;
        if (GameCompat.Confirm is StringName confirm) hints.OnButton(save, confirm);
        root.AddChild(TopBar(k, view, status, onClose, live, screen.X, stage.Position.X, hints));
        stage.AddChild(Nav(k, tabs, hints, hotkey, save));
```

with:

```csharp
        save.Pressed += Save;
        if (GameCompat.Confirm is StringName confirm) hints.OnButton(save, confirm);
        root.AddChild(bar);
        stage.AddChild(Nav(k, tabs, hints, save));
```

In `Nav`, drop the `hotkey` parameter and everything that served it:

```csharp
    private static Control Nav(Kit k, TabContainer tabs, PadHints hints, Button save)
```

and inside it, the positioning block becomes just:

```csharp
        // The tabs run to Decks at x 679 and the stroke under the chosen tab finishes on HandLayout.TabLine. The export
        // starts two tab gutters clear of Decks and its bottom edge lands on that same line, so the row shares a
        // baseline. Nav's own origin is (40, TabsTop), which is what these numbers are relative to.
        save.Position = k.V(699, HandLayout.TabLine - HewnStone.SlabHeight - top);
        Panel saveShadow = HewnStone.Shadow(k, save);
        nav.AddChild(saveShadow);
        nav.AddChild(save);
        HewnStone.Lift(save, k);
```

- [ ] **Step 4: Let `HotkeyRebind` drive the line**

In `src/WhoCarried/UI/HotkeyRebind.cs`, replace `Attach`, `Start`, `Stop` and `SetButton` with:

```csharp
    /// <summary>Attaches the prompt to a newly opened recap.</summary>
    public static void Attach(PanelHandle panel)
    {
        Forget();
        _panel = panel;
        try
        {
            panel.Hotkey.Cap.Pressed += Start;
            panel.Root.GuiInput += OnKey;
            panel.Root.FocusExited += () => Stop(panel);
            panel.Root.TreeExiting += () => Forget(panel);
        }
        catch (Exception e)
        {
            Tracker.LogError("hotkey rebind (the key still works)", e);
            Forget();
        }
    }

    /// <summary>Starts listening for the next key press.</summary>
    public static void Start()
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel.Root)) return;
        _listening = true;
        Listening();
        _panel.Root.FocusMode = Control.FocusModeEnum.All;
        if (_panel.Root.IsInsideTree()) _panel.Root.GrabFocus();
    }

    private static void Stop(PanelHandle? expected = null)
    {
        if (expected != null && !ReferenceEquals(_panel, expected)) return;
        _listening = false;
        Idle();
    }

    /// <summary>The cap empties and the line asks for a key, with the two ways out spelled out beside it.</summary>
    private static void Listening()
    {
        if (!Ready()) return;
        _panel!.Hotkey.Look(HewnStone.CapLook.Listening);
        _panel.Hotkey.Cap.Text = "?";
        _panel.Hotkey.Text.Text = "press a key…";
        _panel.Hotkey.Hint.Text = "esc cancels · del clears";
    }

    /// <summary>The cap carries the key again — or says there isn't one, which the podium button makes survivable.</summary>
    private static void Idle()
    {
        if (!Ready()) return;
        string? name = HotkeyBinding.Name;
        _panel!.Hotkey.Look(name == null ? HewnStone.CapLook.Unbound : HewnStone.CapLook.Bound);
        _panel.Hotkey.Cap.Text = name ?? "—";
        _panel.Hotkey.Text.Text = name == null ? "no key opens the recap" : "opens the recap";
        _panel.Hotkey.Hint.Text = "";
    }

    /// <summary>A panel whose line is still alive to write to.</summary>
    private static bool Ready() => _panel != null && GodotObject.IsInstanceValid(_panel.Hotkey.Cap)
                                                  && GodotObject.IsInstanceValid(_panel.Hotkey.Text);
```

`SetButton` is gone — delete it, and delete the `RecapPanel.HotkeyButtonText` reference it held.

- [ ] **Step 5: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors. If `DevPreview.cs` or `RecapUi.cs` fail on `handle.Hotkey`, they were reading the old button — check what they wanted and read it off `handle.Hotkey.Cap`.

- [ ] **Step 6: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 167 passed.

- [ ] **Step 7: Say it properly in the README**

In `README.md`, replace the hotkey bullet:

```markdown
- **Hotkey:** open the recap and click **Change hotkey (F8)**, then press the key you want. Esc cancels; Delete or Backspace clears it while leaving the podium button available. The setting is saved in `settings.json` in the mod's data folder.
```

with:

```markdown
- **Hotkey:** open the recap and click the key on the top bar — the one reading **F8 opens the recap** — then press the key you want. Esc cancels; Delete or Backspace clears it, leaving the podium button. The setting is saved in `settings.json` in the mod's data folder.
```

- [ ] **Step 8: Commit**

```
git add src/WhoCarried/UI/RecapPanel.cs src/WhoCarried/UI/HotkeyRebind.cs README.md
git commit -m "Move the hotkey into the bar as a key you can press"
```

---

### Task 5: Close, and the status message behind it

Close becomes a word wearing the game's cancel key, and the bar's row stops short of it so the export's status message stops hiding underneath.

**Files:**
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (`TopBar`)
- Modify: `src/WhoCarried/UI/PadHints.cs`

**Interfaces:**
- Consumes: `HewnStone.Word`, `HewnStone.Cap` from Task 2.
- Produces: `PadHints.MouseOnly(Control control)` — hides a control in controller mode, the inverse of `Glyph`.

- [ ] **Step 1: Give `PadHints` the inverse of `Glyph`**

In `src/WhoCarried/UI/PadHints.cs`, add the list beside the other two:

```csharp
    private readonly List<(TextureRect Rect, StringName Action)> _glyphs = new();
    private readonly List<(Button Button, StringName Action, Texture2D? MouseIcon)> _buttons = new();
    private readonly List<Control> _mouseOnly = new();
```

add the method beside `Glyph`:

```csharp
    /// <summary>Hidden in controller mode: the keyboard half of a hint whose other half is a glyph.</summary>
    public void MouseOnly(Control control) => _mouseOnly.Add(control);
```

and one more loop in `Update`, after the buttons loop:

```csharp
            foreach (Control control in _mouseOnly)
                if (GodotObject.IsInstanceValid(control)) control.Visible = !pad;
```

- [ ] **Step 2: Read the key that closes the recap**

`NInputManager` lives in `MegaCrit.Sts2.Core.Nodes.CommonUi`, which `RecapPanel.cs` does not import yet. Add it to the usings at the top, in alphabetical order with the others:

```csharp
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Core;
using WhoCarried.Game;
```

Then add to `RecapPanel`, beside the other private helpers:

```csharp
    /// <summary>
    /// The key the game has bound to cancel — the one that already closes the recap through PadInput. Null when the
    /// binding can't be read, in which case Close says nothing about keys, which is how it behaved before.
    /// </summary>
    private static string? CloseKeyName()
    {
        try
        {
            if (NInputManager.Instance is NInputManager manager && GameCompat.Hotkey(manager, MegaInput.cancel) is Key key && key != Key.None)
                return key.ToString();
        }
        catch (Exception e)
        {
            Tracker.LogError("reading the key that closes the recap", e);
        }
        return null;
    }
```

- [ ] **Step 3: Build Close from a word, a cap and a glyph**

In `TopBar`, replace:

```csharp
        Button close = HewnButton(k, "Close", null, 96, 42, GameArt.EventButton);
        close.Pressed += onClose;
        hints.OnButton(close, MegaInput.cancel);
        close.Position = new Vector2(stageLeft, 0) + k.V(1470, 12);
        bar.AddChild(close);
```

with:

```csharp
        // Close is a word, not a stone: it is the one control nobody hunts for, and the key beside it already does the
        // job. The cap and the glyph are the same hint for two devices — PadHints shows exactly one of them.
        HBoxContainer closing = k.Row(9);
        TextureRect closeGlyph = k.Pic(null, 26, 26);
        closeGlyph.Visible = false;
        hints.Glyph(closeGlyph, MegaInput.cancel);
        closing.AddChild(Kit.Center(closeGlyph));
        if (CloseKeyName() is string closeKey)
        {
            Button closeCap = HewnStone.Cap(k, closeKey);
            closeCap.MouseFilter = Control.MouseFilterEnum.Ignore;
            hints.MouseOnly(closeCap);
            closing.AddChild(Kit.Center(closeCap));
        }
        Button close = HewnStone.Word(k, "Close");
        close.Pressed += onClose;
        closing.AddChild(Kit.Center(close));
        closing.Position = new Vector2(stageLeft, 0) + k.V(1566, 14) - new Vector2(closing.GetCombinedMinimumSize().X, 0);
        bar.AddChild(closing);
```

The cap ignores the mouse so that clicking the key still hits Close, which sits next to it: the cap names the key, it is not a second button.

`GetCombinedMinimumSize()` is being asked before `closing` is in the tree, which is fine for buttons whose font and minimum size are already set — but if the group lands in the wrong place in game, that is why. The fix if so: add it to the bar first and set its position from a `closing.TreeEntered` callback instead.

While you are here, `PadHints`' class comment still says "B on Close, Y on Save image". Close now has a glyph *and* a cap, and the export is no longer called Save image; reword it to match.

- [ ] **Step 4: Stop the status message hiding under Close**

Still in `TopBar`, the row is sized before Close exists, so it runs the full width and the right-aligned status label ends up underneath. Move the row's sizing to after `closing` is placed. Replace:

```csharp
        HBoxContainer row = k.Row(28);
        row.Position = new Vector2(stageLeft + k.U(30), 0);
        row.Size = new Vector2(screenWidth - 2 * (stageLeft + k.U(30)), k.U(68));
        bar.AddChild(row);
```

with:

```csharp
        HBoxContainer row = k.Row(28);
        row.Position = new Vector2(stageLeft + k.U(30), 0);
        row.Size = new Vector2(screenWidth - 2 * (stageLeft + k.U(30)), k.U(68));
        bar.AddChild(row);
        // Sized properly once Close is placed: see the end of this method. The row's last child is the status label,
        // which right-aligns to the row's edge — left at full width it runs under Close, and every export message
        // loses its tail.
```

and immediately after `bar.AddChild(closing);` add:

```csharp
        row.Size = new Vector2(Math.Max(k.U(200), closing.Position.X - k.U(20) - row.Position.X), k.U(68));
```

- [ ] **Step 5: Build**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
```

Expected: build succeeded, 0 errors.

- [ ] **Step 6: Run the whole test suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 167 passed.

- [ ] **Step 7: Commit**

```
git add src/WhoCarried/UI/RecapPanel.cs src/WhoCarried/UI/PadHints.cs
git commit -m "Close wears the key that closes it"
```

---

### Task 6: Take out what nothing reads

Three `GameArt` entries, four `Hewn*` helpers and a dead property, plus the two rows missing from the docs table.

**Files:**
- Modify: `src/WhoCarried/UI/RecapPanel.cs`, `src/WhoCarried/UI/GameArt.cs`, `src/WhoCarried/UI/RecapUi.cs`, `docs/README.md`

**Interfaces:**
- Consumes: everything from Tasks 3–5 being in place.
- Produces: nothing. This task only removes.

- [ ] **Step 1: Check each one really is dead before deleting it**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
git grep -n "HewnButton\|HewnBox\|HewnShadow\|AnimateHewn\|ActionButton\|EventButton\|GameArt.Share\|HotkeyName" -- src tests
```

Expected: hits only in `GameArt.cs` (the declarations) and `RecapPanel.cs` (the four helper definitions), plus `RecapUi.cs:21` for `HotkeyName`. **If anything else still calls one, stop** — a previous task left a caller behind, and that is what to fix instead.

- [ ] **Step 2: Delete the four helpers**

In `src/WhoCarried/UI/RecapPanel.cs`, delete `HewnButton`, `HewnBox`, `HewnShadow` and `AnimateHewn` — the four methods between `Coin` and `Nav`.

- [ ] **Step 3: Delete the three art entries**

In `src/WhoCarried/UI/GameArt.cs`, remove `Share`, `ActionButton` and `EventButton` from the name list on line 18, and their three lines from `Paths`:

```csharp
        [Share] = Stats + "share_stats.png",
        [ActionButton] = "res://images/ui/reward_screen/reward_skip_button.png",
        [EventButton] = "res://images/packed/common_ui/event_button.png",
```

- [ ] **Step 4: Delete the dead property**

In `src/WhoCarried/UI/RecapUi.cs`, remove:

```csharp
    public static string HotkeyName => HotkeyBinding.Name ?? "unbound";
```

- [ ] **Step 5: Put this work in the docs table**

In `docs/README.md`, add two rows to the end of the table:

```markdown
| [Rebindable hotkey](design/specs/2026-09-16-rebindable-hotkey-design.md) | [plan](design/plans/2026-09-16-rebindable-hotkey.md) | Changing the key that opens the recap from inside the game, kept in the mod's own settings file |
| [The recap's controls](design/specs/2026-09-17-recap-controls-design.md) | [plan](design/plans/2026-09-17-recap-controls.md) | The reward screen's stone out, a stone the mod draws in, and the hotkey and Close as keys the bar names |
```

- [ ] **Step 6: Build and run the whole suite**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: build succeeded with 0 errors and 0 warnings; 167 passed.

- [ ] **Step 7: Commit**

```
git add src/WhoCarried/UI/RecapPanel.cs src/WhoCarried/UI/GameArt.cs src/WhoCarried/UI/RecapUi.cs docs/README.md
git commit -m "Drop the reward screen's stone and what held it"
```

---

### Task 7: See it running

Nothing above has been looked at. This task is where it gets looked at, on both supported branches.

**Files:** none.

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: a merged branch, if the owner is happy.

- [ ] **Step 1: Build and run the whole suite one more time**

```
& 'C:\Program Files\dotnet\dotnet.exe' build src/WhoCarried -c Release
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/WhoCarried.Tests
```

Expected: 0 errors, 167 passed.

- [ ] **Step 2: Ask before deploying**

**Do not deploy or launch without asking the owner.** The game locks the DLL and they may be playing. When they say go:

```
powershell -File tools/deploy.ps1
```

Then create `preview.flag` in the data folder (`%APPDATA%\SlayTheSpire2\WhoCarried`) and launch `steam://rungameid/2868840`. About 10 s after load the recap opens with sample data and writes `preview-*.png` there.

- [ ] **Step 3: Check the look**

With the recap open:

1. The export reads **Export as image**, sits just right of **Decks**, and its bottom edge lines up with where the gold stroke under the chosen tab ends.
2. Its corners are chamfered and its end caps are the same shape as each other — the stone is nine-sliced, not stretched.
3. Hover lifts it off its shadow and lights the rim; press sinks it into the shadow. Nothing else on the row moves.
4. The bar reads `F8 opens the recap` after the party, on a key cap.
5. Close is a word at the bar's right end with its key on a cap beside it, and that key really does close the recap.
6. Nothing of the mod's sits under the podium icon. Check this at a wide aspect ratio, not just 16:9 — that is where the collision was.

- [ ] **Step 4: Check the behaviour**

7. Click the cap: it empties, the line reads `press a key…`, and `esc cancels · del clears` appears beside it.
8. Press `F9`: it binds, the cap reads `F9`, `F9` toggles the recap and `F8` does nothing. `settings.json` says `"hotkey": "F9"`.
9. The podium tooltip reads `Who Carried? (F9)` without a restart.
10. Click the cap and press `Esc`: the binding is unchanged and the recap stays open. Hold Esc briefly — still open.
11. Click the cap and press `Delete`: the cap reads `—`, the line reads `no key opens the recap`, no key opens it, the podium button still does.
12. Bind the currently bound key and hold it: the recap stays open until you let go.
13. Export an image: the message appears in full, left of Close and not under it, and clears after about four seconds back to the key line.
14. Restart: the binding survives.

- [ ] **Step 5: Check it with a controller**

15. Pick up a pad with the recap open: Close's cap becomes the cancel glyph and the export shows the confirm glyph, without reopening the panel.
16. The confirm button exports and cancel closes, with the mouse never touching the panel.
17. Put the pad down and move the mouse: the caps come back.
18. The export button's height does not change when the glyph appears — that is the `IconMaxWidth` pin from Task 2, and it is the defect this whole change started from.

- [ ] **Step 6: Check the other game branch**

The mod ships one DLL for both branches and this feature reads no new game internals, so it should behave identically — confirm rather than assume. Repeat steps 3 and 5 on whichever branch wasn't used the first time. Reference DLLs are at `E:\Claude\sts2-refs\v0.107.1` and `E:\Claude\sts2-refs\v0.111.0`; build against either with `-p:GameData=<folder>`.

Set the hotkey back to F8 and close the game fully — the owner asked that any game started here is fully closed.

- [ ] **Step 7: Squash into main**

Only when the owner is happy with what they saw:

```
git checkout main
git merge --squash feature/rebindable-hotkey
git commit
git branch -D feature/rebindable-hotkey
```

Do not push. Pushing is the owner's call.

---

## Notes for whoever implements this

- **The branch already contains the rebindable hotkey.** `Core/Settings`, `UI/HotkeyBinding` and `UI/HotkeyRebind` work and are tested; this plan changes what starts the rebind and what shows its state, and nothing about how it stores or polls a key. If you find yourself editing `HotkeyBinding` or `Settings`, stop and re-read the task.
- **`TextureMargin*` is in texture pixels; `ContentMargin*` is in screen pixels.** Scaling the first with `k.U` breaks the nine-slice, and not scaling the second gives you 18px of padding on a 4K screen. Dropping `TextureMargin*` altogether is exactly the bug this replaces.
- **`PadInput.Attach` can fail.** It sets `_failed` and never subscribes to `GuiInput`. That is why `HotkeyRebind` subscribes to `panel.Root.GuiInput` itself, and why Close's cap has to survive `CloseKeyName()` returning null.
- **The cap is not a second Close button.** It ignores the mouse on purpose; the word next to it is the button.
- **Don't reach for `InputMap` or `NInputManager` beyond `GameCompat.Hotkey`.** The hotkey design rejected coupling to the game's input internals, and nothing here needs it.
- **The old buttons' numbers were mockup numbers.** `1094`, `1355` and `-10` came from a mockup that no longer applies. The new ones come from `HandLayout` and a measurement of the render, and they are written as arithmetic against `HandLayout.TabLine` so they follow the constant if it moves.
