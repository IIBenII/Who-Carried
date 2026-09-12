using System.Globalization;

namespace RunRecap.Core;

/// <summary>A game badge ready to show: its localized name and description, and the keys for its two images.</summary>
/// <param name="Rarity">"gold", "silver" or "bronze".</param>
public sealed record BadgeInfo(string Id, string Rarity, string Title, string Description)
{
    public const string IconPrefix = "badge:";
    public const string BasePrefix = "badge-base:";

    /// <summary>The badge's own picture.</summary>
    public string IconKey => IconPrefix + Id;

    /// <summary>The gold, silver or bronze holder the picture sits in.</summary>
    public string BaseKey => BasePrefix + Rarity;

    public int RarityOrder => Rarity switch { "gold" => 0, "silver" => 1, "bronze" => 2, _ => 3 };
}

/// <summary>One player's end-of-run badges (possibly none).</summary>
public sealed record PlayerBadges(ulong PlayerId, string Name, string ColorHex, string? IconKey, IReadOnlyList<BadgeInfo> Badges);

/// <summary>One of the recap's own awards: a title, who won it, the number behind it and what that number means.</summary>
/// <param name="Value">The headline number, formatted ("7 HP", "+4,138", "9 of 23").</param>
/// <param name="Detail">What the number means, as a short phrase.</param>
public sealed record Award(string Title, ulong PlayerId, string PlayerName, string ColorHex, string? IconKey, string Value,
                           string Detail);

/// <summary>
/// Broadcast-style superlatives, so support players get credit a damage ranking can't give them. Each award goes to
/// the one player who leads it (ties: the higher-ranked player); awards nobody has earned are left out. In solo runs
/// only the awards that don't compare players are given.
/// </summary>
public static class AwardBuilder
{
    /// <summary>"Clutch" needs the lowest HP to be at most this share of max HP.</summary>
    public const double ClutchShare = 0.3;

    public const int MinCardsCreated = 5;

    /// <summary>"Jack of all trades" needs at least this many different damage sources.</summary>
    public const int MinSources = 5;

    public const string Clutch = "Clutch", HeavyHitter = "Heavy hitter", Enabler = "Enabler", Protector = "Protector",
        Wall = "Wall", SiegeBreaker = "Siege breaker", FightLeader = "Fight leader", JackOfAllTrades = "Jack of all trades",
        CardFactory = "Card factory", Unscathed = "Unscathed", PunchingBag = "Punching bag";

    /// <param name="byRank">Players in scoreboard order; ties go to the earlier one.</param>
    public static IReadOnlyList<Award> Build(RunStats stats, IReadOnlyList<PlayerInfo> byRank,
                                             IReadOnlyDictionary<ulong, DefenseTotals> defense)
    {
        var awards = new List<Award>();
        if (byRank.Count == 0) return awards;
        bool team = byRank.Count >= 2;
        PlayerTotals? T(PlayerInfo p) => stats.Get(p.NetId);

        void Give(string title, PlayerInfo? p, string value, string detail)
        {
            if (p != null) awards.Add(new Award(title, p.NetId, p.Name, p.ColorHex, IconOf(p), value, detail));
        }

        // In this order: a player's first award is their headline on the scoreboard, so the more telling ones lead.
        PlayerInfo? hitter = Most(byRank, p => T(p)?.BiggestHit ?? 0);
        if (hitter != null)
            Give(HeavyHitter, hitter, Num(T(hitter)!.BiggestHit), $"damage in one hit, with {T(hitter)!.BiggestHitLabel}");

        PlayerInfo? enabler = team ? Most(byRank, p => Sum(T(p)?.DebuffBonus)) : null;
        if (enabler != null)
            Give(Enabler, enabler, "+" + Num(Sum(T(enabler)!.DebuffBonus)),
                $"damage teammates gained from their {RecapBuilder.JoinAnd(Labels(T(enabler)!.DebuffBonus))}");

        // Clutch: the lowest share of max HP anyone lived through, if it was a real close call.
        (PlayerInfo Player, int Hp, int Max)? low = null;
        foreach (PlayerInfo p in byRank)
        {
            if (Lowest(T(p), defense.GetValueOrDefault(p.NetId)) is not (int hp, int max)) continue;
            if (low is not (_, int bestHp, int bestMax) || (long)hp * bestMax < (long)bestHp * max) low = (p, hp, max);
        }
        if (low is (PlayerInfo lowPlayer, int lowHp, int lowMax) && lowHp <= lowMax * ClutchShare)
            Give(Clutch, lowPlayer, $"{Num(lowHp)} HP", $"left of {Num(lowMax)}, the closest call anyone lived through");

        if (!team) return awards;

        PlayerInfo? protector = Most(byRank, p => Sum(T(p)?.DebuffPrevented));
        if (protector != null)
            Give(Protector, protector, Num(Sum(T(protector)!.DebuffPrevented)),
                $"damage their {RecapBuilder.JoinAnd(Labels(T(protector)!.DebuffPrevented))} kept off the team");

        PlayerInfo? wall = Most(byRank, p => T(p)?.Blocked ?? 0);
        if (wall != null) Give(Wall, wall, Num(T(wall)!.Blocked), "damage blocked");

        PlayerInfo? breaker = Most(byRank, p => T(p)?.BlockRemoved ?? 0);
        if (breaker != null) Give(SiegeBreaker, breaker, Num(T(breaker)!.BlockRemoved), "enemy block knocked off");

        Dictionary<ulong, int> led = FightsLed(stats, byRank);
        PlayerInfo? leader = Most(byRank, p => led.GetValueOrDefault(p.NetId));
        if (leader != null)
            Give(FightLeader, leader, $"{led[leader.NetId]} of {stats.Fights.Count}", "fights where they dealt the most damage");

        int SourceCount(PlayerInfo p) => T(p)?.Sources.Values.Count(s => s.Amount > 0) ?? 0;
        PlayerInfo? jack = Most(byRank, p => SourceCount(p));
        if (jack != null && SourceCount(jack) >= MinSources)
            Give(JackOfAllTrades, jack, Num(SourceCount(jack)), "different cards, relics and powers that dealt damage");

        PlayerInfo? factory = Most(byRank, p => Sum(T(p)?.CardsCreated));
        if (factory != null && Sum(T(factory)!.CardsCreated) >= MinCardsCreated)
            Give(CardFactory, factory, Num(Sum(T(factory)!.CardsCreated)),
                $"cards created, mostly {Labels(T(factory)!.CardsCreated)[0]}");

        // Damage taken comes from the game's own per-floor history; only compare when every player has it.
        if (byRank.All(p => defense.ContainsKey(p.NetId)) && byRank.Select(p => defense[p.NetId].Taken).Distinct().Count() > 1)
        {
            PlayerInfo least = byRank.OrderBy(p => defense[p.NetId].Taken).First();
            PlayerInfo most = Most(byRank, p => defense[p.NetId].Taken)!;
            Give(Unscathed, least, Num(defense[least.NetId].Taken), "damage taken, the least on the team");
            Give(PunchingBag, most, Num(defense[most.NetId].Taken), "damage taken, the most on the team");
        }
        return awards;
    }

