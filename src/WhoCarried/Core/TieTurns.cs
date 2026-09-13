namespace WhoCarried.Core;

/// <summary>
/// Splits whole points by weight like <see cref="DebuffBonus.SplitIndexed"/>, but exact ties take turns: the first goes
/// to the earliest in the list, then the turn passes to the one after each tie's winner. The caller keeps the list in
/// the same order from one split to the next (entry i is always the same player), with weight 0 for anyone out of it.
/// </summary>
public sealed class TieTurns
{
    /// <summary>Index into the list of whoever wins the next exact tie.</summary>
    private int _turn;

    /// <summary>Shares <paramref name="amount"/> by <paramref name="weights"/>; share i goes with weight i.</summary>
    public int[] Split(int amount, IReadOnlyList<decimal> weights)
    {
        int n = weights.Count;
        var shares = new int[n];
        decimal total = weights.Sum(w => Math.Max(0m, w));
        if (n == 0 || amount <= 0 || total <= 0m) return shares;

        // In turn order, so the split's "ties go to the earlier one" hands ties out in turn.
        int first = _turn % n;
        List<decimal> inTurn = Enumerable.Range(0, n).Select(i => Math.Max(0m, weights[(first + i) % n])).ToList();
        int[] points = DebuffBonus.SplitIndexed(amount, inTurn);

        // The same exact parts the split worked from: who got a spare point, and on what fraction.
        decimal[] exact = inTurn.Select(w => w * amount / total).ToArray();
        decimal[] fraction = exact.Select(e => e - Math.Floor(e)).ToArray();
        bool[] spare = Enumerable.Range(0, n).Select(i => points[i] > Math.Floor(exact[i])).ToArray();
        int lastTieWinner = -1;
        for (int i = 0; i < n; i++)
            if (spare[i] && Enumerable.Range(0, n).Any(j => !spare[j] && fraction[j] == fraction[i]))
                lastTieWinner = i;
        if (lastTieWinner >= 0) _turn = ((first + lastTieWinner) % n + 1) % n;

        for (int i = 0; i < n; i++) shares[(first + i) % n] = points[i];
        return shares;
    }

    /// <summary>Back to the first in the list winning the next tie.</summary>
    public void Reset() => _turn = 0;
}

/// <summary>
/// <see cref="TieTurns"/> for callers whose list changes from split to split: it remembers every key in the order it
/// first saw them and lines each split up with that order, so ties go round the same way whoever is in this one.
/// </summary>
public sealed class TieTurnsByKey<TKey> where TKey : notnull
{
    private readonly List<TKey> _order = new();
    private readonly TieTurns _ties = new();

    /// <summary>
    /// Shares <paramref name="amount"/> by weight; share i goes with part i. A key listed twice counts once, with its
    /// weights added, and its first entry gets the points.
    /// </summary>
    public int[] Split(int amount, IReadOnlyList<(TKey Key, decimal Weight)> parts)
    {
        foreach ((TKey key, _) in parts)
            if (!_order.Contains(key)) _order.Add(key);
        var weights = new decimal[_order.Count];
        foreach ((TKey key, decimal weight) in parts) weights[_order.IndexOf(key)] += Math.Max(0m, weight);
        int[] byOrder = _ties.Split(amount, weights);

        var shares = new int[parts.Count];
        var paid = new HashSet<TKey>();
        for (int i = 0; i < parts.Count; i++)
            if (paid.Add(parts[i].Key)) shares[i] = byOrder[_order.IndexOf(parts[i].Key)];
        return shares;
    }
}
