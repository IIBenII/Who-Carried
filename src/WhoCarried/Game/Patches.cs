using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.UI;

namespace WhoCarried.Game;

// Parameter names must match the game's method parameters exactly (Harmony binds by name).

/// <summary>Hook.AfterDamageGiven fires for every damage result, including killing blows.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
internal static class AfterDamageGivenPatch
{
    private static void Prefix(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult results,
                               Creature target, CardModel? cardSource)
    {
        try { Tracker.OnDamage(choiceContext, dealer, results, target, cardSource); }
        catch (Exception e) { Tracker.LogError("AfterDamageGiven", e); }
    }
}

/// <summary>
/// Fires once per target with the hit's final damage, just before block: where Vulnerable's boost and Weak's
/// reduction are measured.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
internal static class BeforeDamageReceivedPatch
{
    private static void Prefix(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        try { Tracker.OnBeforeDamage(target, amount, props, dealer, cardSource); }
        catch (Exception e) { Tracker.LogError("BeforeDamageReceived", e); }
    }
}

/// <summary>Fires before a creature gains block (real gains only, not previews): where Frail's cost is measured.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeBlockGained))]
internal static class BeforeBlockGainedPatch
{
    private static void Prefix(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        try { Tracker.OnBeforeBlock(creature, amount, props, cardSource); }
        catch (Exception e) { Tracker.LogError("BeforeBlockGained", e); }
    }
}

/// <summary>Fires for each card created mid-fight (Souls, Shivs, transformed cards) with the player who made it.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
internal static class CardGeneratedPatch
{
    private static void Prefix(CardModel card, Player? creator)
    {
        try { Tracker.OnCardCreated(card, creator); }
        catch (Exception e) { Tracker.LogError("AfterCardGeneratedForCombat", e); }
    }
}

/// <summary>Fires after any power's stacks change (new application or stacking), with the amount that actually landed.</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
internal static class AfterPowerAmountChangedPatch
{
    private static void Prefix(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier,
                               CardModel? cardSource)
    {
        try { Tracker.OnPowerChanged(choiceContext, power, amount, applier, cardSource); }
        catch (Exception e) { Tracker.LogError("AfterPowerAmountChanged", e); }
    }
}

/// <summary>Doom kills never raise AfterDamageGiven: the creature's remaining HP goes with a direct kill.</summary>
[HarmonyPatch(typeof(DoomPower), nameof(DoomPower.DoomKill))]
internal static class DoomKillPatch
{
    private static void Prefix(IReadOnlyList<Creature> creatures)
    {
        try { Tracker.OnDoomKill(creatures); }
        catch (Exception e) { Tracker.LogError("DoomKill", e); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
internal static class BeforeCombatStartPatch
{
    private static void Prefix(IRunState runState, ICombatState? combatState)
    {
        try { Tracker.OnCombatStart(runState, combatState); }
        catch (Exception e) { Tracker.LogError("BeforeCombatStart", e); }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
internal static class AfterCombatEndPatch
{
    private static void Prefix(IRunState runState)
    {
        try { Tracker.OnCombatEnd(runState); }
        catch (Exception e) { Tracker.LogError("AfterCombatEnd", e); }
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class RunEndedPatch
{
    private static void Postfix(bool isVictory, SerializableRun __result)
    {
        try { Tracker.OnRunEnded(isVictory, __result); }
        catch (Exception e) { Tracker.LogError("RunManager.OnEnded", e); }
    }
}

[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
internal static class GameOverScreenPatch
{
    private static void Postfix(NGameOverScreen __instance)
    {
        try { RecapUi.OnGameOverScreen(__instance); }
        catch (Exception e) { Tracker.LogError("NGameOverScreen._Ready", e); }
    }
}

/// <summary>The top bar is set up once per run, solo or co-op: add the recap button next to Map.</summary>
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class TopBarPatch
{
    private static void Postfix(NTopBar __instance)
    {
        try { TopBarButton.AddTo(__instance); }
        catch (Exception e) { Tracker.LogError("NTopBar.Initialize", e); }
    }
}
