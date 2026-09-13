# Controller Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the recap fully usable with a controller: reach the podium through the top bar's d-pad navigation, and inside the recap switch tabs, close, save, scroll and step through cards and chart fights, with nothing leaking to the game underneath.

**Architecture:** Pure selection logic in `Core/PadCursor` (unit-tested). The open panel takes key focus, so its `GuiInput` signal gets every non-mouse event before the game's input manager, hotkeys and focus navigation; `UI/PadInput` translates each through the game's own bindings into a `PadCommand`, acts on it and accepts (swallows) it. Tabs describe what can be selected as `PadRow`s whose show/clear are the same entry points mouse hover uses. `UI/PadHints` shows the game's controller glyphs in controller mode. The podium joins the top bar's focus chain from a postfix on `NTopBar.UpdateNavigation`.

**Tech Stack:** C# / .NET 9, Godot 4 (GodotSharp), Harmony, the game's `sts2.dll` (v0.111.0). Tests: the repo's own runner (`[Test]` static methods, `Check.*`).

## Global Constraints

- Spec: `docs/design/specs/2026-09-13-controller-support-design.md`.
- The mod can't override Godot virtuals (`_Input`, `_GuiInput`…): the game loads mod DLLs without registering Godot script classes. Use signals and events only (`GuiInput`, `FocusEntered`, `SceneTree.ProcessFrame`, …), as the rest of the mod does.
- Game facts this relies on (v0.111.0): `NInputManager._UnhandledInput` turns `controller_*` actions into game actions through its private `_controllerInputMap` (game action → controller action); `NHotkeyManager` dispatches hotkeys in `_UnhandledInput`; `NControllerManager._Input` switches to controller mode on the first controller press, consumes that press and calls `ActiveScreenContext.Instance.FocusOnDefaultControl()`; switching to mouse mode calls `GuiReleaseFocus()`; the game's clickable controls handle `MegaInput.select` in `_GuiInput` when focused.
- No hardcoded system paths in tracked files. Don't name other mods in repo text.
- Build: `"C:\Program Files\dotnet\dotnet.exe"` (never the x86 `dotnet` on PATH). Tests: `"C:\Program Files\dotnet\dotnet.exe" run --project tests/WhoCarried.Tests [-- <filter>]`.
- Never launch or deploy while the game runs (the DLL is locked). The dev preview needs the owner's go-ahead to launch the game.
- Work on `feature/controller-support`; squash into `main` at the end; never push.
- Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## File map

| File | Change |
|---|---|
| `src/WhoCarried/Core/PadCursor.cs` | New. `PadDirection`, `PadCommand`, `PadRowShape`, `PadCommands`, `PadCursor`, `PadRepeat`. Pure. |
| `tests/WhoCarried.Tests/PadCursorTests.cs` | New. |
| `src/WhoCarried/UI/PadTab.cs` | New. `PadRow`, `PadTab`: what a tab offers the controller. |
| `src/WhoCarried/UI/CardFace.cs` | `Lift` becomes public `SetLifted`; the preview's hover guard moves into the mouse handlers. |
| `src/WhoCarried/UI/Climb.cs` | Hover code reorganised into `Point(int)`; optional `PadTab` registers the fights row. |
| `src/WhoCarried/UI/TimelineTab.cs` | Optional `PadTab` registers the chart's fights row. |
| `src/WhoCarried/UI/ScoreboardTab.cs` | The hand registers its cards row; the climb gets the tab's `PadTab`. |
| `src/WhoCarried/UI/DecksTab.cs`, `SourcesTab.cs`, `DebuffsTab.cs` | Register the banner row and/or scrolling. |
| `src/WhoCarried/UI/RecapPanel.cs` | One `PadTab` per view; `PanelHandle` gains `Pads`, `Close`, `Save`; button hints. |
| `src/WhoCarried/UI/PadInput.cs` | New. Focus, translation, commands, hold-repeat, mouse-mode clearing, focus hand-back. |
| `src/WhoCarried/UI/PadHints.cs` | New. The game's glyphs on the tab bar, Close and Save image. |
| `src/WhoCarried/UI/RecapUi.cs` | Attaches `PadInput` to each opened panel. |
| `src/WhoCarried/UI/TopBarButton.cs` | Focusable podium, focus look, A to open, `JoinNavigation`. |
| `src/WhoCarried/Game/Patches.cs`, `ModEntry.cs` | `TopBarNavigationPatch` (13 patches). |
| `src/WhoCarried/UI/DevPreview.cs` | Controller checks and screenshots 12–15. |
| `docs/…` | Spec touch-ups, docs index, plan link. |

---

### Task 1: The selection logic (pure)

**Files:**
- Create: `src/WhoCarried/Core/PadCursor.cs`
- Test: `tests/WhoCarried.Tests/PadCursorTests.cs`