    /// <summary>The award to show on a player's slab: their first one in award order, or "" for none.</summary>
    public static string Headline(IReadOnlyList<Award> awards, ulong playerId) =>
        awards.FirstOrDefault(a => a.PlayerId == playerId)?.Title ?? "";

    /// <summary>The lower of the tracked in-fight low and the game's end-of-floor low, by share of max HP.</summary>
    public static (int Hp, int Max)? Lowest(PlayerTotals? totals, DefenseTotals? floors)
    {
        (int Hp, int Max)? best = null;
        foreach ((int hp, int max) in new[] { (totals?.LowestHp ?? 0, totals?.LowestHpMax ?? 0), (floors?.LowestHp ?? 0, floors?.LowestHpMax ?? 0) })
        {
            if (hp <= 0 || max <= 0) continue;
            if (best is not (int bh, int bm) || (long)hp * bm < (long)bh * max) best = (hp, max);
        }
        return best;
    }

    /// <summary>How many fights each player out-damaged everyone else in (a tie at the top counts for nobody).</summary>
    private static Dictionary<ulong, int> FightsLed(RunStats stats, IReadOnlyList<PlayerInfo> players)
    {
        var led = new Dictionary<ulong, int>();
        foreach (FightBucket fight in stats.Fights)
        {
            List<(ulong Id, int Damage)> ranked = players
                .Select(p => (p.NetId, fight.DamageByPlayer.GetValueOrDefault(RunStats.KeyFor(p.NetId))))
                .OrderByDescending(x => x.Item2)
                .ToList();
            if (ranked[0].Item2 <= 0 || (ranked.Count > 1 && ranked[1].Item2 == ranked[0].Item2)) continue;
            led[ranked[0].Id] = led.GetValueOrDefault(ranked[0].Id) + 1;
        }
        return led;
    }

    /// <summary>The player with the highest value above zero; the earlier one wins a tie.</summary>
    private static PlayerInfo? Most(IReadOnlyList<PlayerInfo> players, Func<PlayerInfo, long> value)
    {
        PlayerInfo? best = null;
        long bestValue = 0;
        foreach (PlayerInfo p in players)
        {
            long v = value(p);
            if (v > bestValue)
            {
                best = p;
                bestValue = v;
            }
        }
        return best;
    }

    private static int Sum(Dictionary<string, SourceTotal>? entries) => entries?.Values.Sum(e => e.Amount) ?? 0;

    /// <summary>Entry labels, biggest first.</summary>
    private static List<string> Labels(Dictionary<string, SourceTotal> entries) =>
        entries.Values.Where(e => e.Amount > 0)
            .GroupBy(e => e.Label)
            .OrderByDescending(g => g.Sum(e => e.Amount))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();

    private static string? IconOf(PlayerInfo p) => string.IsNullOrEmpty(p.CharacterId) ? null : p.CharacterId;

    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
