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
