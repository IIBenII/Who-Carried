using System.Reflection;
using HarmonyLib;

namespace GenericAttribution;

// SPIKE ONLY. Tests scope mechanics against Harmony; this is not game integration.
internal static class AttributionProbe
{
    private static readonly AsyncLocal<Frame?> Effects = new();
    private static readonly AsyncLocal<Frame?> Damages = new();
    private static int _generation;

    internal sealed class Frame(Effect? source, Frame? previous)
    {
        public readonly Effect? Source = source;
        public readonly Frame? Previous = previous;
        private readonly int _born = Volatile.Read(ref _generation);
        private Task? _task;
        private bool _invoking = true;

        public bool Active => _born == Volatile.Read(ref _generation)
            && (Volatile.Read(ref _invoking) || _task is { IsCompleted: false });

        public void Returned(Task? task)
        {
            _task = task;
            Volatile.Write(ref _invoking, false);
        }
    }

    // Never walk past an inactive top frame: it would credit a detached child to its parent.
    public static Effect? EffectSource => Effects.Value is { Active: true } frame ? frame.Source : null;
    public static Effect? DamageSource => Damages.Value is { Active: true } frame ? frame.Source : null;
    public static void NewCombat() => Interlocked.Increment(ref _generation);

    public static void Install()
    {
        var harmony = new Harmony("whocarried.spike.generic-attribution");
        var targets = HookDiscovery.Find(typeof(Effect), typeof(Effect).Assembly.GetTypes());
        foreach (MethodInfo method in targets)
            harmony.Patch(method, prefix: Patch(nameof(EnterEffect)), finalizer: Patch(nameof(LeaveEffect)));
        harmony.Patch(typeof(Damage).GetMethod(nameof(Damage.Deal))!,
            prefix: Patch(nameof(EnterDamage)), finalizer: Patch(nameof(LeaveDamage)));
        Console.WriteLine($"PATCHED {targets.Length} effect hook implementations + 1 damage boundary; Harmony {typeof(Harmony).Assembly.GetName().Version}");
    }

    private static HarmonyMethod Patch(string name) => new(typeof(AttributionProbe).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!);

    private static void EnterEffect(Effect __instance, out Frame __state)
    {
        __state = new Frame(__instance, Effects.Value);
        Effects.Value = __state;
    }

    private static void LeaveEffect(Frame __state, Task? __result)
    {
        // An async method captures the frame for its continuations. Restore the caller immediately
        // when its Task is returned; retain the original Task as the frame's lifetime boundary.
        __state.Returned(__result);
        Effects.Value = __state.Previous;
    }

    private static void EnterDamage(out Frame __state)
    {
        __state = new Frame(EffectSource, Damages.Value);
        Damages.Value = __state;
    }

    private static void LeaveDamage(Frame __state, Task? __result)
    {
        __state.Returned(__result);
        Damages.Value = __state.Previous;
    }
}