**Interfaces:**
- Produces:
  - `enum PadDirection { Up, Down, Left, Right }`
  - `enum PadCommand { None, TabPrevious, TabNext, Close, Save, Up, Down, Left, Right, ScrollUp, ScrollDown }`
  - `readonly record struct PadRowShape(int Count, int Start)`
  - `static class PadCommands { PadDirection? Direction(PadCommand); bool Repeats(PadCommand); }`
  - `sealed class PadCursor { int Row; int Item; bool Selected; bool Move(PadDirection, IReadOnlyList<PadRowShape>); bool Fit(IReadOnlyList<PadRowShape>); void Clear(); static int NextTab(int index, int step, int count); }`
  - `static class PadRepeat { const ulong DelayMs = 400, IntervalMs = 80; static int Steps(ulong fromMs, ulong toMs); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>What the controller selects in a tab: docs/design/specs/2026-09-13-controller-support-design.md.</summary>
public static class PadCursorTests
{
    private static PadRowShape[] Rows(params (int Count, int Start)[] rows) => rows.Select(r => new PadRowShape(r.Count, r.Start)).ToArray();

    private static string At(PadCursor c) => c.Selected ? $"{c.Row}:{c.Item}" : "none";

    [Test]
    public static void TheFirstPressSelectsWhereTheRowStarts()
    {
        var cards = new PadCursor();
        Check.True(cards.Move(PadDirection.Right, Rows((4, 0))), "selected");
        Check.Equal("0:0", At(cards), "a card row starts at the first card");

        var chart = new PadCursor();
        chart.Move(PadDirection.Left, Rows((15, 14)));
        Check.Equal("0:14", At(chart), "a chart row starts at the latest fight");
    }

    [Test]
    public static void TheFirstPressSkipsEmptyRowsAndDoesNothingWithoutAny()
    {
        var cursor = new PadCursor();
        cursor.Move(PadDirection.Down, Rows((0, 0), (3, 2)));
        Check.Equal("1:2", At(cursor), "the first row with items");

        var none = new PadCursor();
        Check.True(!none.Move(PadDirection.Right, Rows((0, 0))), "nothing to select");
        Check.Equal("none", At(none), "still nothing");
        Check.True(!none.Move(PadDirection.Right, Rows()), "no rows at all");
    }

    [Test]
    public static void LeftAndRightStepAndStopAtTheEnds()
    {
        var cursor = new PadCursor();
        PadRowShape[] rows = Rows((3, 0));
        cursor.Move(PadDirection.Right, rows);
        Check.True(!cursor.Move(PadDirection.Left, rows), "already at the first");
        Check.True(cursor.Move(PadDirection.Right, rows), "to the second");
        Check.True(cursor.Move(PadDirection.Right, rows), "to the third");
        Check.True(!cursor.Move(PadDirection.Right, rows), "no wrap");
        Check.Equal("0:2", At(cursor), "on the last");
    }

    [Test]
    public static void UpAndDownSwitchRowsSkippingEmptyOnes()
    {
        var cursor = new PadCursor();
        PadRowShape[] rows = Rows((4, 0), (0, 0), (15, 14));
        cursor.Move(PadDirection.Right, rows);
        cursor.Move(PadDirection.Right, rows);
        Check.True(cursor.Move(PadDirection.Down, rows), "down");
        Check.Equal("2:14", At(cursor), "past the empty row, landing where the chart starts");
        Check.True(!cursor.Move(PadDirection.Down, rows), "nothing further down");
        Check.True(cursor.Move(PadDirection.Up, rows), "up");
        Check.Equal("0:0", At(cursor), "back to the cards, where they start");
        Check.True(!cursor.Move(PadDirection.Up, rows), "nothing further up");
    }

    [Test]
    public static void ALiveUpdateKeepsThePlaceOrTheNearest()
    {
        var cursor = new PadCursor();
        cursor.Move(PadDirection.Left, Rows((10, 9)));
        Check.True(!cursor.Fit(Rows((11, 10))), "a fight added: still on fight 9");
        Check.Equal("0:9", At(cursor), "same fight");
        Check.True(cursor.Fit(Rows((5, 4))), "fewer fights than the one shown");
        Check.Equal("0:4", At(cursor), "the last that's left");
    }

    [Test]
    public static void ARowThatEmptiesHandsOverToTheNearestRow()
    {
        var cursor = new PadCursor();
        cursor.Move(PadDirection.Down, Rows((0, 0), (3, 0)));
        Check.True(cursor.Fit(Rows((2, 1), (0, 0))), "its row emptied");
        Check.Equal("0:1", At(cursor), "the nearest row with items, where it starts");
        Check.True(cursor.Fit(Rows((0, 0), (0, 0))), "everything emptied");
        Check.Equal("none", At(cursor), "nothing selected");
        Check.True(!cursor.Fit(Rows((0, 0))), "nothing to keep");
    }

    [Test]
    public static void ClearDeselects()
    {
        var cursor = new PadCursor();
        cursor.Move(PadDirection.Right, Rows((2, 0)));
        cursor.Clear();
        Check.Equal("none", At(cursor), "cleared");
        cursor.Move(PadDirection.Right, Rows((2, 0)));
        Check.Equal("0:0", At(cursor), "the next press selects again, where the row starts");
    }

    [Test]
    public static void TabsWrapBothWays()
    {
        Check.Equal(1, PadCursor.NextTab(0, 1, 7), "next");
        Check.Equal(0, PadCursor.NextTab(6, 1, 7), "past the last");
        Check.Equal(6, PadCursor.NextTab(0, -1, 7), "before the first");
        Check.Equal(0, PadCursor.NextTab(3, 1, 0), "no tabs");
    }

    [Test]
    public static void HoldingRepeatsAfterADelay()
    {
        Check.Equal(0, PadRepeat.Steps(0, 399), "not yet");
        Check.Equal(1, PadRepeat.Steps(0, 400), "the first repeat at 400 ms");
        Check.Equal(0, PadRepeat.Steps(400, 479), "the next is due at 480");
        Check.Equal(1, PadRepeat.Steps(400, 480), "480");
        Check.Equal(3, PadRepeat.Steps(0, 560), "400, 480 and 560 in one long frame");
        Check.Equal(0, PadRepeat.Steps(500, 450), "time never runs backwards");
    }

    [Test]
    public static void CommandsKnowTheirDirectionAndWhetherTheyRepeat()
    {
        Check.Equal(PadDirection.Left, PadCommands.Direction(PadCommand.Left), "left");
        Check.True(PadCommands.Direction(PadCommand.Close) == null, "close isn't a direction");
        Check.True(PadCommands.Repeats(PadCommand.Down), "moving repeats");
        Check.True(PadCommands.Repeats(PadCommand.ScrollUp), "scrolling repeats");
        Check.True(!PadCommands.Repeats(PadCommand.TabNext), "switching tabs doesn't");
        Check.True(!PadCommands.Repeats(PadCommand.Save), "saving doesn't");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `"C:\Program Files\dotnet\dotnet.exe" run --project tests/WhoCarried.Tests -- PadCursor`
Expected: build errors, `PadCursor`, `PadRowShape`, `PadDirection` … not found.

- [ ] **Step 3: Write the implementation**

`src/WhoCarried/Core/PadCursor.cs`:

```csharp
namespace WhoCarried.Core;

/// <summary>A d-pad (or left stick) direction.</summary>
public enum PadDirection { Up, Down, Left, Right }

/// <summary>What a controller press means inside the recap.</summary>
public enum PadCommand { None, TabPrevious, TabNext, Close, Save, Up, Down, Left, Right, ScrollUp, ScrollDown }

/// <summary>One row the controller can step through: how many items it has, and where the first press lands.</summary>
public readonly record struct PadRowShape(int Count, int Start);

public static class PadCommands
{
    public static PadDirection? Direction(PadCommand command) => command switch
    {
        PadCommand.Up => PadDirection.Up,
        PadCommand.Down => PadDirection.Down,
        PadCommand.Left => PadDirection.Left,
        PadCommand.Right => PadDirection.Right,
        _ => null,
    };

    /// <summary>Holding it repeats: moving and scrolling do; switching tabs, closing and saving don't.</summary>
    public static bool Repeats(PadCommand command) =>
        Direction(command) != null || command is PadCommand.ScrollUp or PadCommand.ScrollDown;
}

/// <summary>
/// What the controller has selected on one tab: nothing, or one item of one row. The first press selects where the
/// first row with items starts; then left and right step along the row and stop at its ends, and up and down move to
/// the next row with items, landing where it starts. No game or Godot types.
/// </summary>
public sealed class PadCursor
{
    public int Row { get; private set; } = -1;
    public int Item { get; private set; } = -1;
    public bool Selected => Row >= 0;

    /// <summary>A d-pad press on a tab with these rows. True when the selection changed.</summary>
    public bool Move(PadDirection direction, IReadOnlyList<PadRowShape> rows)
    {
        if (Fit(rows)) return true;
        if (!Selected)
        {
            int first = Next(rows, -1, 1);
            if (first < 0) return false;
            Select(first, rows);
            return true;
        }
        switch (direction)
        {
            case PadDirection.Left:
                if (Item <= 0) return false;
                Item--;
                return true;
            case PadDirection.Right:
                if (Item >= rows[Row].Count - 1) return false;
                Item++;
                return true;
            default:
                int row = Next(rows, Row, direction == PadDirection.Down ? 1 : -1);
                if (row < 0) return false;
                Select(row, rows);
                return true;
        }
    }

