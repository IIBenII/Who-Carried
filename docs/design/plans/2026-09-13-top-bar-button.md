# Top-bar Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A podium button in the game's top bar, just left of the Map button, that opens the recap, with an icon that
looks like the game's own top-bar icons.

**Architecture:** Two pure pieces in `Core` (unit-tested without Godot): `HardPixels.Majority`, which turns a big
smooth render into hard-edged pixels, and `TopBarIconArt`, the icon's geometry. `UI/TopBarButton` builds the texture
from the embedded SVG with the game's SVG renderer, adds the button to the top bar's right-hand `HBoxContainer` from a
Harmony postfix on `NTopBar.Initialize`, and handles hover, press and the game's tooltip. The dev preview loads the
game's real `top_bar.tscn` and screenshots the button at rest and hovered.

**Tech Stack:** C# / .NET 9, Godot 4.5 (GodotSharp), Harmony, the repo's own test runner (`tests/WhoCarried.Tests`).

**Spec:** `docs/design/specs/2026-09-13-top-bar-button-design.md`

## Global Constraints

- Work on `feature/top-bar-button`; squash into `main` when done; never push.
- No absolute paths in tracked files. Build with `C:\Program Files\dotnet\dotnet.exe` (an x86 `dotnet` shadows it on
  PATH); commands below say `dotnet` for short.
- Read-only mod: never change game state. Every patch body is wrapped in `try`/`catch` → `Tracker.LogError`.
- Don't reference other mods' types or ids. Game node names are fine only in dev-preview code.
- The icon is the mod's own SVG (`src/WhoCarried/UI/Art/TopBarIcon.svg`, already committed). No game art in it.
- Icon canvas 72×64 units; rendered at 6.5×, reduced by 4×4 majority blocks to 117×104; texture without mipmaps; shown
  in a 72×64 box at y 8–72 of an 80×80 slot.
