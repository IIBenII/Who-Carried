using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Experimental, off unless settings.json has "experimentalEffectSources": true. Some effects deal damage with no
/// dealer, no card and nothing on the action stack (a modded power ticking at the start of a turn), so the hit arrives
/// with nothing to credit. This watches the game's turn-start and turn-end hooks: as a piece of content's hook runs,
/// the live instance is the running effect (<see cref="EffectScopes"/>), and each damage command pins the effect that
/// started it. Nothing names a mod: the hooks are found through the game's own model list and base class.
/// Installed once, when the first run starts, so every mod's content is registered by then.
/// </summary>
internal static class EffectSources
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly EffectScopes Scopes = new();
    private static bool _tried;

    public static bool Enabled { get; private set; }

    /// <summary>The live effect that started the damage command running here; null when off or none was seen.</summary>
    public static AbstractModel? DamageSource => Enabled ? Scopes.DamageSource as AbstractModel : null;

    public static void NewFight()
    {
        if (Enabled) Scopes.NewFight();
    }

    public static void InstallOnce(string dataDir)
    {
        if (_tried) return;
        _tried = true;
        (Settings settings, _) = Settings.Load(dataDir);
        if (!settings.ExperimentalEffectSources) return;

        var clock = Stopwatch.StartNew();
        var harmony = new Harmony("whocarried.effects");
        List<MethodInfo> hooks = TurnHooks(Models.Types());
        int watched = Patch(harmony, hooks, nameof(EnterEffect), nameof(LeaveEffect));
        List<MethodInfo> commands = typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(CreatureCmd.Damage)).ToList();
        int pinned = Patch(harmony, commands, nameof(EnterDamage), nameof(LeaveDamage));
        // Without the damage command, nothing would ever read a watched hook.
        Enabled = pinned > 0;
        Tracker.Note($"effect sources: watching {watched}/{hooks.Count} turn hooks, {pinned}/{commands.Count} damage commands, " +
                     $"in {clock.ElapsedMilliseconds} ms{(Enabled ? "" : "; OFF: no damage command")}");
    }

    /// <summary>
    /// Every turn-start or turn-end hook some content overrides, where it's declared: an override a mod's shared base
    /// class makes once is one method, whichever content inherits it.
    /// </summary>
    private static List<MethodInfo> TurnHooks(IEnumerable<Type> types)
    {
        var found = new HashSet<MethodInfo>();
        foreach (Type type in types)
        {
            if (type.IsAbstract || !typeof(AbstractModel).IsAssignableFrom(type)) continue;
            try
            {
                foreach (MethodInfo method in type.GetMethods(Instance))
                {
                    if (method.IsAbstract || !method.IsVirtual || method.ContainsGenericParameters) continue;
                    if (method.ReturnType != typeof(Task) || method.DeclaringType == typeof(AbstractModel)) continue;
                    if (!EffectScopes.IsTurnHook(method.Name) || method.GetBaseDefinition().DeclaringType != typeof(AbstractModel)) continue;
                    found.Add(Declared(method));
                }
            }
            catch (Exception e)
            {
                Tracker.Note($"effect sources: can't look at {type.FullName}: {e.Message}");
            }
        }
        return found.ToList();
    }

    /// <summary>Harmony won't patch an inherited method reflected through a subclass: go back to where it's declared.</summary>
    private static MethodInfo Declared(MethodInfo method) =>
        method.DeclaringType!.GetMethods(Instance | BindingFlags.DeclaredOnly).Single(d => d.MetadataToken == method.MetadataToken);

    private static int Patch(Harmony harmony, IEnumerable<MethodInfo> methods, string prefix, string finalizer)
    {
        var enter = new HarmonyMethod(typeof(EffectSources).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static));
        var leave = new HarmonyMethod(typeof(EffectSources).GetMethod(finalizer, BindingFlags.NonPublic | BindingFlags.Static));
        int patched = 0, failed = 0;
        foreach (MethodInfo method in methods)
        {
            try
            {
                harmony.Patch(method, prefix: enter, finalizer: leave);
                patched++;
            }
            catch (Exception e)
            {
                if (++failed <= 5) Tracker.Note($"effect sources: can't watch {method.DeclaringType?.FullName}.{method.Name}: {e.Message}");
            }
        }
        return patched;
    }

    private static void EnterEffect(AbstractModel __instance, out EffectScopes.Frame? __state) => __state = Scopes.EnterEffect(__instance);

    private static void LeaveEffect(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveEffect(__state, __result);

    private static void EnterDamage(out EffectScopes.Frame? __state) => __state = Scopes.EnterDamage();

    private static void LeaveDamage(EffectScopes.Frame? __state, Task? __result) => Scopes.LeaveDamage(__state, __result);

    /// <summary>Tells two instances of the same content apart in the log.</summary>
    public static string InstanceId(AbstractModel effect) => $"{effect.Id.Entry} #{RuntimeHelpers.GetHashCode(effect):x}";
}
