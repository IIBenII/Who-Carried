using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using RunRecap.Game;
using RunRecap.UI;

namespace RunRecap;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Version = "0.1.0";

    private static readonly Type[] PatchClasses =
    {
        typeof(AfterDamageGivenPatch),
        typeof(DoomKillPatch),
        typeof(AfterPowerAmountChangedPatch),
        typeof(BeforeDamageReceivedPatch),
        typeof(BeforeBlockGainedPatch),
        typeof(CardGeneratedPatch),
        typeof(BeforeCombatStartPatch),
        typeof(AfterCombatEndPatch),
        typeof(RunEndedPatch),
        typeof(GameOverScreenPatch),
        typeof(DamageCommandPatch),
    };

    public static void Initialize()
    {
        string modDir = Path.GetDirectoryName(typeof(ModEntry).Assembly.Location) ?? ".";
        Tracker.Init(modDir);

        var harmony = new Harmony("runrecap");
        int applied = 0;
        foreach (Type patchClass in PatchClasses)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                applied++;
            }
            catch (Exception e)
            {
                Log.Error($"[RunRecap] patch {patchClass.Name} failed: {e.Message}");
            }
        }

        RunManager.Instance.RunStarted += run =>
        {
            try { Tracker.OnRunStarted(run); }
            catch (Exception e) { Tracker.LogError("RunStarted", e); }
        };

        RecapUi.Install();
        Log.Info($"[RunRecap] loaded v{Version}: {applied}/{PatchClasses.Length} patches applied");
    }
}