- Hover: scale 1.1 about the centre and modulate 1.1, no rotation; ease back over 1 s. Press: modulate 0.4.
- Tooltip title `Who Carried? (F8)`, description `View everyone's damage, defense and awards for this run.`
- Tests: `dotnet run --project tests/WhoCarried.Tests -c Release [-- <filter>]`. Build:
  `dotnet build WhoCarried.sln -c Release --nologo -v q` (0 warnings expected).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WhoCarried/Core/HardPixels.cs` (new) | Majority-of-block downsampling of RGBA bytes. Pure. |
| `src/WhoCarried/Core/TopBarIconArt.cs` (new) | The icon's numbers: canvas, render scale, block, art size, slot box, hover/press values. Pure. |
| `tests/WhoCarried.Tests/HardPixelsTests.cs` (new) | Majority rule, no invented colours, transparent votes, leftovers, ties, bad input. |
| `tests/WhoCarried.Tests/TopBarIconArtTests.cs` (new) | Render maths, and the SVG's canvas and element guard. |
| `src/WhoCarried/WhoCarried.csproj` | Embed the SVG as `WhoCarried.TopBarIcon.svg`. |
| `src/WhoCarried/UI/TopBarButton.cs` (new) | Texture from SVG, the button node, hover/press/click, tooltip with fallback. |
| `src/WhoCarried/UI/RecapUi.cs` | `HotkeyName` constant shared with the tooltip. |
| `src/WhoCarried/Game/Patches.cs` | `TopBarPatch` postfix on `NTopBar.Initialize`. |
| `src/WhoCarried/ModEntry.cs` | Register `TopBarPatch`. |
| `src/WhoCarried/UI/DevPreview.cs` | Top-bar screenshots after the export. |

---

### Task 1: HardPixels.Majority

**Files:**
- Create: `src/WhoCarried/Core/HardPixels.cs`
- Test: `tests/WhoCarried.Tests/HardPixelsTests.cs`

**Interfaces:**
- Produces: `public static HardPixels.Result HardPixels.Majority(byte[] rgba, int width, int height, int block)`;
  `public readonly record struct HardPixels.Result(byte[] Pixels, int Width, int Height)`. Output is
  `width / block` × `height / block` (leftover rows and columns dropped), RGBA8.

- [ ] **Step 1: Write the failing tests**

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>Turning a big smooth render into hard pixels: each block becomes its most common colour.</summary>
public static class HardPixelsTests
{
    private static readonly byte[] Red = { 200, 40, 30, 255 }, Blue = { 30, 60, 200, 255 }, Clear = { 0, 0, 0, 0 };

    /// <summary>An image from rows of pixels, each pixel 4 bytes.</summary>
    private static byte[] Image(params byte[][] pixels) => pixels.SelectMany(p => p).ToArray();

    private static byte[] Pixel(HardPixels.Result r, int x, int y) => r.Pixels.Skip((y * r.Width + x) * 4).Take(4).ToArray();

    [Test]
    public static void TheMostCommonColourInABlockWins()
    {
        HardPixels.Result r = HardPixels.Majority(Image(Red, Blue, Red, Red), 2, 2, 2);
        Check.Equal(1, r.Width, "width");
        Check.Equal(1, r.Height, "height");
        Check.True(Pixel(r, 0, 0).SequenceEqual(Red), "three reds beat one blue");
    }

    [Test]
    public static void NoColourAppearsThatWasntInItsBlock()
    {
        var rng = new Random(11);
        const int w = 24, h = 16, block = 4;
        var rgba = new byte[w * h * 4];
        rng.NextBytes(rgba);
        HardPixels.Result r = HardPixels.Majority(rgba, w, h, block);
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                byte[] got = Pixel(r, x, y);
                bool found = false;
                for (int dy = 0; dy < block && !found; dy++)
                    for (int dx = 0; dx < block && !found; dx++)
                    {
                        int i = ((y * block + dy) * w + x * block + dx) * 4;
                        byte[] source = rgba[i + 3] == 0 ? Clear : rgba[i..(i + 4)];
                        found = source.SequenceEqual(got);
                    }
                Check.True(found, $"pixel {x},{y} came from its own block");
            }
    }

    [Test]
    public static void SeeThroughPixelsVoteTogetherWhateverColourTheyCarry()
    {
        // Renderers leave junk RGB in fully transparent pixels. Counted apart, the blue would win this tie on order.
        byte[] junk1 = { 9, 9, 9, 0 }, junk2 = { 200, 0, 0, 0 }, junk3 = { 0, 0, 77, 0 };
        HardPixels.Result r = HardPixels.Majority(Image(Blue, junk1, junk2, junk3), 2, 2, 2);
        Check.True(Pixel(r, 0, 0).SequenceEqual(Clear), "three see-through pixels win, as plain transparent");
    }

    [Test]
    public static void LeftoverRowsAndColumnsAreDropped()
    {
        var rgba = new byte[5 * 7 * 4];
        HardPixels.Result r = HardPixels.Majority(rgba, 5, 7, 2);
        Check.Equal(2, r.Width, "width");
        Check.Equal(3, r.Height, "height");
        Check.Equal(2 * 3 * 4, r.Pixels.Length, "bytes");
    }

    [Test]
    public static void ATieGoesToTheColourThatReachedTheTopCountFirst()
    {
        // Reading order red, blue, blue, red: blue gets to two first.
        HardPixels.Result r = HardPixels.Majority(Image(Red, Blue, Blue, Red), 2, 2, 2);
        Check.True(Pixel(r, 0, 0).SequenceEqual(Blue), "blue");
    }

    [Test]
    public static void AnImageOfTheWrongSizeIsRefused()
    {
        bool threw = false;
        try { HardPixels.Majority(new byte[10], 2, 2, 2); }
        catch (ArgumentException) { threw = true; }
        Check.True(threw, "10 bytes isn't a 2x2 RGBA image");
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- HardPixels`
Expected: build error, `HardPixels` doesn't exist.

