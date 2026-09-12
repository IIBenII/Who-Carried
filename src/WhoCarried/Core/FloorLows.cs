namespace WhoCarried.Core;

/// <summary>
/// Each player's lowest end-of-floor HP (as a share of max HP) from the game's per-floor history, for "Clutch".
/// Floors where the damage taken used up all the HP they came in with are skipped: they went down there, and a co-op
/// revive afterwards isn't a close call.
/// </summary>
public sealed class FloorLows
{
    private readonly RunStats _lows = new(); // reuses its "lowest share of max HP" rule
    private readonly Dictionary<ulong, int> _before = new();

    /// <param name="hp">HP at the end of the floor.</param>
    /// <param name="taken">Damage taken on the floor.</param>
    public void Add(ulong player, int hp, int maxHp, int taken)
    {
        bool wentDown = _before.TryGetValue(player, out int before) && taken > 0 && taken >= before;
        _before[player] = hp;
        if (!wentDown) _lows.RecordHp(player, hp, maxHp);
    }

    /// <summary>(0, 0) when nothing was recorded.</summary>
    public (int Hp, int Max) Get(ulong player) =>
        _lows.Get(player) is PlayerTotals t ? (t.LowestHp, t.LowestHpMax) : (0, 0);
}