    /// <summary>
    /// Keeps the selection valid after the rows change size (a live update): the same item if it's still there, else
    /// the last one; a row left empty hands over to the nearest row with items. True when the selection moved.
    /// </summary>
    public bool Fit(IReadOnlyList<PadRowShape> rows)
    {
        if (!Selected) return false;
        if (Row < rows.Count && rows[Row].Count > 0)
        {
            int last = rows[Row].Count - 1;
            if (Item <= last) return false;
            Item = last;
            return true;
        }
        int nearest = Nearest(rows, Math.Min(Row, rows.Count - 1));
        if (nearest < 0) Clear();
        else Select(nearest, rows);
        return true;
    }

    public void Clear()
    {
        Row = -1;
        Item = -1;
    }

    /// <summary>The tab <paramref name="step"/> away from <paramref name="index"/>, wrapping round.</summary>
    public static int NextTab(int index, int step, int count) => count <= 0 ? 0 : ((index + step) % count + count) % count;

    private void Select(int row, IReadOnlyList<PadRowShape> rows)
    {
        Row = row;
        Item = Math.Clamp(rows[row].Start, 0, rows[row].Count - 1);
    }

    /// <summary>The first row after <paramref name="from"/>, going <paramref name="step"/>, that has items; -1 if none.</summary>
    private static int Next(IReadOnlyList<PadRowShape> rows, int from, int step)
    {
        for (int r = from + step; r >= 0 && r < rows.Count; r += step)
            if (rows[r].Count > 0) return r;
        return -1;
    }

    /// <summary>The row with items closest to <paramref name="around"/> (the earlier one on a tie); -1 if none.</summary>
    private static int Nearest(IReadOnlyList<PadRowShape> rows, int around)
    {
        for (int d = 0; d < rows.Count; d++)
        {
            if (around - d >= 0 && around - d < rows.Count && rows[around - d].Count > 0) return around - d;
            if (around + d >= 0 && around + d < rows.Count && rows[around + d].Count > 0) return around + d;
        }
        return -1;
    }
}

/// <summary>Holding a direction: one step on the press, a repeat after 400 ms, then one every 80 ms.</summary>
public static class PadRepeat
{
    public const ulong DelayMs = 400, IntervalMs = 80;

    /// <summary>Repeats due between <paramref name="fromMs"/> and <paramref name="toMs"/> of holding (the end included).</summary>
    public static int Steps(ulong fromMs, ulong toMs) => Math.Max(0, Fired(toMs) - Fired(fromMs));

    private static int Fired(ulong heldMs) => heldMs < DelayMs ? 0 : (int)((heldMs - DelayMs) / IntervalMs) + 1;
}
```

- [ ] **Step 4: Run the tests**

Run: `"C:\Program Files\dotnet\dotnet.exe" run --project tests/WhoCarried.Tests`
Expected: all pass (141 existing + 10 new = `151/151 passed`).

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/Core/PadCursor.cs tests/WhoCarried.Tests/PadCursorTests.cs
git commit -m "Controller: selection logic for the recap's tabs"
```

---

### Task 2: Tabs say what can be selected

Mouse behaviour must not change. Nothing uses the rows yet; this task only builds.

**Files:**
- Create: `src/WhoCarried/UI/PadTab.cs`
- Modify: `CardFace.cs`, `Climb.cs`, `TimelineTab.cs`, `ScoreboardTab.cs`, `DecksTab.cs`, `SourcesTab.cs`, `DebuffsTab.cs`, `RecapPanel.cs`

**Interfaces:**
- Consumes: `PadRowShape` (Task 1).
- Produces:
  - `sealed class PadRow(Func<int> count, Func<int> start, Action<int> show, Action clear) { PadRowShape Shape; void Show(int); void Clear(); }`
  - `sealed class PadTab { List<PadRow> Rows; Action<int>? Scroll; IReadOnlyList<PadRowShape> Shapes(); const float ScrollStep = 90; static Action<int> Scrolls(ScrollContainer, float stepPx); }`
  - `record PanelHandle(Control Root, TabContainer Tabs, Label Status, Live Live, IReadOnlyList<PadTab> Pads, Action Close, Action Save)`
  - `CardFace.SetLifted(bool on)` (public)

- [ ] **Step 1: `src/WhoCarried/UI/PadTab.cs`**

```csharp
using Godot;
using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>A row the controller steps through: its items are shown the way mouse hover shows them.</summary>
internal sealed class PadRow(Func<int> count, Func<int> start, Action<int> show, Action clear)
{
    public PadRowShape Shape => new(count(), start());
    public void Show(int item) => show(item);
    public void Clear() => clear();
}

/// <summary>What the controller can do on one tab: its rows, and scrolling if it has a long list.</summary>
internal sealed class PadTab
{
    /// <summary>Design pixels one scroll step moves.</summary>
    public const float ScrollStep = 90;

    public List<PadRow> Rows { get; } = new();

    /// <summary>Scrolls the tab's list one step (+1 down, -1 up); null when it has nothing to scroll.</summary>
    public Action<int>? Scroll { get; set; }

    public IReadOnlyList<PadRowShape> Shapes() => Rows.Select(r => r.Shape).ToList();

    public static Action<int> Scrolls(ScrollContainer scroll, float stepPx) => direction =>
    {
        if (GodotObject.IsInstanceValid(scroll)) scroll.ScrollVertical += (int)(direction * stepPx);
    };
}
```

- [ ] **Step 2: `CardFace`: public lift, preview guard only for the mouse**

Replace `LiftOnHover` and `Lift` (currently `CardFace.cs:159-176`) with:

```csharp
    /// <summary>The card rises a little and grows when the mouse is over it, like a card in hand.</summary>
    public void LiftOnHover()
    {
        Root.MouseFilter = Control.MouseFilterEnum.Pass;
        _home = Root.Position;
        _homeRotation = Root.Rotation;
        // The dev preview ignores the real, idle cursor; the controller still lifts cards there.
        Root.MouseEntered += () => { if (!Climb.IgnoreHover) SetLifted(true); };
        Root.MouseExited += () => { if (!Climb.IgnoreHover) SetLifted(false); };
    }

    /// <summary>Lifts the card or sets it back down: mouse hover, or the controller selecting it.</summary>
    public void SetLifted(bool on)
    {
        if (_lifted == on) return;
        _lifted = on;
        (Vector2 at, Vector2 size) = Lifted();
        Anim.To(Root, "position", at);
        Anim.To(Root, "scale", size);
    }
```

- [ ] **Step 3: `Climb`: one entry point for pointing at a fight, and its row**

Change the signature (`Climb.cs:19`) to add a last parameter:

```csharp
    public static Control Create(Kit k, RecapView view, float width, float height, float barMax, bool interactive, Live? live,
                                 PadTab? pad = null)
```

After `void ShowTip() { … }` add:

```csharp
        // Points at fight i (-1: none), from the mouse or the controller.
        void Point(int i)
        {
            if (i == hovered) return;
            hovered = i;
            chart.QueueRedraw();
            ShowTip();
        }
```