- [ ] **Step 3: Implement**

```csharp
namespace WhoCarried.Core;

/// <summary>
/// Hard-edged pixel art from a big smooth render: each block of pixels becomes the one colour that covers most of it.
/// Unlike averaging, it never blends two colours into a new one, so edges come out stepped like hand-placed pixels.
/// </summary>
public static class HardPixels
{
    public readonly record struct Result(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// Shrinks <paramref name="rgba"/> (RGBA8, <paramref name="width"/>×<paramref name="height"/>) by
    /// <paramref name="block"/> in each direction; rows and columns that don't fill a whole block are dropped. Fully
    /// transparent pixels count as one colour whatever RGB they carry. On a tie, the colour that reached the top count
    /// first in reading order wins.
    /// </summary>
    public static Result Majority(byte[] rgba, int width, int height, int block)
    {
        if (width <= 0 || height <= 0 || block <= 0 || rgba.Length != width * height * 4)
            throw new ArgumentException($"expected a {width}x{height} RGBA image ({width * height * 4} bytes), got {rgba.Length} bytes");
        int w = width / block, h = height / block;
        var pixels = new byte[w * h * 4];
        var counts = new Dictionary<uint, int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                counts.Clear();
                uint best = 0;
                int bestCount = 0;
                for (int dy = 0; dy < block; dy++)
                    for (int dx = 0; dx < block; dx++)
                    {
                        int i = ((y * block + dy) * width + x * block + dx) * 4;
                        uint colour = rgba[i + 3] == 0 ? 0u : (uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]);
                        int n = counts.TryGetValue(colour, out int seen) ? seen + 1 : 1;
                        counts[colour] = n;
                        if (n > bestCount)
                        {
                            bestCount = n;
                            best = colour;
                        }
                    }
                int o = (y * w + x) * 4;
                pixels[o] = (byte)(best >> 24);
                pixels[o + 1] = (byte)(best >> 16);
                pixels[o + 2] = (byte)(best >> 8);
                pixels[o + 3] = (byte)best;
            }
        return new Result(pixels, w, h);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- HardPixels`
Expected: `6/6 passed`. Then the whole suite: `dotnet run --project tests/WhoCarried.Tests -c Release`, all pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/HardPixels.cs tests/WhoCarried.Tests/HardPixelsTests.cs
git commit -m "Add hard-pixel downsampling for the top-bar icon"
```

---

### Task 2: TopBarIconArt and the embedded SVG

**Files:**
- Create: `src/WhoCarried/Core/TopBarIconArt.cs`
- Modify: `src/WhoCarried/WhoCarried.csproj` (embed the SVG)
- Test: `tests/WhoCarried.Tests/TopBarIconArtTests.cs`

**Interfaces:**
- Produces (all `public`, in `WhoCarried.Core.TopBarIconArt`): `const int Width = 72, Height = 64, Block = 4`;
  `const float ArtScale = 1.625f, RenderScale = 6.5f`; `static int RenderWidth, RenderHeight, ArtWidth, ArtHeight`
  (468, 416, 117, 104); `const float Slot = 80f, BoxTop = 8f`; `const float HoverGrow = 1.1f, HoverBright = 1.1f,
  PressDim = 0.4f, SettleSeconds = 1f`; `const string SvgResource = "WhoCarried.TopBarIcon.svg"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Xml.Linq;
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The top-bar icon's numbers, and the SVG they describe.</summary>
public static class TopBarIconArtTests
{
    [Test]
    public static void TheBigRenderSplitsIntoWholeBlocks()
    {
        Check.Equal(468, TopBarIconArt.RenderWidth, "render width");
        Check.Equal(416, TopBarIconArt.RenderHeight, "render height");
        Check.Equal(0, TopBarIconArt.RenderWidth % TopBarIconArt.Block, "no leftover columns");
        Check.Equal(0, TopBarIconArt.RenderHeight % TopBarIconArt.Block, "no leftover rows");
        Check.Equal(117, TopBarIconArt.ArtWidth, "art width");
        Check.Equal(104, TopBarIconArt.ArtHeight, "art height, like the Map's 104");
    }

