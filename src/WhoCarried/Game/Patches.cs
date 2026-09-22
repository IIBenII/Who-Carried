using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
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

/// <summary>
/// The game's damage calculation, run for previews as well as real hits. For a real hit (every modifier, no preview)
/// this remembers the damage it started from, which BeforeDamageReceived doesn't pass: the result is floored at zero,
/// so it's the only way to tell how big a hit was that Strength loss took to nothing.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class ModifyDamagePatch
{
    private static void Postfix(Creature? target, Creature? dealer, decimal damage, ValueProp props,
                                ModifyDamageHookType modifyDamageHookType, CardPreviewMode previewMode, decimal __result)
    {
        if (target == null || modifyDamageHookType != ModifyDamageHookType.All || previewMode != CardPreviewMode.None) return;
        try { DebuffBonusTracker.OnDamageCalculated(target, dealer, damage, props, __result); }
        catch (Exception e) { Tracker.LogError("ModifyDamage", e); }
    }
}

/// <summary>
/// The game's HP-loss calculation: the one place every layer between block and HP settles, whoever added it. What a
/// layer ate is the difference between what went in and what came out (see <see cref="AbsorbLayers"/>).
/// Runs last of all the patches on this method, so it sees what other mods' layers left behind.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyHpLost))]
internal static class ModifyHpLostPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Creature target, decimal amount, HpLossHookPhase phases, decimal __result,
                                ref IEnumerable<AbstractModel> modifiers)
    {
        // Real damage settles one phase at a time; a call naming both is a preview and never removes HP.
        if (phases != HpLossHookPhase.BeforeOsty && phases != HpLossHookPhase.AfterOsty) return;
        try { AbsorbLayers.Measured(target, amount, __result, modifiers); }
        catch (Exception e) { Tracker.LogError("ModifyHpLost", e); }
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

/// <summary>The game relinks the top bar's controller navigation on every change: keep the podium at its end.</summary>
[HarmonyPatch(typeof(NTopBar), "UpdateNavigation")]
internal static class TopBarNavigationPatch
{
    private static void Postfix(NTopBar __instance)
    {
        try { TopBarButton.JoinNavigation(__instance); }
        catch (Exception e) { Tracker.LogError("NTopBar.UpdateNavigation", e); }
    }
}
