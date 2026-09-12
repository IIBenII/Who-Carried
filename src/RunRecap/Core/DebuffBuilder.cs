using System.Globalization;

namespace RunRecap.Core;

/// <summary>
/// One player's share of one debuff, the extra damage teammates dealt thanks to it (Vulnerable), and the HP it kept
/// off the team (Weak, Strength loss).
/// </summary>
public sealed record DebuffBar(string PlayerLabel, string ColorHex, string? IconKey, int Amount, double Fraction,
                               int Bonus = 0, int Prevented = 0);

/// <summary>
/// A debuff applied to enemies this run, with a bar for each player who used it (fractions relative to this
/// debuff's leader).
/// </summary>
public sealed record DebuffGroup(string Label, string IconKey, int Total, IReadOnlyList<DebuffBar> Bars, int BonusTotal = 0,
                                 int PreventedTotal = 0);

/// <summary>Debuff stacks enemies put on one player, and the most common ones.</summary>
public sealed record DebuffReceivedRow(string Label, string ColorHex, string? IconKey, int Total, string Top);

/// <summary>One thing an enemy debuff cost a player: "Vulnerable: 84 extra damage taken".</summary>
/// <param name="Effect"><see cref="RunStats.CostTaken"/>, <see cref="RunStats.CostDealt"/> or <see cref="RunStats.CostBlock"/>.</param>
public sealed record CostLine(string Debuff, string Effect, int Amount);

/// <summary>What enemy debuffs cost one player, biggest first.</summary>
public sealed record DebuffCostRow(string Label, string ColorHex, string? IconKey, IReadOnlyList<CostLine> Lines);

public sealed record DebuffsView(IReadOnlyList<DebuffGroup> Applied, int MoreApplied, IReadOnlyList<DebuffReceivedRow> Received,
                                 IReadOnlyList<DebuffCostRow>? CostRows = null)
{
    public IReadOnlyList<DebuffCostRow> Costs => CostRows ?? Array.Empty<DebuffCostRow>();
}

/// <summary>Turns counted debuff stacks into the Debuffs tab and export section. No game or Godot types.</summary>
public static class DebuffBuilder
{
    /// <summary>Every debuff is shown; kept so a cap can come back if runs get very debuff-heavy.</summary>
    public const int TopDebuffs = int.MaxValue;
    public const int TopReceived = 3;

    /// <summary>Icon keys for debuffs are "power:" + the power id; character icon keys have no prefix.</summary>
    public const string IconPrefix = "power:";

    public static DebuffsView Build(RunStats stats, IReadOnlyList<PlayerInfo> players)
    {
        int AmountOf(PlayerInfo p, string key) => stats.Get(p.NetId)?.DebuffsApplied.GetValueOrDefault(key)?.Amount ?? 0;
        int BonusOf(PlayerInfo p, string key) => stats.Get(p.NetId)?.DebuffBonus.GetValueOrDefault(key)?.Amount ?? 0;
        int PreventedOf(PlayerInfo p, string key) => stats.Get(p.NetId)?.DebuffPrevented.GetValueOrDefault(key)?.Amount ?? 0;

        // A debuff shows if anyone applied it, or it did something (bonus damage, damage kept off).
        var debuffs = players
            .SelectMany(p => stats.Get(p.NetId) is PlayerTotals t
                ? t.DebuffsApplied.Concat(t.DebuffBonus).Concat(t.DebuffPrevented)
                : Enumerable.Empty<KeyValuePair<string, SourceTotal>>())
            .GroupBy(kv => kv.Key)
            .Select(g => (Key: g.Key, Label: g.First().Value.Label, Total: players.Sum(p => AmountOf(p, g.Key))))
            .OrderByDescending(d => d.Total)
            .ThenBy(d => d.Label, StringComparer.Ordinal)
            .ToList();

        List<DebuffGroup> applied = debuffs.Take(TopDebuffs).Select(d =>
        {
            int leader = players.Select(p => AmountOf(p, d.Key)).DefaultIfEmpty(0).Max();
            List<DebuffBar> all = players
                .Select(p => new DebuffBar(p.Name, p.ColorHex, IconOf(p), AmountOf(p, d.Key),
                    Fraction(AmountOf(p, d.Key), leader), BonusOf(p, d.Key), PreventedOf(p, d.Key)))
                .ToList();
            List<DebuffBar> bars = all.Where(b => b.Amount > 0 || b.Bonus > 0 || b.Prevented > 0).ToList();
            return new DebuffGroup(d.Label, IconPrefix + IdOf(d.Key), d.Total, bars, all.Sum(b => b.Bonus),
                all.Sum(b => b.Prevented));
        }).ToList();

        List<DebuffReceivedRow> received = players.Select(p =>
        {
            ICollection<SourceTotal> totals = stats.Get(p.NetId)?.DebuffsReceived.Values ?? (ICollection<SourceTotal>)Array.Empty<SourceTotal>();
            string top = string.Join(" · ", totals
                .OrderByDescending(t => t.Amount)
                .ThenBy(t => t.Label, StringComparer.Ordinal)
                .Take(TopReceived)
                .Select(t => $"{t.Label} {t.Amount.ToString("N0", CultureInfo.InvariantCulture)}"));
            return new DebuffReceivedRow(p.Name, p.ColorHex, IconOf(p), totals.Sum(t => t.Amount), top);
        }).ToList();

        List<DebuffCostRow> costs = players.Select(p => new DebuffCostRow(p.Name, p.ColorHex, IconOf(p),
            (stats.Get(p.NetId)?.DebuffCosts.Values ?? Enumerable.Empty<CostTotal>())
                .Where(c => c.Amount > 0)
                .OrderByDescending(c => c.Amount)
                .ThenBy(c => c.Label, StringComparer.Ordinal)
                .Select(c => new CostLine(c.Label, c.Effect, c.Amount))
                .ToList())).ToList();

        return new DebuffsView(applied, Math.Max(0, debuffs.Count - applied.Count), received, costs);
    }

    /// <summary>"Power:VULNERABLE_POWER" -> "VULNERABLE_POWER".</summary>
    private static string IdOf(string sourceKey)
    {
        int colon = sourceKey.IndexOf(':');
        return colon < 0 ? sourceKey : sourceKey[(colon + 1)..];
    }

    private static string? IconOf(PlayerInfo p) => string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId;

    private static double Fraction(int value, int max) => max <= 0 ? 0 : Math.Clamp((double)value / max, 0, 1);
}