Replace the `if (interactive) { … }` block at the end (`Climb.cs:188-208`) with:

```csharp
        if (interactive)
        {
            chart.GuiInput += input =>
            {
                if (input is not InputEventMouseMotion motion || fights == 0 || IgnoreHover) return;
                float x = motion.Position.X / k.S, y = motion.Position.Y / k.S;
                int nearest = Enumerable.Range(0, fights).OrderBy(i => Math.Abs(X(i) - x)).First();
                Point(Math.Abs(X(nearest) - x) <= Math.Max(colW, (width - 40) / Math.Max(1, fights - 1) / 2) && y > baseY - 30 - barMax && y < baseY + 30
                    ? nearest : -1);
            };
            chart.MouseExited += () => Point(-1);
            pad?.Rows.Add(new PadRow(() => fights, () => fights - 1, Point, () => Point(-1)));
        }
```

- [ ] **Step 4: `TimelineTab`: the chart's row**

`Create` (`TimelineTab.cs:25`) becomes `public static Control Create(Kit k, RecapView view, Live live, PadTab? pad = null)` and passes it on:

```csharp
        frame.AddChild(Chart(k, view, ChartW, ChartH, interactive: true, live, visible => { frame.Visible = visible; empty.Visible = !visible; }, pad));
```

`Chart`'s signature (`TimelineTab.cs:76-77`) gains `PadTab? pad = null` after `setVisible`:

```csharp
    public static Control Chart(Kit k, RecapView view, float width, float height, bool interactive, Live? live,
                                Action<bool>? setVisible = null, PadTab? pad = null)
```

Inside `if (interactive) { … }`, after `chart.MouseExited += Hide;` add:

```csharp
            pad?.Rows.Add(new PadRow(() => fights, () => fights - 1, i => Show(i), Hide));
```

- [ ] **Step 5: `ScoreboardTab`: the cards row, and the climb's**

`Create` becomes `public static Control Create(Kit k, RecapView view, Live live, bool deal, PadTab? pad = null)`. After `live.On(hand.Sync);` add `if (pad != null) pad.Rows.Add(hand.Row());`, and pass `pad` to the climb:

```csharp
        tab.AddChild(k.At(Climb.Create(k, view, 1522, 204, 92, interactive: true, live, pad), 40, 682));
```

In `Hand`, add a field and keep it in scoreboard order at the end of `Sync` (after the `for` loop, before `_deal = false;`):

```csharp
        /// <summary>The cards in scoreboard order, as the controller steps through them.</summary>
        private readonly List<PlayerCard> _order = new();
```

```csharp
            _order.Clear();
            _order.AddRange(players.Select(p => _cards[p.Label]));
```

and the row:

```csharp
        /// <summary>The cards for the controller: selecting one lifts it as hover does and sets the others down.</summary>
        public PadRow Row() => new(() => _order.Count, () => 0,
            i => { for (int j = 0; j < _order.Count; j++) _order[j].Face.SetLifted(j == i); },
            () => { foreach (PlayerCard card in _order) card.Face.SetLifted(false); });
```

- [ ] **Step 6: `DecksTab`, `SourcesTab`, `DebuffsTab`**

`DecksTab.Create` becomes `public static Control Create(Kit k, RecapView view, CardVisuals? cards, Live live, PadTab? pad = null)`. Just before `return tab;` add:

```csharp
        if (pad != null)
        {
            // The banners: stepping to one shows that deck straight away; the first press lands on the one shown.
            pad.Rows.Add(new PadRow(() => tabs.Count, () => Math.Max(0, tabs.FindIndex(t => t.Player == shownPlayer)),
                i => { if (i >= 0 && i < tabs.Count && tabs[i].Player != shownPlayer) ShowDeck(tabs[i].Player); },
                () => { }));
            pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
        }
```

`SourcesTab.Create` and `DebuffsTab.Create` each gain `PadTab? pad = null` as the last parameter and, right after `tab.AddChild(scroll);`:

```csharp
        if (pad != null) pad.Scroll = PadTab.Scrolls(scroll, k.U(PadTab.ScrollStep));
```

- [ ] **Step 7: `RecapPanel`: one `PadTab` per view, and a richer handle**

Replace the record (`RecapPanel.cs:8`):

```csharp
/// <summary>
/// What the caller needs to drive an open recap: switch views, show a status message, push live updates, and what the
/// controller can do (each view's rows, close, save).
/// </summary>
internal sealed record PanelHandle(Control Root, TabContainer Tabs, Label Status, Live Live, IReadOnlyList<PadTab> Pads,
                                   Action Close, Action Save);
```

In `Create`, replace from `var handle = new PanelHandle(...)` down to the `tabs.AddChild(Safe(k, 6, ...))` line with:

```csharp
        PadTab[] pads = Views.Select(_ => new PadTab()).ToArray();
        PanelHandle? handle = null;
        void Save() => onSave(handle!);
        handle = new PanelHandle(root, tabs, status, live, pads, onClose, Save);

        tabs.AddChild(Safe(k, 0, pads[0], () => ScoreboardTab.Create(k, view, live, deal: true, pads[0])));
        tabs.AddChild(Safe(k, 1, pads[1], () => AwardsTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 2, pads[2], () => SourcesTab.Create(k, view, live, pads[2])));
        tabs.AddChild(Safe(k, 3, pads[3], () => DebuffsTab.Create(k, view, live, pads[3])));
        tabs.AddChild(Safe(k, 4, pads[4], () => TimelineTab.Create(k, view, live, pads[4])));
        tabs.AddChild(Safe(k, 5, pads[5], () => DefenseTab.Create(k, view, live)));
        tabs.AddChild(Safe(k, 6, pads[6], () => DecksTab.Create(k, view, cards, live, pads[6])));
```

The next line (`root.AddChild(TopBar(k, view, status, onClose, () => onSave(handle), …))`) becomes `root.AddChild(TopBar(k, view, status, onClose, Save, live, screen.X, stage.Position));`.

`Safe` takes the pad and empties it if the view fails (rows could point at half-built controls):

```csharp
    private static Control Safe(Kit k, int index, PadTab pad, Func<Control> build)
    {
        try
        {
            return Named(build(), index);
        }
        catch (Exception e)
        {
            Tracker.LogError($"recap view {Views[index]}", e);
            pad.Rows.Clear();
            pad.Scroll = null;
            Control failed = k.Box(DesignW, DesignH);
            failed.AddChild(k.At(k.Text("This view couldn't be drawn this time. The details are in the mod's log.", 18, RecapTheme.Muted), 40, 160));
            return Named(failed, index);
        }
    }
```

- [ ] **Step 8: Build and test**

Run: `"C:\Program Files\dotnet\dotnet.exe" build src/WhoCarried -c Release` → `Build succeeded`, 0 errors.
Run the tests → `151/151 passed`.

- [ ] **Step 9: Commit**

```bash
git add src/WhoCarried/UI
git commit -m "Controller: tabs say what can be selected"
```

---

### Task 3: The recap listens to the controller

**Files:**
- Create: `src/WhoCarried/UI/PadInput.cs`
- Modify: `src/WhoCarried/UI/RecapUi.cs` (`ShowView`)

