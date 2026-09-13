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
