using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// Some mods put a layer between block and HP: an armour a hit spends before it reaches the creature. The game settles
/// every such layer in one place (see <see cref="ModifyHpLostPatch"/>), so what they ate is measured there, for any
/// mod, without knowing what the layer is. It counts as block: protection a hit spent, rather than HP anyone lost.
/// </summary>
internal static class AbsorbLayers
{
    private static readonly AbsorbLedger Ledger = new();

    /// <summary>The game worked out an HP loss: <paramref name="before"/> went in, <paramref name="after"/> came out.</summary>
    /// <param name="modifiers">The models the game says changed it; a layer that keeps quiet isn't in it.</param>
    public static void Measured(Creature target, decimal before, decimal after, IEnumerable<AbstractModel>? modifiers)
    {
        decimal eaten = before - after;
        if (eaten <= 0m) return;
        Ledger.Add(target, eaten, Layer(modifiers));
    }

    /// <summary>What this creature's layers ate on the hit that just landed, and what they were called. Clears it.</summary>
    public static (int Amount, IReadOnlyList<string> Layers) Take(Creature target) => Ledger.Take(target);

    /// <summary>A hit on this creature is starting: anything still counted for it is stale.</summary>
    public static void Starting(Creature target) => Ledger.Clear(target);

    public static void Clear() => Ledger.Clear();

    /// <summary>
    /// The last model the game was told changed this HP loss, as a hint for the log. Layers that reduce the amount
    /// without adding themselves leave nothing to name, which is why the amount is counted on its own.
    /// </summary>
    private static string? Layer(IEnumerable<AbstractModel>? modifiers)
    {
        try { return modifiers?.LastOrDefault()?.Id.Entry; }
        catch (Exception) { return null; }
    }
}