**Interfaces:**
- Consumes: `PadCursor`, `PadRepeat`, `PadCommands`, `PadCommand`, `PadDirection` (Task 1); `PanelHandle.Pads/Close/Save/Tabs/Live/Root`, `PadTab`, `PadRow` (Task 2).
- Produces: `PadInput.Attach(PanelHandle panel)`; `PadInput.ControllerMode` (bool); `PadInput.Diagnostics` (bool, dev preview) and `PadInput.LastCommand` (PadCommand, dev preview).

- [ ] **Step 1: `src/WhoCarried/UI/PadInput.cs`**

```csharp
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Controller (and keyboard) input for the open recap. The panel takes key focus, so every non-mouse press reaches its
/// GuiInput before the game's input manager, hotkeys and focus navigation. Each is translated through the game's own
/// bindings into a <see cref="PadCommand"/>, acted on, and swallowed whether it meant anything or not. Mouse input and
/// F8 (polled) are untouched. docs/design/specs/2026-09-13-controller-support-design.md.
/// </summary>
internal sealed class PadInput
{
    /// <summary>The game's actions the recap listens for, and what each means here.</summary>
    private static readonly (StringName Action, PadCommand Command)[] Actions =
    {
        (MegaInput.viewDeckAndTabLeft, PadCommand.TabPrevious),
        (MegaInput.viewExhaustPileAndTabRight, PadCommand.TabNext),
        (MegaInput.cancel, PadCommand.Close),
        (MegaInput.pauseAndBack, PadCommand.Close),
        (MegaInput.confirm, PadCommand.Save),
        (MegaInput.up, PadCommand.Up),
        (MegaInput.down, PadCommand.Down),
        (MegaInput.left, PadCommand.Left),
        (MegaInput.right, PadCommand.Right),
        (MegaInput.altUp, PadCommand.ScrollUp),
        (MegaInput.altDown, PadCommand.ScrollDown),
    };

    /// <summary>The left stick moves the selection too; the game keeps it out of its rebindable map.</summary>
    private static readonly (StringName Action, PadCommand Command)[] Stick =
    {
        (Controller.lStickUp, PadCommand.Up), (Controller.lStickDown, PadCommand.Down),
        (Controller.lStickLeft, PadCommand.Left), (Controller.lStickRight, PadCommand.Right),
    };

    private static bool _failed;

    /// <summary>Logs every command (dev preview).</summary>
    public static bool Diagnostics { get; set; }

    /// <summary>The last command acted on (dev preview checks).</summary>
    public static PadCommand LastCommand { get; private set; }

    /// <summary>Whether the game is in controller mode (it switches on the first controller press, back on mouse use).</summary>
    public static bool ControllerMode => NControllerManager.Instance?.IsUsingDirectionalNavigation ?? false;

    private readonly PanelHandle _panel;
    private readonly Dictionary<StringName, StringName> _controllerMap;
    private readonly PadCursor _cursor = new();
    private readonly Dictionary<PadCommand, (ulong Since, ulong Done)> _held = new();
    private int _cursorTab = -1;
    private Control? _previousFocus;

    private PadInput(PanelHandle panel, Dictionary<StringName, StringName> controllerMap)
    {
        _panel = panel;
        _controllerMap = controllerMap;
    }

    /// <summary>
    /// Starts listening on a newly opened panel. If the game's controller bindings can't be read, controller support is
    /// off for the session (one log line) and the recap works as before with the mouse and F8.
    /// </summary>
    public static void Attach(PanelHandle panel)
    {
        if (_failed) return;
        try
        {
            var map = NInputManager.Instance is NInputManager input
                ? AccessTools.Field(typeof(NInputManager), "_controllerInputMap")?.GetValue(input) as Dictionary<StringName, StringName>
                : null;
            if (map == null || map.Count == 0) throw new InvalidOperationException("the game's controller bindings couldn't be read");
            new PadInput(panel, new Dictionary<StringName, StringName>(map)).Start();
        }
        catch (Exception e)
        {
            _failed = true;
            Tracker.LogError("controller support (off; mouse and F8 still work)", e);
        }
    }

    private void Start()
    {
        Control root = _panel.Root;
        var tree = (SceneTree)Engine.GetMainLoop();
        _previousFocus = tree.Root.GuiGetFocusOwner();
        root.FocusMode = Control.FocusModeEnum.All;
        root.GuiInput += OnInput;
        root.FocusExited += OnFocusLost;
        _panel.Tabs.TabChanged += _ => ClearSelection();
        _panel.Live.On(_ => Refit());
        tree.ProcessFrame += OnFrame;

        Callable mouse = Callable.From(OnMouseMode);
        NControllerManager? controllers = NControllerManager.Instance;
        controllers?.Connect(NControllerManager.SignalName.MouseDetected, mouse);
        root.TreeExiting += () =>
        {
            tree.ProcessFrame -= OnFrame;
            if (controllers != null && GodotObject.IsInstanceValid(controllers) && controllers.IsConnected(NControllerManager.SignalName.MouseDetected, mouse))
                controllers.Disconnect(NControllerManager.SignalName.MouseDetected, mouse);
            GiveFocusBack();
        };
        Callable.From(Grab).CallDeferred(); // the panel's layer can still be on its way into the tree
    }

    // ------------------------------------------------------------------ focus

    private void Grab()
    {
        Control root = _panel.Root;
        if (GodotObject.IsInstanceValid(root) && root.IsInsideTree() && root.IsVisibleInTree() && !root.HasFocus()) root.GrabFocus();
    }

    /// <summary>
    /// The game takes focus away when it switches input mode (to its default control, or to nothing): take it back, so
    /// presses keep coming here and never reach the game underneath.
    /// </summary>
    private void OnFocusLost()
    {
        _held.Clear();
        if (GodotObject.IsInstanceValid(_panel.Root) && _panel.Root.IsInsideTree()) Callable.From(Grab).CallDeferred();
    }

    /// <summary>On close, focus goes back to whatever had it (the podium, or the victory screen's button).</summary>
    private void GiveFocusBack()
    {
        Control? previous = _previousFocus;
        if (previous != null && GodotObject.IsInstanceValid(previous) && previous.IsInsideTree() && previous.IsVisibleInTree())
            previous.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void OnMouseMode()
    {
        _held.Clear();
        ClearSelection();
    }

    // ------------------------------------------------------------------ input

    private void OnInput(InputEvent input)
    {
        if (input is InputEventMouse or InputEventScreenTouch or InputEventScreenDrag or InputEventGesture) return;
        _panel.Root.AcceptEvent();
        try
        {
            if (Translate(input) is not (PadCommand command, bool pressed)) return;
            if (!pressed)
            {
                _held.Remove(command);
                return;
            }
            if (_held.ContainsKey(command)) return; // a stick still pushed sends a stream of presses
            if (PadCommands.Repeats(command)) _held[command] = (Time.GetTicksMsec(), 0);
            Do(command);
        }
        catch (Exception e)
        {
            Tracker.LogError("controller input", e);
        }
    }

    /// <summary>What this event means in the recap and whether it's a press (true) or a release; null if nothing.</summary>
    private (PadCommand Command, bool Pressed)? Translate(InputEvent input)
    {
        // Game actions: keyboard shortcuts the game parsed, and anything already translated.
        foreach ((StringName action, PadCommand command) in Actions)
            if (Match(input, action) is bool pressed) return (command, pressed);
        // Raw controller buttons and sticks, through the player's own bindings.
        foreach ((StringName gameAction, StringName controllerAction) in _controllerMap)
            if (Match(input, controllerAction) is bool pressed && CommandFor(gameAction) is PadCommand command) return (command, pressed);
        foreach ((StringName action, PadCommand command) in Stick)
            if (Match(input, action) is bool pressed) return (command, pressed);
        // Keys the game binds to those actions (the view-deck key switches tabs, as on the game's own screens).
        if (input is InputEventKey key && !key.Echo && NInputManager.Instance is NInputManager manager)
            foreach ((StringName action, PadCommand command) in Actions)
                if (manager.GetCurrentHotkey(action) is Key hotkey && hotkey != Key.None && hotkey == key.Keycode) return (command, key.Pressed);
        return null;
    }

    private static PadCommand? CommandFor(StringName gameAction)
    {
        foreach ((StringName action, PadCommand command) in Actions)
            if (action == gameAction) return command;
        return null;
    }

    private static bool? Match(InputEvent input, StringName action)
    {
        if (input is InputEventAction named) return named.Action == action ? named.Pressed : null;
        if (!InputMap.HasAction(action)) return null;
        if (input.IsActionPressed(action)) return true;
        if (input.IsActionReleased(action)) return false;
        return null;
    }

    /// <summary>Held directions and scrolling repeat: after 400 ms, then every 80 ms.</summary>
    private void OnFrame()
    {
        if (_held.Count == 0) return;
        try
        {
            ulong now = Time.GetTicksMsec();
            foreach (PadCommand command in _held.Keys.ToList())
            {
                (ulong since, ulong done) = _held[command];
                ulong heldFor = now - since;
                _held[command] = (since, heldFor);
                for (int i = PadRepeat.Steps(done, heldFor); i > 0; i--) Do(command);
            }
        }
        catch (Exception e)
        {
            _held.Clear();
            Tracker.LogError("controller repeat", e);
        }
    }

    // ------------------------------------------------------------------ commands

    private void Do(PadCommand command)
    {
        LastCommand = command;
        if (Diagnostics) Tracker.Note($"controller: {command}");
        switch (command)
        {
            case PadCommand.TabPrevious:
            case PadCommand.TabNext:
                _panel.Tabs.CurrentTab = PadCursor.NextTab(_panel.Tabs.CurrentTab, command == PadCommand.TabNext ? 1 : -1, _panel.Tabs.GetTabCount());
                break;
            case PadCommand.Close:
                _held.Clear();
                _panel.Close();
                break;
            case PadCommand.Save:
                _panel.Save();
                break;
            case PadCommand.ScrollUp:
            case PadCommand.ScrollDown:
                CurrentPad()?.Scroll?.Invoke(command == PadCommand.ScrollDown ? 1 : -1);
                break;
            default:
                if (PadCommands.Direction(command) is PadDirection direction) Step(direction);
                break;
        }
    }

    private PadTab? CurrentPad()
    {
        int index = _panel.Tabs.CurrentTab;
        return index >= 0 && index < _panel.Pads.Count ? _panel.Pads[index] : null;
    }

    private void Step(PadDirection direction)
    {
        if (CurrentPad() is not PadTab pad) return;
        // Up and down switch rows on a tab with two; on the others they scroll its list.
        if (direction is PadDirection.Up or PadDirection.Down && pad.Rows.Count < 2)
        {
            pad.Scroll?.Invoke(direction == PadDirection.Down ? 1 : -1);
            return;
        }
        if (_cursorTab != _panel.Tabs.CurrentTab) ClearSelection();
        _cursorTab = _panel.Tabs.CurrentTab;
        int oldRow = _cursor.Row;
        if (!_cursor.Move(direction, pad.Shapes())) return;
        Show(pad, oldRow);
    }

    /// <summary>A live update changed the tab: keep the selection on the same item, or the nearest, and show it again.</summary>
    private void Refit()
    {
        if (!_cursor.Selected || _cursorTab < 0 || _cursorTab >= _panel.Pads.Count) return;
        PadTab pad = _panel.Pads[_cursorTab];
        int oldRow = _cursor.Row;
        _cursor.Fit(pad.Shapes());
        Show(pad, oldRow);
    }

    private void Show(PadTab pad, int oldRow)
    {
        if (oldRow >= 0 && oldRow != _cursor.Row && oldRow < pad.Rows.Count) pad.Rows[oldRow].Clear();
        if (_cursor.Selected && _cursor.Row < pad.Rows.Count) pad.Rows[_cursor.Row].Show(_cursor.Item);
    }

    private void ClearSelection()
    {
        if (_cursor.Selected && _cursorTab >= 0 && _cursorTab < _panel.Pads.Count && _cursor.Row < _panel.Pads[_cursorTab].Rows.Count)
            _panel.Pads[_cursorTab].Rows[_cursor.Row].Clear();
        _cursor.Clear();
        _cursorTab = -1;
    }
}
```

