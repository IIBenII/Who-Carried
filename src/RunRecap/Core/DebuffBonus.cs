namespace RunRecap.Core;

/// <summary>
/// What debuffs changed about a hit (extra damage from Vulnerable, damage kept off by Weak or Strength loss), and how
/// to share it between the players who applied them. Pure arithmetic; the game layer supplies the numbers.
/// </summary>
public static class DebuffBonus
{
    /// <summary>HP a hit of <paramref name="amount"/> removes: what gets past block, truncated like the game, capped at HP.</summary>
    public static int HpLoss(decimal amount, int block, int hpCap) =>
        hpCap <= 0 ? 0 : Math.Min((int)Math.Max(amount - block, 0m), hpCap);

    /// <summary>How much more HP the bigger of two versions of a hit removes (same block, same HP).</summary>
    public static int HpDifference(decimal more, decimal less, int block, int hpCap) =>
        Math.Max(0, HpLoss(more, block, hpCap) - HpLoss(less, block, hpCap));

    /// <summary>
    /// Extra HP a hit removed because of one debuff multiplier: HP it actually removed minus HP it would have removed
    /// without that multiplier, after the same block. Killing blows and Intangible-style caps get nothing extra.
    /// </summary>
    /// <param name="modifiedAmount">The hit's final damage, before block.</param>
    /// <param name="multiplier">The debuff's multiplier for this hit (1.5 for plain Vulnerable).</param>
    /// <param name="blocked">Damage the target's block absorbed.</param>
    /// <param name="hpRemoved">HP the hit actually removed (overkill excluded).</param>
    public static int Bonus(decimal modifiedAmount, decimal multiplier, int blocked, int hpRemoved) =>
        Bonuses(modifiedAmount, new[] { multiplier }, blocked, hpRemoved)[0];

    /// <summary>
    /// Extra HP a hit removed because of all its debuff multipliers together (Vulnerable and Flanking on one hit),
    /// shared between them by how much each multiplies (by logarithm, the even-handed split for multipliers that
    /// compound), so the shares add up to exactly the extra and no damage is claimed twice. Share i goes with
    /// multiplier i; multipliers of 1 or less get nothing.
    /// </summary>
    public static int[] Bonuses(decimal modifiedAmount, IReadOnlyList<decimal> multipliers, int blocked, int hpRemoved)
    {
        var none = new int[multipliers.Count];
        if (modifiedAmount <= 0m || hpRemoved <= 0) return none;
        decimal product = 1m;
        foreach (decimal m in multipliers)
            if (m > 1m) product *= m;
        if (product <= 1m) return none;
        decimal without = modifiedAmount / product;
        int hpWithout = Math.Min((int)Math.Max(without - blocked, 0m), hpRemoved);
        int extra = Math.Max(0, hpRemoved - hpWithout);
        return SplitIndexed(extra, multipliers.Select(m => m > 1m ? (decimal)Math.Log((double)m) : 0m).ToList());
    }

    /// <summary>
    /// HP a damage-reducing debuff on the attacker (Weak) kept off the target: HP the hit would have removed without
    /// the reduction minus HP it will remove, both after the target's block and capped at the target's HP.
    /// </summary>
    /// <param name="modifiedAmount">The hit's final damage, before block.</param>
    /// <param name="multiplier">The debuff's multiplier for this hit (0.75 for plain Weak).</param>
    /// <param name="block">The target's block before the hit.</param>
    /// <param name="hpCap">The target's current HP.</param>
    public static int Prevented(decimal modifiedAmount, decimal multiplier, int block, int hpCap) =>
        PreventedShares(modifiedAmount, new[] { multiplier }, block, hpCap)[0];

    /// <summary>
    /// HP several damage-reducing debuffs on the attacker (Weak and Shrink on one hit) kept off together, shared
    /// between them by how much each shrank the hit, like <see cref="Bonuses"/>. Share i goes with multiplier i;
    /// multipliers outside (0, 1) get nothing.
    /// </summary>
    public static int[] PreventedShares(decimal modifiedAmount, IReadOnlyList<decimal> multipliers, int block, int hpCap)
    {
        var none = new int[multipliers.Count];
        if (modifiedAmount <= 0m) return none;
        decimal product = 1m;
        foreach (decimal m in multipliers)
            if (m > 0m && m < 1m) product *= m;
        if (product >= 1m) return none;
        int kept = HpDifference(modifiedAmount / product, modifiedAmount, block, hpCap);
        return SplitIndexed(kept, multipliers.Select(m => m > 0m && m < 1m ? (decimal)-Math.Log((double)m) : 0m).ToList());
    }

    /// <summary>
    /// Shares <paramref name="amount"/> between players by weight (stacks each applied). Whole numbers that add up to
    /// the amount: floors first, then the leftover points go to the largest remainders (ties: earlier in the list).
    /// </summary>
    public static IReadOnlyDictionary<ulong, int> Split(int amount, IReadOnlyList<(ulong Player, int Weight)> weights)
    {
        int[] shares = SplitIndexed(amount, weights.Select(w => (decimal)w.Weight).ToList());
        var result = new Dictionary<ulong, int>();
        for (int i = 0; i < weights.Count; i++)
            if (shares[i] > 0) result[weights[i].Player] = result.GetValueOrDefault(weights[i].Player) + shares[i];
        return result;
    }

    /// <summary>The same sharing rule, by position: share i goes with weight i.</summary>
    public static int[] SplitIndexed(int amount, IReadOnlyList<decimal> weights)
    {
        var shares = new int[weights.Count];
        decimal total = weights.Sum(w => Math.Max(0m, w));
        if (amount <= 0 || total <= 0m) return shares;
        var exact = weights.Select(w => Math.Max(0m, w) * amount / total).ToArray();
        for (int i = 0; i < shares.Length; i++) shares[i] = (int)Math.Floor(exact[i]);
        int left = amount - shares.Sum();
        foreach (int i in Enumerable.Range(0, shares.Length)
                     .Where(i => weights[i] > 0m)
                     .OrderByDescending(i => exact[i] - Math.Floor(exact[i]))
                     .ThenBy(i => i)
                     .Take(left))
            shares[i] += 1;
        return shares;
    }
}