    [Test]
    public static void TheArtFillsTheMapsBoxAtOnePixelPerUnit()
    {
        float k = Math.Min(TopBarIconArt.Width / (float)TopBarIconArt.ArtWidth, TopBarIconArt.Height / (float)TopBarIconArt.ArtHeight);
        Check.Near(TopBarIconArt.Width, TopBarIconArt.ArtWidth * k, "drawn width", tolerance: 0.5);
        Check.Near(TopBarIconArt.Height, TopBarIconArt.ArtHeight * k, "drawn height", tolerance: 0.5);
        Check.Near(1.0, k * TopBarIconArt.ArtScale, "one art unit per screen pixel at 1080p", tolerance: 0.01);
        Check.Near(TopBarIconArt.Slot, TopBarIconArt.BoxTop * 2 + TopBarIconArt.Height, "the box is centred in the slot");
    }

    [Test]
    public static void TheSvgCanvasMatchesAndUsesOnlyShapes()
    {
        XElement svg = XElement.Load(SvgPath());
        Check.Equal($"0 0 {TopBarIconArt.Width} {TopBarIconArt.Height}", (string?)svg.Attribute("viewBox"), "viewBox");
        // Text needs fonts the game's SVG renderer doesn't have; images would mean embedded art; filters don't render.
        string[] banned = { "text", "image", "filter", "use" };
        foreach (XElement e in svg.Descendants())
            Check.True(!banned.Contains(e.Name.LocalName), $"<{e.Name.LocalName}> in the icon");
    }

    private static string SvgPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WhoCarried.sln")))
                return Path.Combine(dir.FullName, "src", "WhoCarried", "UI", "Art", "TopBarIcon.svg");
        throw new CheckFailed("couldn't find the repo root above " + AppContext.BaseDirectory);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- TopBarIconArt`
Expected: build error, `TopBarIconArt` doesn't exist.

- [ ] **Step 3: Implement**

`src/WhoCarried/Core/TopBarIconArt.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>
/// The top-bar button's icon. Its SVG canvas is <see cref="Width"/>×<see cref="Height"/> units, one unit per screen
/// pixel at 1080p (the Map icon's box). The game's SVG renderer draws it at <see cref="RenderScale"/>, and every
/// <see cref="Block"/>×<see cref="Block"/> block becomes one hard pixel: art at <see cref="ArtScale"/>× with stepped
/// edges, like the game's own icons (the Map's art is 114×104), which the game then shrinks the same way.
/// </summary>
public static class TopBarIconArt
{
    public const int Width = 72, Height = 64, Block = 4;
    public const float ArtScale = 1.625f;
    public const float RenderScale = ArtScale * Block;

    public static int RenderWidth => (int)MathF.Round(Width * RenderScale);
    public static int RenderHeight => (int)MathF.Round(Height * RenderScale);
    public static int ArtWidth => RenderWidth / Block;
    public static int ArtHeight => RenderHeight / Block;

    /// <summary>The game's top-bar buttons are 80×80; the icon box sits at y 8–72 like the Map's.</summary>
    public const float Slot = 80f, BoxTop = 8f;

    /// <summary>Hover and press, as the game's top-bar buttons do them (no tilt: the owner wants it straight).</summary>
    public const float HoverGrow = 1.1f, HoverBright = 1.1f, PressDim = 0.4f, SettleSeconds = 1f;