- [ ] **Step 2: Attach it to every opened panel**

In `RecapUi.ShowView`, after `EnsureLayer().AddChild(handle.Root);`:

```csharp
        PadInput.Attach(handle);
```

- [ ] **Step 3: Build and test**

Run the Release build → `Build succeeded`. Run the tests → `151/151 passed`.

- [ ] **Step 4: Commit**

```bash
git add src/WhoCarried/UI/PadInput.cs src/WhoCarried/UI/RecapUi.cs
git commit -m "Controller: the recap catches and uses controller presses"
```

---

### Task 4: Button hints

**Files:**
- Create: `src/WhoCarried/UI/PadHints.cs`
- Modify: `src/WhoCarried/UI/RecapPanel.cs` (`Create`, `TopBar`, `Nav`)

**Interfaces:**
- Consumes: `PadInput.ControllerMode` (Task 3).
- Produces: `PadHints { void Glyph(TextureRect, StringName action); void OnButton(Button, StringName action); void Attach(Control root); }`

- [ ] **Step 1: `src/WhoCarried/UI/PadHints.cs`**

```csharp
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The game's controller button icons on the recap (LB and RB by the tabs, B on Close, Y on Save image), shown only in
/// controller mode, in the controller's own style, and following rebinding. A missing glyph is left out.
/// </summary>
internal sealed class PadHints
{
    private readonly List<(TextureRect Rect, StringName Action)> _glyphs = new();
    private readonly List<(Button Button, StringName Action, Texture2D? MouseIcon)> _buttons = new();

    public void Glyph(TextureRect rect, StringName action) => _glyphs.Add((rect, action));

    /// <summary>In controller mode the button's icon becomes the glyph; in mouse mode it's its own again.</summary>
    public void OnButton(Button button, StringName action) => _buttons.Add((button, action, button.Icon));

    /// <summary>Shows the right hints now and follows the game's input mode and rebinding until the panel closes.</summary>
    public void Attach(Control root)
    {
        Update();
        Callable update = Callable.From(Update);
        var sources = new List<(GodotObject Source, StringName Signal)>();
        if (NControllerManager.Instance is NControllerManager controllers)
        {
            sources.Add((controllers, NControllerManager.SignalName.ControllerDetected));
            sources.Add((controllers, NControllerManager.SignalName.MouseDetected));
        }
        if (NInputManager.Instance is NInputManager input) sources.Add((input, NInputManager.SignalName.InputRebound));
        foreach ((GodotObject source, StringName signal) in sources) source.Connect(signal, update);
        root.TreeExiting += () =>
        {
            foreach ((GodotObject source, StringName signal) in sources)
                if (GodotObject.IsInstanceValid(source) && source.IsConnected(signal, update)) source.Disconnect(signal, update);
        };
    }

    private void Update()
    {
        try
        {
            bool pad = PadInput.ControllerMode;
            foreach ((TextureRect rect, StringName action) in _glyphs)
            {
                if (!GodotObject.IsInstanceValid(rect)) continue;
                rect.Texture = pad ? Icon(action) : null;
                rect.Visible = rect.Texture != null;
            }
            foreach ((Button button, StringName action, Texture2D? mouseIcon) in _buttons)
                if (GodotObject.IsInstanceValid(button)) button.Icon = (pad ? Icon(action) : null) ?? mouseIcon;
        }
        catch (Exception e)
        {
            Tracker.LogError("controller hints", e);
        }
    }

    private static Texture2D? Icon(StringName action) => NInputManager.Instance?.GetHotkeyIcon(action);
}
```

