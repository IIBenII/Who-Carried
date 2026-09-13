using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Owns the overlay: F8 polling, the canvas layer, opening on the end-of-run screen, image export, and keeping the
/// open recap live (refreshed in place when the stats change, and every couple of seconds for floor, deck and HP).
/// </summary>
internal static class RecapUi
{
    private const double RefreshDelay = 0.25;
    private const double IdleRefresh = 1.5;

    /// <summary>The key that toggles the recap, as the top-bar tooltip names it.</summary>
    public const string HotkeyName = "F8";

    private static CanvasLayer? _layer;
    private static Control? _panel;
    private static CardVisuals? _cards;
    private static bool _f8WasDown;

    private static Live? _live;
    private static IRunState? _liveRun;
    private static RecapView? _currentView;
    private static bool _refreshPending;
    private static int _generation;

    public static void Install()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += OnFrame;
            Tracker.Changed += OnStatsChanged;
            DevPreview.StartIfFlagged(Tracker.DataDir);
            Replay.StartIfFlagged(Tracker.DataDir);
        }
        else
        {
            Log.Warn("[WhoCarried] no SceneTree at init; F8 toggle unavailable");
        }
    }

    public static void Toggle()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) Hide();
        else Show();
    }

    /// <summary>Opens the recap for the current (or just-finished) run and keeps it live.</summary>
    public static void Show()
    {
        IRunState? run = Tracker.CurrentRun;
        if (run == null) return;
        ShowView(BuildView(run), GameReader.WithPowerIcons(id => GameReader.CharacterIcon(run, id)),
            new CardVisuals((playerId, cardId) => GameReader.DeckCardModel(run, playerId, cardId)));
        _liveRun = run;
        int generation = _generation;
        IdleTick(generation);
    }

    public static PanelHandle ShowView(RecapView view, Func<string?, Texture2D?> icons, CardVisuals? cards)
    {
        Hide();
        _cards = cards;
        _currentView = view;
        PanelHandle handle = RecapPanel.Create(view, icons, cards, Hide, h => Export(_currentView ?? view, icons, h));
        _panel = handle.Root;
        _live = handle.Live;
        EnsureLayer().AddChild(handle.Root);
        PadInput.Attach(handle);
        return handle;
    }

    /// <summary>Pushes a newer view into the open recap (used by the dev preview's live check).</summary>
    public static void Apply(RecapView view)
    {
        if (_live == null || _panel == null || !GodotObject.IsInstanceValid(_panel)) return;
        _currentView = view;
        _live.Apply(view);
    }

    /// <summary>Closes the panel. Cards go back to the game's pool first, before their slots are freed.</summary>
    public static void Hide()
    {
        _generation++;
        _live = null;
        _liveRun = null;
        _currentView = null;
        _cards?.ReleaseAll();
        _cards = null;
        if (_panel != null && GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
        _panel = null;
    }

    /// <summary>Called after the victory/defeat screen is ready: open the recap once its banner has animated in.</summary>
    public static void OnGameOverScreen(NGameOverScreen screen)
    {
        screen.TreeExiting += Hide;
        Later.Run(1.5, () =>
        {
            if (GodotObject.IsInstanceValid(screen) && screen.IsInsideTree()) Show();
        });
    }

    private static RecapView BuildView(IRunState run)
    {
        RunStats stats = Tracker.Stats;
        bool? victory = stats.Finished ? stats.Victory : null;
        return RecapBuilder.Build(stats, GameReader.Players(run), GameReader.Defense(run),
            GameReader.Header(run, victory), victory, GameReader.Decks(run), GameReader.BadgeText, GameReader.Facts(run));
    }

    /// <summary>Stats changed: refresh shortly, at most once per <see cref="RefreshDelay"/>, however many hits land.</summary>
    private static void OnStatsChanged()
    {
        if (_live == null || _liveRun == null || _refreshPending) return;
        _refreshPending = true;
        Later.Run(RefreshDelay, () =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    /// <summary>Floor, deck and HP changes don't raise events; a slow tick picks them up while the recap is open.</summary>
    private static void IdleTick(int generation)
    {
        Later.Run(IdleRefresh, () =>
        {
            if (generation != _generation) return;
            Refresh();
            IdleTick(generation);
        });
    }

    private static void Refresh()
    {
        if (_liveRun == null) return;
        Apply(BuildView(_liveRun));
    }

    private static void Export(RecapView view, Func<string?, Texture2D?> icons, PanelHandle handle)
    {
        string result = view.Victory switch { true => "victory", false => "defeat", null => "in-progress" };
        string path = Path.Combine(PngExporter.Folder, $"run-{DateTime.Now:yyyy-MM-dd_HHmm}-{result}.png");
        handle.Status.Text = "Saving...";
        PngExporter.Save(SummaryCard.Create(view, icons), SummaryCard.Width, path, error =>
        {
            Tracker.Note(error == null ? $"exported {path}" : $"export failed: {error}");
            if (GodotObject.IsInstanceValid(handle.Status))
                handle.Status.Text = error == null ? $"Saved to Pictures\\{PngExporter.FolderName}" : "Couldn't save the image";
        });
    }

    private static void OnFrame()
    {
        try
        {
            bool down = Input.IsKeyPressed(Key.F8);
            if (down && !_f8WasDown) Toggle();
            _f8WasDown = down;
        }
        catch (Exception e)
        {
            Tracker.LogError("F8", e);
        }
    }

    private static CanvasLayer EnsureLayer()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return _layer;
        _layer = new CanvasLayer { Layer = 100, Name = "WhoCarriedLayer" };
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _layer);
        return _layer;
    }
}
