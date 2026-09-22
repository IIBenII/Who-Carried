using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>
/// What the mod remembers about the fight in progress. <see cref="Reset"/> swaps in an empty one as a fight starts and
/// as it ends, so nothing kept for one fight reaches the next and nothing has to be cleared by hand: memory a new rule
/// keeps for a fight belongs here. Memory tied to one enemy or one power (a debuff's stacks) stays with that object
/// instead, since every fight brings new ones.
/// </summary>
internal sealed class Fight
{
    /// <summary>The fight in progress; between fights, an empty one.</summary>
    public static Fight Now { get; private set; } = new();

    public static void Reset() => Now = new Fight();

    /// <summary>Hits a debuff made bigger, waiting for their block to be known (<see cref="DebuffBonusTracker"/>).</summary>
    public Dictionary<Creature, DebuffBonusTracker.PendingHit> BoostedHits { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Each target's next hit as the game's damage calculation worked it out (<see cref="DebuffBonusTracker"/>).</summary>
    public Dictionary<Creature, DebuffBonusTracker.Calc> Calcs { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>What mods' armour layers ate on each creature's current hit (<see cref="AbsorbLayers"/>).</summary>
    public AbsorbLedger Absorbed { get; } = new();

    /// <summary>What made each player's last unexplained damage call, and when (<see cref="SelfFire"/>).</summary>
    public Dictionary<ulong, (AbstractModel Source, ulong At)> SelfFired { get; } = new();

    /// <summary>Enemies whose Doom kill is counted, so the kill command that follows doesn't count them again.</summary>
    public HashSet<Creature> DoomKilled { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Each player's lowest HP; kept only if they finish the fight standing.</summary>
    public Dictionary<ulong, (int Hp, int Max)> Lows { get; } = new();

    /// <summary>Players who went down (a co-op revive afterwards isn't a close call).</summary>
    public HashSet<ulong> Fallen { get; } = new();
}