- [ ] **Step 2: Wire the hints in `RecapPanel`**

Add `using MegaCrit.Sts2.Core.ControllerInput;`. In `Create`, before `root.AddChild(TopBar(…))` add `var hints = new PadHints();`; pass `hints` into `TopBar` and `Nav` (new last parameter on each); after `stage.AddChild(Nav(k, tabs, hints));` add `hints.Attach(root);`.

In `TopBar` (new last parameter `PadHints hints`), after creating each button:

```csharp
        hints.OnButton(save, MegaInput.confirm);
        …
        hints.OnButton(close, MegaInput.cancel);
```

In `Nav` (new last parameter `PadHints hints`), after the `for` loop that places the tabs (`x` is then the right end of the last tab plus 30):

```csharp
        // LB and RB either side of the tabs, in controller mode only.
        float glyphY = (HandLayout.TabsBottom - top - 28) / 2;
        TextureRect previous = k.At(k.Pic(null, 28, 28), -34, glyphY);
        TextureRect next = k.At(k.Pic(null, 28, 28), x - 24, glyphY);
        foreach (TextureRect glyph in new[] { previous, next })
        {
            glyph.Visible = false;
            nav.AddChild(glyph);
        }
        hints.Glyph(previous, MegaInput.viewDeckAndTabLeft);
        hints.Glyph(next, MegaInput.viewExhaustPileAndTabRight);
```

- [ ] **Step 3: Build and test** (as before: `Build succeeded`, `151/151 passed`).

- [ ] **Step 4: Commit**

```bash
git add src/WhoCarried/UI/PadHints.cs src/WhoCarried/UI/RecapPanel.cs
git commit -m "Controller: the game's button hints on the recap"
```

---

### Task 5: The podium in the top bar's navigation

**Files:**
- Modify: `src/WhoCarried/UI/TopBarButton.cs`, `src/WhoCarried/Game/Patches.cs`, `src/WhoCarried/ModEntry.cs`

**Interfaces:**
- Consumes: `PadInput.ControllerMode` (Task 3); `RecapUi.Show()`.
- Produces: `TopBarButton.JoinNavigation(NTopBar bar)`; `TopBarNavigationPatch`.

- [ ] **Step 1: Focusable podium with the hover look, and A to open**

In `TopBarButton.Build`: `FocusMode = Control.FocusModeEnum.All` (was `None`). After the existing signal hookups add:

```csharp
        // Controller: the top bar's d-pad navigation reaches it (JoinNavigation); focused looks like hovered.
        button.FocusEntered += () => { if (PadInput.ControllerMode) behaviour.Enter(); };
        button.FocusExited += behaviour.Leave;
```

In `Behaviour.Input`, before the mouse check:

```csharp
            if (input.IsActionPressed(MegaInput.select))
            {
                button.AcceptEvent();
                Leave();
                RecapUi.Show();
                return;
            }
```

(add `using MegaCrit.Sts2.Core.ControllerInput;`). Replace the `<summary>` line about keyboard and controller in the class doc if present; the class doc becomes "…click (or select it with the controller and press A) to open the recap…".

At the end of `AddTo`, before `return button;`, add `JoinNavigation(bar);`.

- [ ] **Step 2: `JoinNavigation`**

```csharp
    private static bool _navigationFailed;

    /// <summary>
    /// Puts the podium at the right-hand end of the top bar's controller navigation, after the game has linked the
    /// rest (it relinks whenever potion slots, modifiers or the screen change). Left goes back to where the chain
    /// ended, down to the relics like the other top-bar items.
    /// </summary>
    public static void JoinNavigation(NTopBar bar)
    {
        if (_navigationFailed) return;
        try
        {
            if (bar.Map.GetParent()?.GetNodeOrNull<Control>(NodeName) is not Control podium) return;
            NodePath below = bar.Hp.FocusNeighborBottom;
            if (below.IsEmpty) return; // not linked yet (no relics on screen)
            Control end = ChainEnd(bar);
            end.FocusNeighborRight = podium.GetPath();
            podium.FocusNeighborLeft = end.GetPath();
            podium.FocusNeighborRight = podium.GetPath();
            podium.FocusNeighborTop = podium.GetPath();
            podium.FocusNeighborBottom = below;
        }
        catch (Exception e)
        {
            _navigationFailed = true;
            Tracker.LogError("top bar navigation (the podium stays mouse-only)", e);
        }
    }

    /// <summary>The last item in the game's chain: the last modifier, else the boss icon if it can take focus, else the floor.</summary>
    private static Control ChainEnd(NTopBar bar)
    {
        if (Traverse.Create(bar).Field<Control>("_modifiersContainer").Value is Control modifiers && modifiers.Visible
            && modifiers.GetChildren().OfType<Control>().LastOrDefault() is Control last)
            return last;
        return bar.BossIcon.IsVisible() && bar.BossIcon.FocusMode != Control.FocusModeEnum.None ? bar.BossIcon : bar.FloorIcon;
    }
```

- [ ] **Step 3: The patch**

In `Patches.cs`, after `TopBarPatch`:

```csharp
/// <summary>The game relinks the top bar's controller navigation on every change: keep the podium at its end.</summary>
[HarmonyPatch(typeof(NTopBar), "UpdateNavigation")]
internal static class TopBarNavigationPatch
{
    private static void Postfix(NTopBar __instance)
    {
        try { TopBarButton.JoinNavigation(__instance); }
        catch (Exception e) { Tracker.LogError("NTopBar.UpdateNavigation", e); }
    }
}
```

