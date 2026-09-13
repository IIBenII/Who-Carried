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