    public const string SvgResource = "WhoCarried.TopBarIcon.svg";
}
```

In `src/WhoCarried/WhoCarried.csproj`, extend the last `ItemGroup`:

```xml
  <ItemGroup>
    <None Include="WhoCarried.json" CopyToOutputDirectory="PreserveNewest" />
    <EmbeddedResource Include="UI\Art\TopBarIcon.svg" LogicalName="WhoCarried.TopBarIcon.svg" />
  </ItemGroup>
```

- [ ] **Step 4: Run the tests and build**

Run: `dotnet run --project tests/WhoCarried.Tests -c Release -- TopBarIconArt` → `3/3 passed`.
Run: `dotnet build WhoCarried.sln -c Release --nologo -v q` → 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/TopBarIconArt.cs src/WhoCarried/WhoCarried.csproj tests/WhoCarried.Tests/TopBarIconArtTests.cs
git commit -m "Describe the top-bar icon's geometry and embed its SVG"
```

---

### Task 3: The button in the top bar

**Files:**
- Create: `src/WhoCarried/UI/TopBarButton.cs`
- Modify: `src/WhoCarried/UI/RecapUi.cs` (add `HotkeyName`), `src/WhoCarried/Game/Patches.cs` (add `TopBarPatch`),
  `src/WhoCarried/ModEntry.cs` (register it)

**Interfaces:**
- Consumes: `HardPixels.Majority`, `TopBarIconArt.*` (Tasks 1–2); `RecapUi.Show()`; `RecapTexts.ModName`;
  `Kit`, `RecapTheme.Tip`, `RecapTheme.TipEdge`, `RecapTheme.Text`; `Tracker.LogError`, `Tracker.Note`.
- Produces: `internal static Control? TopBarButton.AddTo(NTopBar bar)` (returns the button, the existing one, or null);
  `public const string TopBarButton.NodeName = "WhoCarriedTopBarButton"`; `public const string RecapUi.HotkeyName = "F8"`.
  Hover can be driven from outside by emitting the button's `MouseEntered` / `MouseExited` signals.

No unit test: everything here needs Godot. Task 4's preview checks it.

- [ ] **Step 1: Add the hotkey name to `RecapUi`**

Under `private const double IdleRefresh = 1.5;` add:

```csharp
    /// <summary>The key that toggles the recap, as the top-bar tooltip names it.</summary>
    public const string HotkeyName = "F8";
```

- [ ] **Step 2: Write `src/WhoCarried/UI/TopBarButton.cs`**

```csharp
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The podium button in the game's top bar, just left of the Map button: click to open the recap, hover for the game's
/// tooltip. It grows and brightens like the game's own top-bar buttons, but stays straight.
/// </summary>
internal static class TopBarButton
{
    public const string NodeName = "WhoCarriedTopBarButton";
    public const string Title = RecapTexts.ModName + " (" + RecapUi.HotkeyName + ")";
    public const string Description = "View everyone's damage, defense and awards for this run.";

    private static Texture2D? _icon;
    private static bool _iconFailed, _tipFailed;

    /// <summary>
    /// Adds the button to this top bar, just before its Map button, once. Returns the button (or the one already there),
    /// or null if the icon can't be drawn.
    /// </summary>
    public static Control? AddTo(NTopBar bar)
    {
        Control map = bar.Map;
        if (map.GetParent() is not Node row) return null;
        if (row.GetNodeOrNull<Control>(NodeName) is Control existing) return existing;
        if (Icon() is not Texture2D icon) return null;
        Control button = Build(icon);
        row.AddChild(button);
        row.MoveChild(button, map.GetIndex());
        Tracker.Note("top bar: recap button added");
        return button;
    }

    private static Control Build(Texture2D icon)
    {
        var button = new Control
        {
            Name = NodeName,
            CustomMinimumSize = new Vector2(TopBarIconArt.Slot, TopBarIconArt.Slot),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
        };
        // Keep-aspect in the Map icon's box; the texture has no mipmaps, so the game shrinks it like its own icons.
        var art = new TextureRect
        {
            Texture = icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2((TopBarIconArt.Slot - TopBarIconArt.Width) / 2, TopBarIconArt.BoxTop),
            Size = new Vector2(TopBarIconArt.Width, TopBarIconArt.Height),
            PivotOffset = new Vector2(TopBarIconArt.Width / 2f, TopBarIconArt.Height / 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        button.AddChild(art);
        var behaviour = new Behaviour(button, art);
        button.MouseEntered += behaviour.Enter;
        button.MouseExited += behaviour.Leave;
        button.GuiInput += behaviour.Input;
        button.TreeExiting += behaviour.Gone;
        return button;
    }

    /// <summary>Hover, press and click for one button.</summary>
    private sealed class Behaviour(Control button, TextureRect art)
    {
        private Tween? _tween;
        private bool _pressed;

        public void Enter()
        {
            Set(TopBarIconArt.HoverGrow, _pressed ? TopBarIconArt.PressDim : TopBarIconArt.HoverBright);
            ShowTip(button);
        }

        public void Leave()
        {
            Settle();
            HideTip(button);
        }

        public void Input(InputEvent input)
        {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
            button.AcceptEvent();
            if (click.Pressed)
            {
                _pressed = true;
                Set(TopBarIconArt.HoverGrow, TopBarIconArt.PressDim);
                return;
            }
            if (!_pressed) return;
            _pressed = false;
            Leave();
            // The release comes here even off the button; only a release over it counts as a click.
            if (new Rect2(Vector2.Zero, button.Size).HasPoint(click.Position)) RecapUi.Show();
        }

        public void Gone()
        {
            _tween?.Kill();
            HideTip(button);
        }

        private void Set(float scale, float bright)
        {
            _tween?.Kill();
            art.Scale = Vector2.One * scale;
            art.Modulate = new Color(bright, bright, bright);
        }

        /// <summary>Back to rest, easing out over a second like the game's buttons.</summary>
        private void Settle()
        {
            _tween?.Kill();
            if (!button.IsInsideTree()) return;
            _tween = button.CreateTween().SetParallel();
            _tween.TweenProperty(art, "scale", Vector2.One, TopBarIconArt.SettleSeconds)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
            _tween.TweenProperty(art, "modulate", Colors.White, TopBarIconArt.SettleSeconds)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        }
    }

    // ------------------------------------------------------------------ tooltip

    private const string OwnTipName = "WhoCarriedTopBarTip";

    /// <summary>The game's hover tip, under the button with right edges aligned like the Deck's.</summary>
    private static void ShowTip(Control owner)
    {
        HideTip(owner);
        if (!_tipFailed)
        {
            try
            {
                // Null means the game is holding tips back right now; that's not a failure.
                if (NHoverTipSet.CreateAndShow(owner, GameTip()) is NHoverTipSet set)
                    set.GlobalPosition = owner.GlobalPosition + new Vector2(owner.Size.X - set.Size.X, owner.Size.Y + 20f);
                return;
            }
            catch (Exception e)
            {
                _tipFailed = true;
                Tracker.LogError("top bar tooltip", e);
            }
        }
        ShowOwnTip(owner);
    }

    private static void HideTip(Control owner)
    {
        try { NHoverTipSet.Remove(owner); }
        catch (Exception) { }
        owner.GetNodeOrNull(OwnTipName)?.QueueFree();
    }

    /// <summary>
    /// The game's tips take their title from its own text tables, which don't have ours: start from one of its plain
    /// lines and put our words in.
    /// </summary>
    private static IHoverTip GameTip()
    {
        object tip = new HoverTip(new LocString("static_hover_tips", "DECK.description"));
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(tip, Title);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(tip, Description);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Id)).SetValue(tip, NodeName);
        return (IHoverTip)tip;
    }

    /// <summary>Fallback when the game's tip can't be made: the same words in a small panel of our own.</summary>
    private static void ShowOwnTip(Control owner)
    {
        var k = new Kit(1f, _ => null);
        var panel = new PanelContainer { Name = OwnTipName, TopLevel = true, ZIndex = 100, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = RecapTheme.Tip, BorderColor = RecapTheme.TipEdge,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddChild(k.Strong(Title, 24));
        lines.AddChild(k.Text(Description, 20, RecapTheme.Text));
        panel.AddChild(lines);
        owner.AddChild(panel);
        panel.ResetSize();
        panel.GlobalPosition = owner.GlobalPosition + new Vector2(owner.Size.X - panel.Size.X, owner.Size.Y + 20f);
    }

    // ------------------------------------------------------------------ icon

    /// <summary>The podium, built once: drawn big by the game's SVG renderer, then made hard-edged like the game's art.</summary>
    private static Texture2D? Icon()
    {
        if (_icon != null && GodotObject.IsInstanceValid(_icon)) return _icon;
        if (_iconFailed) return null;
        try
        {
            var big = new Image();
            Error error = big.LoadSvgFromString(ReadSvg(), TopBarIconArt.RenderScale);
            if (error != Error.Ok) throw new InvalidOperationException($"the SVG didn't draw: {error}");
            big.Convert(Image.Format.Rgba8);
            HardPixels.Result art = HardPixels.Majority(big.GetData(), big.GetWidth(), big.GetHeight(), TopBarIconArt.Block);
            _icon = ImageTexture.CreateFromImage(Image.CreateFromData(art.Width, art.Height, false, Image.Format.Rgba8, art.Pixels));
            return _icon;
        }
        catch (Exception e)
        {
            _iconFailed = true;
            Tracker.LogError("top bar icon (F8 still works)", e);
            return null;
        }
    }

    private static string ReadSvg()
    {
        using Stream stream = typeof(TopBarButton).Assembly.GetManifestResourceStream(TopBarIconArt.SvgResource)
            ?? throw new InvalidOperationException($"missing embedded {TopBarIconArt.SvgResource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 3: Add the patch** — in `src/WhoCarried/Game/Patches.cs`, add `using MegaCrit.Sts2.Core.Nodes.CommonUi;`
  and, after `GameOverScreenPatch`:

```csharp
/// <summary>The top bar is set up once per run, solo or co-op: add the recap button next to Map.</summary>
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class TopBarPatch
{
    private static void Postfix(NTopBar __instance)
    {
        try { TopBarButton.AddTo(__instance); }
        catch (Exception e) { Tracker.LogError("NTopBar.Initialize", e); }
    }
}
```

- [ ] **Step 4: Register it** — in `ModEntry.PatchClasses`, add `typeof(TopBarPatch),` after `typeof(GameOverScreenPatch),`.

- [ ] **Step 5: Build and run the tests**

Run: `dotnet build WhoCarried.sln -c Release --nologo -v q` → 0 warnings, 0 errors.
Run: `dotnet run --project tests/WhoCarried.Tests -c Release` → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/WhoCarried/UI/TopBarButton.cs src/WhoCarried/UI/RecapUi.cs src/WhoCarried/Game/Patches.cs src/WhoCarried/ModEntry.cs
git commit -m "Add the recap button to the game's top bar"
```

---

### Task 4: Preview screenshots and the in-game check

**Files:**
- Modify: `src/WhoCarried/UI/DevPreview.cs`
- Scratch (untracked): the session's `cycle.ps1` waits for the new last screenshot

**Interfaces:**
- Consumes: `TopBarButton.AddTo`, `TopBarButton.NodeName`.
- Produces: `data/preview-10-topbar.png`, `data/preview-11-topbar-hover.png`, `data/preview-topbar-icon.png` (the
  117×104 art), and one `top bar preview:` line in `events.log` with the podium's and Map icon's boxes.

- [ ] **Step 1: Capture the top bar after the export** — in `DevPreview.Run`, inside `CaptureLive`'s export callback,
  replace

```csharp
                        Tracker.Note(error == null ? "preview done" : $"preview export failed: {error}");
                        RecapUi.Hide();
```

with

```csharp
                        if (error != null) Tracker.Note($"preview export failed: {error}");
                        RecapUi.Hide();
                        CaptureTopBar(dataDir, () => Tracker.Note("preview done"));
```

and add to `DevPreview` (plus `using MegaCrit.Sts2.Core.Nodes.CommonUi;`):

```csharp
    /// <summary>
    /// The game's real top bar with the recap button added the way a run adds it, at rest and hovered. The bar isn't set
    /// up for a run here, so its labels show placeholders, and its own start-up logs an error when it can't find the
    /// run's screens (the buttons are ready before that).
    /// </summary>
    private static void CaptureTopBar(string dataDir, Action done)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var layer = new CanvasLayer { Layer = 101, Name = "WhoCarriedPreviewTopBar" };
        root.AddChild(layer);
        NTopBar bar = ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        layer.AddChild(bar);
        Control? button = TopBarButton.AddTo(bar);
        if (button == null)
        {
            Tracker.Note("top bar preview: no button (icon failed, see the log)");
            layer.QueueFree();
            done();
            return;
        }
        Later.Run(0.8, () =>
        {
            Control podium = button.GetChild<Control>(0);
            Control mapIcon = bar.Map.GetNode<Control>("Control/Icon");
            Tracker.Note($"top bar preview: podium box {podium.GetGlobalRect()}, map icon box {mapIcon.GetGlobalRect()}");
            (((TextureRect)podium).Texture as ImageTexture)?.GetImage().SavePng(Path.Combine(dataDir, "preview-topbar-icon.png"));
            root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-10-topbar.png"));
            button.EmitSignal(Control.SignalName.MouseEntered);
            Later.Run(0.8, () =>
            {
                root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-11-topbar-hover.png"));
                button.EmitSignal(Control.SignalName.MouseExited);
                layer.QueueFree();
                done();
            });
        });
    }
```

- [ ] **Step 2: Build, test, deploy and run the preview** — build and tests as before. In the session scratchpad's
  `cycle.ps1`, change both `preview-9-export.png` checks to `preview-11-topbar-hover.png`, then run
  `powershell -Command "& '<scratchpad>\cycle.ps1' -Sizes 4"` (it deploys, launches, waits, closes the game fully and
  recycles the flag).
  Expected: the last log lines include `loaded v0.1.0: 12/12 patches applied`; `events.log` has
  `top bar: recap button added`, the `top bar preview:` boxes and `preview done`.

- [ ] **Step 3: Check the screenshots**
  - `preview-10-topbar.png`: the podium sits left of the Map scroll, in the same row, not overlapping the timer.
  - Alignment: the podium's box has the same top and height as the Map icon's box (from the log line). From
    `preview-topbar-icon.png`, the first and last rows with alpha > 60 map to slot y ≈ 9.3 and ≈ 70.2
    (`8 + row × 64/104`), inside the game icons' 8.0–71.4 band and centred like them (≈ 39.8).
  - Crunchy edges: zoomed in, the crown's diagonals step like the Deck's strokes; no stray colours.
  - `preview-11-topbar-hover.png`: podium 10% larger, brighter, not rotated; the game's tooltip under it reading
    "Who Carried? (F8)" and the description. If it's the fallback panel instead, find the error in `godot.log`.
  - Recycle `preview-topbar-icon.png`'s copies with the other preview files (they stay out of the repo).

- [ ] **Step 4: Commit**

```bash
git add src/WhoCarried/UI/DevPreview.cs
git commit -m "Preview the top-bar button at rest and hovered"
```

- [ ] **Step 5: Hand over** — squash `feature/top-bar-button` into `main` as one commit, delete the branch, leave the
  build deployed. The owner checks in a real run: podium in line with Map and Deck, tooltip on hover, click opens the
  recap, F8 still toggles.