In `ModEntry.PatchClasses`, add `typeof(TopBarNavigationPatch),` after `typeof(TopBarPatch),` (13 patches).

- [ ] **Step 4: Build and test** (`Build succeeded`, `151/151 passed`).

- [ ] **Step 5: Commit**

```bash
git add src/WhoCarried/UI/TopBarButton.cs src/WhoCarried/Game/Patches.cs src/WhoCarried/ModEntry.cs
git commit -m "Controller: reach the podium from the top bar"
```

---

### Task 6: Dev preview checks

**Files:**
- Modify: `src/WhoCarried/UI/DevPreview.cs`
- Scratchpad (untracked): `cycle.ps1` waits for `preview-15-podium-focus.png`.

**Interfaces:**
- Consumes: `PadInput.Diagnostics`, `PadInput.LastCommand`, `PadInput.ControllerMode`, `TopBarButton.AddTo`, `RecapUi.ShowView`, `TimelineTab`.

- [ ] **Step 1: Chain the controller checks after the top bar's**

In `Run`, `CaptureTopBar(dataDir, () => Tracker.Note("preview done"));` becomes:

```csharp
                        CaptureTopBar(dataDir, () => CapturePad(dataDir, sample, () => Tracker.Note("preview done")));
```

- [ ] **Step 2: `CapturePad`**

```csharp
    /// <summary>
    /// Presses controller buttons the way a pad would (raw joypad events, through the game's bindings): RB to the next
    /// tab, the d-pad on the scoreboard's cards, a held left on the Timeline, B to close, then the podium selected on
    /// the top bar. Logs what each press did and whether the game is in controller mode (it only switches while the
    /// game window has focus).
    /// </summary>
    private static void CapturePad(string dataDir, Sample sample, Action done)
    {
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        PadInput.Diagnostics = true;
        PanelHandle handle = RecapUi.ShowView(sample.View, sample.Icons, new CardVisuals(sample.CardFor));
        void Press(JoyButton button, bool down = true) => Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = down });
        void Tap(JoyButton button)
        {
            Press(button);
            Press(button, down: false);
        }
        void Shot(string name) => root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, name));

        Later.Run(1.0, () =>
        {
            Tap(JoyButton.DpadDown); // the first press after the mouse switches the game to controller mode
            Tracker.Note($"preview pad: controller mode {PadInput.ControllerMode}, focus on {root.GuiGetFocusOwner()?.Name}");
            Tap(JoyButton.RightShoulder);
            Tracker.Note($"preview pad: RB -> {PadInput.LastCommand}, tab {handle.Tabs.CurrentTab}");
        });
        Later.Run(2.0, () =>
        {
            Shot("preview-12-pad-tab.png");
            Tap(JoyButton.LeftShoulder);
            Tap(JoyButton.DpadRight);
            Tap(JoyButton.DpadRight);
            Tracker.Note($"preview pad: back to tab {handle.Tabs.CurrentTab}, last {PadInput.LastCommand}");
        });
        Later.Run(3.0, () =>
        {
            Shot("preview-13-pad-card.png");
            handle.Tabs.CurrentTab = 4;
            Press(JoyButton.DpadLeft); // held: one step, then repeats from 400 ms
        });
        Later.Run(3.7, () => Press(JoyButton.DpadLeft, down: false));
        Later.Run(4.4, () =>
        {
            Shot("preview-14-pad-fight.png");
            Tap(JoyButton.B);
            Tracker.Note($"preview pad: B -> {PadInput.LastCommand}, recap open {GodotObject.IsInstanceValid(handle.Root) && !handle.Root.IsQueuedForDeletion()}");
            CapturePodiumFocus(dataDir, done);
        });
    }

    /// <summary>The podium selected with the controller: grown, brightened, with its tooltip.</summary>
    private static void CapturePodiumFocus(string dataDir, Action done)
    {
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;
        var layer = new CanvasLayer { Layer = 101, Name = "WhoCarriedPreviewTopBarFocus" };
        root.AddChild(layer);
        NTopBar bar = ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        layer.AddChild(bar);
        Control? button = TopBarButton.AddTo(bar);
        Later.Run(0.8, () =>
        {
            button?.GrabFocus();
            Tracker.Note($"preview pad: podium focused {button?.HasFocus()}, controller mode {PadInput.ControllerMode}, " +
                         $"left neighbour {button?.FocusNeighborLeft}");
            Later.Run(0.8, () =>
            {
                root.GetTexture().GetImage().SavePng(Path.Combine(dataDir, "preview-15-podium-focus.png"));
                layer.QueueFree();
                PadInput.Diagnostics = false;
                done();
            });
        });
    }
```

- [ ] **Step 3: Build and test** (`Build succeeded`, `151/151 passed`). Commit:

```bash
git add src/WhoCarried/UI/DevPreview.cs
git commit -m "Dev preview: controller checks"
```

- [ ] **Step 4: Run the preview (only with the game closed and the owner's go-ahead)**

`cycle.ps1` in the session scratchpad: change the file it waits for to `preview-15-podium-focus.png`, then run `powershell -Command "& '<scratchpad>\cycle.ps1' preview"`. Read `preview-12…15` and the `preview pad:` / `controller:` lines in `data\events.log`. Expected:
- 12: Awards tab showing, LB/RB glyphs by the tabs, B on Close, Y on Save (if controller mode switched);
- 13: the scoreboard with the second card lifted;
- 14: Timeline readout about four fights before the last (the press selects the latest fight, then repeats at 400, 480, 560 and 640 ms each step left; timers can shift it by one);
- 15: podium grown with its tooltip;
- log: `RB -> TabNext, tab 1`, `recap open False` after B, `controller mode True` (or False with a note if the window wasn't focused).

If raw joypad presses don't arrive in the panel's `GuiInput` (no `controller:` lines), stop and revisit the input path (fallback: Harmony prefixes on `NInputManager._UnhandledInput` and `NHotkeyManager._UnhandledInput` that route to `PadInput` while the recap is open).

---

### Task 7: Docs and finish

- [ ] **Step 1: Spec touch-ups** (`docs/design/specs/2026-09-13-controller-support-design.md`):
  - Getting in: "Closing the recap gives focus back to whatever had it: the podium when it opened the recap, the victory or defeat screen's button at the end of a run."
  - While open: add "The first controller press after using the mouse only switches the game to controller mode; the game eats that press everywhere, and takes focus, which the recap takes straight back."
  - Testing: the preview "logs what each press did", instead of "logs that LB didn't reach the game".
- [ ] **Step 2:** `docs/README.md`: the Controller support row's plan cell links `design/plans/2026-09-13-controller-support.md`.
- [ ] **Step 3:** Full test run and Release build; commit docs.
- [ ] **Step 4:** After the preview passes and with the game closed: squash `feature/controller-support` into `main` (one commit, "Controller support for the recap"), delete the branch, deploy with `tools\deploy.ps1`. Don't push.
- [ ] **Step 5:** Ask the owner to try it with a real controller (and a Steam Deck if possible): X then d-pad right to the podium, A to open, LB/RB, d-pad on the scoreboard and Timeline, right stick on Sources, Y to save, B to close.
