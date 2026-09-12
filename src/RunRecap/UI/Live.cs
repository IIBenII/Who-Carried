using Godot;
using RunRecap.Core;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>
/// Keeps an open recap in step with the run. Widgets register how to update themselves; each refresh hands them the
/// newest view. Nothing is rebuilt: numbers count to their new values, bars slide, rows appear and reorder.
/// </summary>
internal sealed class Live
{
    private readonly List<Action<RecapView>> _updaters = new();

    public void On(Action<RecapView> update) => _updaters.Add(update);

    public void Apply(RecapView view)
    {
        foreach (Action<RecapView> update in _updaters)
        {
            try { update(view); }
            catch (Exception e) { Tracker.LogError("live update", e); }
        }
    }
}

/// <summary>Short eased transitions for live changes. One running tween per node and property.</summary>
internal static class Anim
{
    public const double Time = 0.45;
    private static readonly Dictionary<(ulong, string), Tween> Running = new();

    public static void To(Node node, string property, Variant value)
    {
        if (!node.IsInsideTree())
        {
            node.Set(property, value);
            return;
        }
        var key = (node.GetInstanceId(), property);
        if (Running.TryGetValue(key, out Tween? old) && GodotObject.IsInstanceValid(old)) old.Kill();
        Tween tween = node.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(node, property, value, Time);
        tween.Finished += () => Running.Remove(key);
        Running[key] = tween;
    }

    /// <summary>Runs <paramref name="step"/> with a value going from 0 to 1 (for custom-drawn charts).</summary>
    public static Tween Progress(Node node, Action<float> step)
    {
        Tween tween = node.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenMethod(Callable.From(step), 0f, 1f, Time);
        return tween;
    }
}

/// <summary>A number that counts to its new value. Optionally shrinks to fit a width and crops to digit height.</summary>
internal sealed class LiveNumber
{
    private readonly Label _label;
    private readonly Control? _holder;
    private int _value;
    private Tween? _tween;

    public LiveNumber(Label label, int value, bool tight = false, float maxWidth = 0, int minSize = 0)
    {
        _label = label;
        _value = value;
        MaxWidth = maxWidth;
        MinSize = minSize;
        label.Text = Kit.Num(value);
        _fullSize = label.GetThemeFontSize("font_size");
        if (maxWidth > 0) Kit.FitScaled(label, maxWidth, minSize);
        if (tight) _holder = Kit.Tight(label);
    }

    private readonly int _fullSize;

    public Control Control => _holder ?? _label;

    public Label Label => _label;

    public float MaxWidth { get; set; }

    public int MinSize { get; set; }

    public void Set(int value)
    {
        if (value == _value) return;
        int from = _value;
        _value = value;
        // Size everything for the final text first, so the layout settles once instead of every frame.
        _label.Text = Kit.Num(value);
        if (MaxWidth > 0)
        {
            _label.AddThemeFontSizeOverride("font_size", _fullSize);
            Kit.FitScaled(_label, MaxWidth, MinSize);
        }
        if (_holder != null) Kit.Retight(_label, _holder);
        if (!_label.IsInsideTree()) return;
        _tween?.Kill();
        _tween = _label.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenMethod(Callable.From<int>(v => _label.Text = Kit.Num(v)), from, value, Anim.Time);
    }
}

/// <summary>A square-ended bar whose fill slides to a new length.</summary>
internal sealed class LiveBar
{
    private readonly ColorRect _fill;
    private readonly float _width;
    private readonly float _height;
    private double _fraction;

    public LiveBar(double fraction, Color color, float width, float height)
    {
        _width = width;
        _height = height;
        _fraction = Math.Clamp(fraction, 0, 1);
        var track = new ColorRect { Color = RecapTheme.Track, CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fill = new ColorRect { Color = color, Size = new Vector2(FillWidth(_fraction), height), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddChild(_fill);
        Control = Kit.Center(track);
    }

    public Control Control { get; }

    public void Set(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (Math.Abs(fraction - _fraction) < 0.0005) return;
        _fraction = fraction;
        Anim.To(_fill, "size", new Vector2(FillWidth(fraction), _height));
    }

    private float FillWidth(double fraction) => fraction <= 0 ? 0 : Math.Max(2, (float)(_width * fraction));
}

/// <summary>
/// Keeps a container's children in step with a list: adds a row for each new key, updates existing rows in place,
/// removes rows whose key disappeared, and orders rows like the list (after <paramref name="offset"/> fixed children).
/// </summary>
internal sealed class KeyedRows<T>
{
    private readonly Container _box;
    private readonly Func<T, string> _key;
    private readonly Func<T, (Control Row, Action<T> Update)> _make;
    private readonly int _offset;
    private readonly Dictionary<string, (Control Row, Action<T> Update)> _rows = new();

    public KeyedRows(Container box, Func<T, string> key, Func<T, (Control Row, Action<T> Update)> make, int offset = 0)
    {
        _box = box;
        _key = key;
        _make = make;
        _offset = offset;
    }

    public int Count => _rows.Count;

    public void Sync(IEnumerable<T> items)
    {
        var seen = new HashSet<string>();
        int index = 0;
        foreach (T item in items)
        {
            string key = _key(item);
            if (!seen.Add(key)) continue;
            if (_rows.TryGetValue(key, out (Control Row, Action<T> Update) row))
            {
                row.Update(item);
            }
            else
            {
                row = _make(item);
                _rows[key] = row;
                _box.AddChild(row.Row);
            }
            _box.MoveChild(row.Row, Math.Min(_offset + index, _box.GetChildCount() - 1));
            index++;
        }
        foreach (string gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Control row = _rows[gone].Row;
            _box.RemoveChild(row);
            row.QueueFree();
            _rows.Remove(gone);
        }
    }
}
