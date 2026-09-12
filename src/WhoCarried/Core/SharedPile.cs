namespace WhoCarried.Core;

/// <summary>
/// One Poison or Doom pile on one enemy, and whose it is. Each player's stacks are their share; when the pile shrinks,
/// every share shrinks by the same factor, so the proportions only change when stacks are added. Damage the pile deals
/// is split by the players' shares in whole points: everyone rounded down, the spare points to the largest fractions,
/// and exact ties take turns (the first goes to whoever stacked first, then the turn passes to the player after each
/// tie's winner). Stacks no player applied shrink with the rest but take no part in the split.
/// </summary>
public sealed class SharedPile
{
    /// <summary>Owners in the order they first stacked; null is stacks no player applied (an enemy's, or unseen).</summary>
    private readonly List<(ulong? Player, decimal Share)> _shares = new();

    /// <summary>Index into the players (in first-stacked order) of whoever wins the next exact tie.</summary>
    private int _turn;

    /// <summary>Scaling leaves decimal dust: a pile this close to its real size counts as matching it.</summary>
    private const decimal Dust = 0.000001m;

    /// <summary>Stacks that just landed; <paramref name="pileAfter"/> is the pile's size including them.</summary>
    public void Add(ulong? player, int stacks, int pileAfter)
    {
        if (stacks <= 0) return;
        ShrinkTo(pileAfter - stacks);
        int at = _shares.FindIndex(s => s.Player == player);
        if (at < 0) _shares.Add((player, stacks));
        else _shares[at] = (player, _shares[at].Share + stacks);
    }

    /// <summary>
    /// Shares out <paramref name="damage"/> that the pile just dealt at size <paramref name="pileNow"/>: whole points per
    /// player that add up to the damage. Empty when there's no damage or no player has a share.
    /// </summary>
    public IReadOnlyDictionary<ulong, int> Credit(int pileNow, int damage)
    {
        ShrinkTo(pileNow);
        var credits = new Dictionary<ulong, int>();
        IReadOnlyList<(ulong Player, decimal Share)> players = Shares();
        if (damage <= 0 || players.Count == 0) return credits;

        // In turn order, so the split's "ties go to the earlier one" hands ties out in turn.
        int n = players.Count;
        int first = _turn % n;
        var inTurn = Enumerable.Range(0, n).Select(i => players[(first + i) % n]).ToList();
        List<decimal> weights = inTurn.Select(p => p.Share).ToList();
        int[] points = DebuffBonus.SplitIndexed(damage, weights);

        // The same exact parts the split worked from: who got a spare point, and on what fraction.
        decimal total = weights.Sum();
        decimal[] exact = weights.Select(w => w * damage / total).ToArray();
        decimal[] fraction = exact.Select(e => e - Math.Floor(e)).ToArray();
        bool[] spare = Enumerable.Range(0, n).Select(i => points[i] > Math.Floor(exact[i])).ToArray();
        int lastTieWinner = -1;
        for (int i = 0; i < n; i++)
            if (spare[i] && Enumerable.Range(0, n).Any(j => !spare[j] && fraction[j] == fraction[i]))
                lastTieWinner = i;
        if (lastTieWinner >= 0) _turn = ((first + lastTieWinner) % n + 1) % n;

        for (int i = 0; i < n; i++)
            if (points[i] > 0) credits[inTurn[i].Player] = points[i];
        return credits;
    }

    /// <summary>Each player's share of the pile as last seen, in the order they first stacked.</summary>
    public IReadOnlyList<(ulong Player, decimal Share)> Shares() =>
        _shares.Where(s => s.Player.HasValue).Select(s => (s.Player.GetValueOrDefault(), s.Share)).ToList();

    /// <summary>
    /// Matches the pile's size: every share shrinks by the same factor, and stacks nobody was seen adding join the
    /// no-player share. At 0 the pile is gone; the game makes a new one when Poison or Doom lands again.
    /// </summary>
    private void ShrinkTo(int amount)
    {
        if (amount <= 0)
        {
            _shares.Clear();
            _turn = 0;
            return;
        }
        decimal total = _shares.Sum(s => s.Share);
        if (total > amount + Dust)
        {
            for (int i = 0; i < _shares.Count; i++)
                _shares[i] = (_shares[i].Player, _shares[i].Share * amount / total);
        }
        else if (total < amount - Dust)
        {
            decimal unseen = amount - total;
            int at = _shares.FindIndex(s => s.Player == null);
            if (at < 0) _shares.Add((null, unseen));
            else _shares[at] = (null, _shares[at].Share + unseen);
        }
    }
}
