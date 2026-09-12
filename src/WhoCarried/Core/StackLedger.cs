namespace WhoCarried.Core;

/// <summary>
/// Who put the stacks into one debuff on one enemy, in the order they went on. Duration debuffs (Vulnerable, Weak)
/// wear off one stack a round; the oldest stacks go first. New stacks always join the back and wearing off always takes
/// from the front, so trimming only when a hit needs the split gives the same answer as following every tick.
/// </summary>
public sealed class StackLedger
{
    private readonly List<(ulong? Player, int Stacks)> _queue = new();
    private readonly List<(ulong Player, int Stacks)> _lifetime = new();

    /// <summary>Stacks that landed; <paramref name="player"/> null for stacks no player applied (they still wear off in turn).</summary>
    public void Add(ulong? player, int stacks)
    {
        if (stacks <= 0) return;
        _queue.Add((player, stacks));
        if (player is not ulong id) return;
        int at = _lifetime.FindIndex(e => e.Player == id);
        if (at < 0) _lifetime.Add((id, stacks));
        else _lifetime[at] = (id, _lifetime[at].Stacks + stacks);
    }

    /// <summary>
    /// Matches the queue to the stacks the enemy actually has: the oldest wear off first. If it has more than were
    /// seen (applied before a Save &amp; Quit resume), the unseen ones count as the oldest and belong to no player.
    /// </summary>
    public void SyncTo(int amount)
    {
        if (amount < 0) return; // "infinite" debuffs use negative amounts
        int total = _queue.Sum(e => e.Stacks);
        if (total < amount)
        {
            _queue.Insert(0, (null, amount - total));
            return;
        }
        int wornOff = total - amount;
        while (wornOff > 0 && _queue.Count > 0)
        {
            (ulong? player, int stacks) = _queue[0];
            if (stacks <= wornOff)
            {
                wornOff -= stacks;
                _queue.RemoveAt(0);
            }
            else
            {
                _queue[0] = (player, stacks - wornOff);
                wornOff = 0;
            }
        }
    }

    /// <summary>Each player's stacks still on the enemy, in the order they first appear.</summary>
    public IReadOnlyList<(ulong Player, int Weight)> Active()
    {
        var active = new List<(ulong Player, int Weight)>();
        foreach ((ulong? player, int stacks) in _queue)
        {
            if (player is not ulong id) continue;
            int at = active.FindIndex(e => e.Player == id);
            if (at < 0) active.Add((id, stacks));
            else active[at] = (id, active[at].Weight + stacks);
        }
        return active;
    }

    /// <summary>Every stack each player ever put in, in the order they first applied.</summary>
    public IReadOnlyList<(ulong Player, int Weight)> Lifetime() => _lifetime.ToList();
}
