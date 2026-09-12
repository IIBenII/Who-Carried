using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using RunRecap.Core;

namespace RunRecap.Game;

/// <summary>
/// For Vulnerable-style bonus damage. Per enemy hit: the final damage (before block) and any debuff on the target
/// that multiplied it. Per debuff instance: how many stacks each player put in. Only reads game state.
/// </summary>
internal static class DebuffBonusTracker
{
    public sealed record Amplifier(PowerModel Power, decimal Multiplier);

    public sealed record PendingHit(decimal Amount, IReadOnlyList<Amplifier> Amplifiers);

    private static readonly Dictionary<Creature, PendingHit> Pending = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<PowerModel, StackLedger> Stacks = new();

    /// <summary>Called just before a hit's block is applied, with the hit's final damage.</summary>
    public static void BeforeDamage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Pending.Remove(target);
        if (!target.IsEnemy || amount <= 0m) return;
        List<Amplifier>? found = null;
        foreach (PowerModel power in target.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = power.ModifyDamageMultiplicative(target, amount, props, dealer, cardSource, null); }
            catch (Exception) { continue; }
            if (multiplier > 1m) (found ??= new List<Amplifier>()).Add(new Amplifier(power, multiplier));
        }
        if (found != null) Pending[target] = new PendingHit(amount, found);
    }

    /// <summary>Debuffs on the attacking enemy that shrank this hit (Weak), with their multipliers (below 1).</summary>
    public static IReadOnlyList<Amplifier> Reducers(Creature dealer, Creature target, decimal amount, ValueProp props,
                                                    CardModel? cardSource) =>
        DamageMultipliers(dealer, target, amount, props, dealer, cardSource, m => m > 0m && m < 1m);

    /// <summary>
    /// Debuffs on <paramref name="holder"/> whose damage multiplier for this hit passes <paramref name="keep"/>. Holder
    /// is the target for Vulnerable-style debuffs and the dealer for Weak-style ones.
    /// </summary>
    public static IReadOnlyList<Amplifier> DamageMultipliers(Creature holder, Creature target, decimal amount, ValueProp props,
                                                             Creature? dealer, CardModel? cardSource, Func<decimal, bool> keep)
    {
        var found = new List<Amplifier>();
        foreach (PowerModel power in holder.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = power.ModifyDamageMultiplicative(target, amount, props, dealer, cardSource, null); }
            catch (Exception) { continue; }
            if (keep(multiplier)) found.Add(new Amplifier(power, multiplier));
        }
        return found;
    }

    /// <summary>Debuffs on a creature that shrink the block it gains (Frail), with their multipliers (below 1).</summary>
    public static IReadOnlyList<Amplifier> BlockReducers(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        var found = new List<Amplifier>();
        foreach (PowerModel power in creature.Powers.ToList())
        {
            if (power.Type != PowerType.Debuff) continue;
            decimal multiplier;
            try { multiplier = power.ModifyBlockMultiplicative(creature, amount, props, cardSource, null); }
            catch (Exception) { continue; }
            if (multiplier > 0m && multiplier < 1m) found.Add(new Amplifier(power, multiplier));
        }
        return found;
    }

    /// <summary>
    /// The product of every damage multiplier on this hit (Weak, Vulnerable, relics…), from the game's own damage
    /// calculation, the same one it uses to preview intents. 1 if it can't be worked out.
    /// </summary>
    public static decimal DamageMultiplier(IRunState run, Creature target, Creature dealer, ValueProp props, CardModel? cardSource)
    {
        try
        {
            return Hook.ModifyDamage(run, target.CombatState, target, dealer, 1m, props, cardSource, null,
                ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
        }
        catch (Exception)
        {
            return 1m;
        }
    }

    /// <summary>Temporary Strength-down debuffs on an enemy (Piercing Wail, Dark Shackles) and how much Strength each removes.</summary>
    public static IReadOnlyList<(PowerModel Power, int Amount)> TemporaryStrengthLoss(Creature enemy) =>
        enemy.Powers.Where(p => p is TemporaryStrengthPower && p.Type == PowerType.Debuff && p.Amount > 0)
            .Select(p => (p, p.Amount)).ToList();

    private static readonly ConditionalWeakTable<Creature, Dictionary<ulong, int>> StrengthLoss = new();

    /// <summary>
    /// Lasting Strength a player took off an enemy (Malaise). Negative Strength from a player adds to it; a temporary
    /// Strength-down debuff from that player takes its own amount back out, because it lowered Strength the same way
    /// and is counted on its own.
    /// </summary>
    public static void AdjustStrengthLoss(Creature enemy, ulong player, int delta)
    {
        Dictionary<ulong, int> byPlayer = StrengthLoss.GetOrCreateValue(enemy);
        byPlayer[player] = byPlayer.GetValueOrDefault(player) + delta;
    }

    public static IReadOnlyList<(ulong Player, int Amount)> LastingStrengthLoss(Creature enemy) =>
        StrengthLoss.TryGetValue(enemy, out Dictionary<ulong, int>? byPlayer)
            ? byPlayer.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)).ToList()
            : Array.Empty<(ulong, int)>();

    /// <summary>The pending hit on this target, if a debuff boosted it. Removes it.</summary>
    public static PendingHit? Take(Creature target) => Pending.Remove(target, out PendingHit? hit) ? hit : null;

    public static void Clear() => Pending.Clear();

    /// <summary>Stacks landing on an enemy's debuff; <paramref name="player"/> null when no player applied them.</summary>
    public static void AddStacks(PowerModel power, ulong? player, int stacks)
    {
        if (stacks <= 0) return;
        Stacks.GetOrCreateValue(power).Add(player, stacks);
    }

    /// <summary>
    /// How to share what this debuff did on a hit right now: each player's stacks still on the enemy (the oldest wear
    /// off first). If no player's stacks are left (all applied before a Save &amp; Quit resume), the power's own
    /// applier gets it all; failing that, everyone who ever stacked it, by how much.
    /// </summary>
    public static IReadOnlyList<(ulong Player, int Weight)> Weights(PowerModel power)
    {
        if (Stacks.TryGetValue(power, out StackLedger? ledger))
        {
            ledger.SyncTo(power.Amount);
            IReadOnlyList<(ulong Player, int Weight)> active = ledger.Active();
            if (active.Count > 0) return active;
        }
        if (FactsExtractor.PlayerIdOf(power.Applier) is ulong id) return new[] { (id, 1) };
        return ledger?.Lifetime() ?? Array.Empty<(ulong, int)>();
    }
}
