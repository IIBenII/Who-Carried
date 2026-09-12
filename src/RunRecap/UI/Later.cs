using Godot;
using RunRecap.Game;

namespace RunRecap.UI;

internal static class Later
{
    /// <summary>Runs <paramref name="action"/> after a real-time delay on the main thread. Errors are logged, never thrown.</summary>
    public static void Run(double seconds, Action action)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () =>
        {
            try { action(); }
            catch (Exception e) { Tracker.LogError("Later", e); }
        };
    }
}
