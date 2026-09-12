using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Glue between game events and the pure core. All calls arrive on the game's main thread.</summary>
internal static class Tracker
{
    private static RunStats _stats = new();
    private static IRunState? _run;
    private static EventLog? _log;
    private static string _dataDir = "";
    private static Dictionary<ulong, string> _names = new();

    public static RunStats Stats => _stats;

    /// <summary>Raised on the main thread after anything counted changes (the open recap refreshes from it).</summary>
    public static event Action? Changed;

    private static void Touch()
    {
        try { Changed?.Invoke(); }
        catch (Exception e) { LogError("Changed", e); }
    }

    public static IRunState? CurrentRun => _run;

    public static string DataDir => _dataDir;

    /// <summary>Appends a free-form line to events.log (exports, preview status).</summary>
    public static void Note(string line) => _log?.Write(line);

    // Not ".json": the game's mod loader scans every .json under mods/ looking for manifests.
    private static string StatsPath => Path.Combine(_dataDir, "current_run.dat");

    private static string Where => _run == null ? "[--]" : $"[F{_run.TotalFloor} A{_run.CurrentActIndex + 1}]";

    public static void Init(string modDir)
    {
        _dataDir = Path.Combine(modDir, "data");
        _log = new EventLog(Path.Combine(_dataDir, "events.log"));
    }

    public static void OnRunStarted(IRunState run)
    {
        _run = run;
        string key = GameReader.RunKey(run);
        RunStats? resumed = RunStatsStore.LoadIfResumable(StatsPath, key, GameReader.LoadedFromSave());
        _stats = resumed ?? new RunStats { RunKey = key };
        if (resumed == null)
        {
            // The last run's stats and log are kept beside the new ones (".previous"), in case it's wanted later.
            try
            {
                if (File.Exists(StatsPath)) File.Copy(StatsPath, Path.ChangeExtension(StatsPath, ".previous.dat"), overwrite: true);
            }
            catch (Exception e) { LogError("keep previous stats", e); }
            _log?.Reset(LogReplay.HeaderLine(ModEntry.Version, key, DateTime.Now));
        }
        else
            _log?.Write(LogReplay.ResumedLine(key, _stats.Fights.Count));

        IReadOnlyList<PlayerInfo> players = GameReader.Players(run);
        _names = players.ToDictionary(p => p.NetId, p => p.Name);
        foreach (PlayerInfo p in players)
            _log?.Write($"player {p.NetId} = {p.Name} ({p.Character}) #{p.ColorHex}");
    }

    public static void OnCombatStart(IRunState run, ICombatState? combat)
    {
        _run ??= run;
        DebuffBonusTracker.Clear();
        SelfFire.Clear();
        _fightLows.Clear();
        _fallen.Clear();
        string label = GameReader.EncounterLabel(combat);
        string room = GameReader.RoomType(run);
        _stats.BeginFight(run.CurrentActIndex + 1, run.TotalFloor, label, room);
        _log?.Write(room.Length > 0 ? $"{Where} fight start: {label} [{room}]" : $"{Where} fight start: {label}");
        Touch();
    }

    public static void OnDamage(PlayerChoiceContext? context, Creature? dealer, DamageResult result, Creature target,
                                CardModel? cardSource)
    {
        DamageFacts facts = FactsExtractor.Extract(context, dealer, result, target, cardSource);
        DebuffBonusTracker.PendingHit? boosted = DebuffBonusTracker.Take(target);
        if (facts.TargetIsEnemy)
        {
            AttributionResult who = Attribution.Resolve(facts);
            _stats.RecordDamage(who.PlayerId, who.Source, facts.HpRemoved, facts.Blocked);
            _log?.Write($"{Where} {NameOf(who.PlayerId)} <- {who.Source.Kind}:{who.Source.Id} ({who.Source.Label}) " +
                        $"{facts.HpRemoved} hp | target {Describe(target)}, blocked {facts.Blocked}, " +
                        $"dealer {Describe(dealer)}, stack [{StackIds(context)}]");
            if (boosted != null) CreditDebuffBonus(boosted, facts, who.PlayerId);
        }
        else if (facts.TargetPlayerId is ulong targetPlayer)
        {
            _stats.RecordBlocked(targetPlayer, facts.Blocked);
        }
        if (target.Player is Player hurt) NoteHp(hurt.NetId, target.CurrentHp, target.MaxHp);
        Touch();
    }

    /// <summary>Lasting Strength a player took off an enemy (Malaise) is listed as this debuff.</summary>
    private static readonly SourceRef StrengthLoss = new(SourceKind.Power, "STRENGTH_LOSS", "Strength loss");

    /// <summary>
    /// Just before block, with the hit's final damage.
    /// Hits on enemies: remember Vulnerable-style boosts for after the hit; if the attacking player is Weak, count the
    /// damage they lost.
    /// Enemy hits on players or pets: credit the HP that Weak and Strength loss on the enemy kept off, and count the
    /// extra HP a Vulnerable-style debuff on the victim cost them.
    /// </summary>
    public static void OnBeforeDamage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        DebuffBonusTracker.BeforeDamage(target, amount, props, dealer, cardSource);
        if (dealer == null || amount <= 0m) return;

        if (target.IsEnemy && FactsExtractor.PlayerIdOf(dealer) is ulong attacker)
        {
            foreach (DebuffBonusTracker.Amplifier weak in DebuffBonusTracker.DamageMultipliers(dealer, target, amount, props, dealer, cardSource, m => m > 0m && m < 1m))
            {
                int lost = (int)(amount / weak.Multiplier) - (int)amount;
                _stats.RecordDebuffCost(attacker, DebuffRef(weak.Power), RunStats.CostDealt, lost);
            }
            Touch();
            return;
        }

        if (!dealer.IsEnemy || FactsExtractor.PlayerIdOf(target) is not ulong victim) return;
        int block = (target.PetOwner?.Creature ?? target).Block; // pets use their owner's block
        int hp = target.CurrentHp;

        // Weak-style debuffs on the attacker: what they kept off together, shared by how much each shrank the hit.
        IReadOnlyList<DebuffBonusTracker.Amplifier> reducers = DebuffBonusTracker.Reducers(dealer, target, amount, props, cardSource);
        int[] kept = DebuffBonus.PreventedShares(amount, reducers.Select(r => r.Multiplier).ToList(), block, hp);
        for (int i = 0; i < reducers.Count; i++)
        {
            DebuffBonusTracker.Amplifier weak = reducers[i];
            int prevented = kept[i];
            if (prevented <= 0) continue;
            SourceRef debuff = DebuffRef(weak.Power);
            foreach ((ulong player, int share) in DebuffBonus.Split(prevented, DebuffBonusTracker.Weights(weak.Power)))
            {
                if (share <= 0) continue;
                _stats.RecordDebuffPrevented(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} prevented {share} via {debuff.Id} ({debuff.Label}) | " +
                            $"{Describe(dealer)} hit {NameOf(victim)} for {amount} (x{weak.Multiplier}, block {block})");
            }
        }

        if (props.IsPoweredAttack()) CreditStrengthLoss(target, amount, props, dealer, cardSource, victim, block, hp);

        foreach (DebuffBonusTracker.Amplifier vulnerable in DebuffBonusTracker.DamageMultipliers(target, target, amount, props, dealer, cardSource, m => m > 1m))
        {
            int extra = DebuffBonus.HpDifference(amount, amount / vulnerable.Multiplier, block, hp);
            _stats.RecordDebuffCost(victim, DebuffRef(vulnerable.Power), RunStats.CostTaken, extra);
        }
        Touch();
    }

    /// <summary>
    /// Strength an enemy lost to players (Piercing Wail, Dark Shackles for the turn; Malaise for good) made its
    /// attack weaker: the hit would have been bigger by that Strength times the hit's multipliers. The HP difference is
    /// shared between whoever took the Strength away, in proportion to how much each took.
    /// </summary>
    private static void CreditStrengthLoss(Creature target, decimal amount, ValueProp props, Creature dealer,
                                           CardModel? cardSource, ulong victim, int block, int hp)
    {
        var parts = new List<(ulong Player, SourceRef Debuff, decimal Strength)>();
        foreach ((PowerModel power, int strength) in DebuffBonusTracker.TemporaryStrengthLoss(dealer))
        {
            IReadOnlyList<(ulong Player, int Weight)> weights = DebuffBonusTracker.Weights(power);
            int total = weights.Sum(w => w.Weight);
            if (total <= 0) continue;
            SourceRef debuff = DebuffRef(power);
            foreach ((ulong player, int weight) in weights) parts.Add((player, debuff, (decimal)strength * weight / total));
        }
        foreach ((ulong player, int strength) in DebuffBonusTracker.LastingStrengthLoss(dealer))
            parts.Add((player, StrengthLoss, strength));
        decimal removed = parts.Sum(p => p.Strength);
        if (removed <= 0m || _run == null) return;

        decimal multiplier = DebuffBonusTracker.DamageMultiplier(_run, target, dealer, props, cardSource);
        int prevented = DebuffBonus.HpDifference(amount + removed * multiplier, amount, block, hp);
        int[] shares = DebuffBonus.SplitIndexed(prevented, parts.Select(p => p.Strength).ToList());
        for (int i = 0; i < parts.Count; i++)
        {
            if (shares[i] <= 0) continue;
            _stats.RecordDebuffPrevented(parts[i].Player, parts[i].Debuff, shares[i]);
            _log?.Write($"{Where} {NameOf(parts[i].Player)} prevented {shares[i]} via {parts[i].Debuff.Id} ({parts[i].Debuff.Label}) | " +
                        $"{Describe(dealer)} hit {NameOf(victim)} for {amount}, {removed} Strength removed (x{multiplier}, block {block})");
        }
    }

    /// <summary>Just before a creature gains block: count what a Frail-style debuff on a player took off it.</summary>
    public static void OnBeforeBlock(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        if (amount <= 0m || FactsExtractor.PlayerIdOf(creature) is not ulong player) return;
        IReadOnlyList<DebuffBonusTracker.Amplifier> reducers = DebuffBonusTracker.BlockReducers(creature, amount, props, cardSource);
        if (reducers.Count == 0) return;
        decimal final;
        try { final = Hook.ModifyBlock(creature.CombatState!, creature, amount, props, cardSource, null, out _); }
        catch (Exception) { return; }
        foreach (DebuffBonusTracker.Amplifier frail in reducers)
            _stats.RecordDebuffCost(player, DebuffRef(frail.Power), RunStats.CostBlock, (int)(final / frail.Multiplier) - (int)final);
        Touch();
    }

    /// <summary>A card created mid-fight (Souls, Shivs…), credited to the player who made it.</summary>
    public static void OnCardCreated(CardModel card, Player? creator)
    {
        if (creator == null) return; // enemies adding Dazed or Wounds pass no creator
        string id = card.Id.Entry;
        _stats.RecordCardCreated(creator.NetId, new SourceRef(SourceKind.Card, id, GameText.Title(card.TitleLocString, id)));
        Touch();
    }

    /// <summary>
    /// The extra HP debuff multipliers (Vulnerable, Flanking) added to this hit: shared between the debuffs by how much
    /// each multiplied, then each debuff's part between the players whose stacks of it are still on the enemy. The
    /// hitter's own share is dropped: it's already in their damage.
    /// </summary>
    private static void CreditDebuffBonus(DebuffBonusTracker.PendingHit hit, DamageFacts facts, ulong? hitter)
    {
        int[] bonuses = DebuffBonus.Bonuses(hit.Amount, hit.Amplifiers.Select(a => a.Multiplier).ToList(), facts.Blocked, facts.HpRemoved);
        for (int i = 0; i < hit.Amplifiers.Count; i++)
        {
            DebuffBonusTracker.Amplifier amp = hit.Amplifiers[i];
            int bonus = bonuses[i];
            if (bonus <= 0) continue;
            SourceRef debuff = DebuffRef(amp.Power);
            foreach ((ulong player, int share) in DebuffBonus.Split(bonus, DebuffBonusTracker.Weights(amp.Power)))
            {
                if (share <= 0 || player == hitter) continue;
                _stats.RecordDebuffBonus(player, debuff, share);
                _log?.Write($"{Where} {NameOf(player)} +{share} bonus via {debuff.Id} ({debuff.Label}) " +
                            $"on {NameOf(hitter)}'s hit (x{amp.Multiplier}, {facts.HpRemoved} hp)");
            }
        }
    }

    private static SourceRef DebuffRef(PowerModel power)
    {
        string id = power.Id.Entry;
        string label;
        try { label = GameText.Title(power.Title, id); }
        catch (Exception) { label = id; } // some powers build their title from another model
        return new SourceRef(SourceKind.Power, id, label);
    }

    /// <summary>
    /// Doom kills bypass the damage hooks: the game removes the creature's remaining HP with a direct kill.
    /// Count that HP as removed by the Doom power, credited to whoever applied it.
    /// </summary>
    public static void OnDoomKill(IReadOnlyList<Creature> creatures)
    {
        foreach (Creature creature in creatures)
        {
            if (creature == null || !creature.IsEnemy) continue;
            int hp = creature.CurrentHp;
            if (hp <= 0) continue;
            DoomPower? doom = creature.GetPower<DoomPower>();
            SourceCandidate source = doom != null
                ? FactsExtractor.Candidate(doom)
                : new SourceCandidate(new SourceRef(SourceKind.Power, "DOOM_POWER", "Doom"), null);
            _stats.RecordDamage(source.OwnerId, source.Source, hp);
            _log?.Write($"{Where} {NameOf(source.OwnerId)} <- {source.Source.Kind}:{source.Source.Id} ({source.Source.Label}) " +
                        $"{hp} hp | target {Describe(creature)}, doom kill");
        }
        Touch();
    }

    /// <summary>
    /// Debuff stacks landing on a creature, after Artifact and other modifiers. Only debuff-type powers with positive
    /// changes count: buffs, reductions and duration ticks are ignored. Strength-down cards (Piercing Wail, Dark
    /// Shackles) apply their own debuff, which is what gets counted, so the Strength they remove isn't counted twice.
    /// </summary>
    public static void OnPowerChanged(PlayerChoiceContext? context, PowerModel power, decimal amount, Creature? applier,
                                      CardModel? cardSource)
    {
        TrackStrengthLoss(power, amount, applier);
        if (amount <= 0 || power.Type != PowerType.Debuff) return;
        Creature? target = power.Owner;
        int stacks = (int)Math.Round(amount);
        if (target == null || stacks <= 0) return;

        SourceRef debuff = DebuffRef(power);
        ulong? applierPlayer = FactsExtractor.PlayerIdOf(applier);

        if (target.IsEnemy)
        {
            ulong? who = Attribution.ResolveApplier(applierPlayer, applier != null && applierPlayer == null,
                cardSource != null ? FactsExtractor.Candidate(cardSource) : null, FactsExtractor.StackTop(context));
            if (who is ulong player) _stats.RecordDebuffApplied(player, debuff, stacks);
            // Every stack goes in the queue, a player's or not, so they wear off in the order they went on.
            DebuffBonusTracker.AddStacks(power, who, stacks);
            string by = who != null ? NameOf(who) : applier != null ? "enemy" : "UNATTRIBUTED";
            _log?.Write($"{Where} {by} applied {stacks} {debuff.Id} ({debuff.Label}) | target {Describe(target)}, " +
                        $"applier {Describe(applier)}, stack [{StackIds(context)}]");
        }
        else if (target.Player is { } victim && applierPlayer == null)
        {
            _stats.RecordDebuffReceived(victim.NetId, debuff, stacks);
            _log?.Write($"{Where} {NameOf(victim.NetId)} received {stacks} {debuff.Id} ({debuff.Label}) | " +
                        $"applier {Describe(applier)}");
        }
        Touch();
    }

    /// <summary>
    /// Keeps the lasting Strength players took off each enemy. Negative Strength from a player counts; a temporary
    /// Strength-down debuff (Piercing Wail) lowers Strength the same way but is counted as itself, so its own amount is
    /// taken back out. The order the two arrive in doesn't matter.
    /// </summary>
    private static void TrackStrengthLoss(PowerModel power, decimal amount, Creature? applier)
    {
        Creature? enemy = power.Owner;
        if (enemy == null || !enemy.IsEnemy || FactsExtractor.PlayerIdOf(applier) is not ulong player) return;
        int change = (int)Math.Round(amount);
        if (power is StrengthPower && change < 0)
        {
            DebuffBonusTracker.AdjustStrengthLoss(enemy, player, -change);
            _stats.AdjustDebuffApplied(player, StrengthLoss, -change);
        }
        else if (power is TemporaryStrengthPower && power.Type == PowerType.Debuff && change > 0)
        {
            DebuffBonusTracker.AdjustStrengthLoss(enemy, player, -change);
            _stats.AdjustDebuffApplied(player, StrengthLoss, -change);
        }
    }

    /// <summary>Each player's lowest HP in the current fight; kept only if they finish the fight standing.</summary>
    private static readonly Dictionary<ulong, (int Hp, int Max)> _fightLows = new();

    /// <summary>Players who went down in the current fight (a co-op revive afterwards isn't a close call).</summary>
    private static readonly HashSet<ulong> _fallen = new();

    /// <summary>Close calls ("Clutch"): a player's own HP right after a hit, pets excluded.</summary>
    private static void NoteHp(ulong player, int hp, int max)
    {
        if (hp <= 0)
        {
            _fallen.Add(player);
            return;
        }
        if (max <= 0) return;
        if (!_fightLows.TryGetValue(player, out (int Hp, int Max) low) || (long)hp * low.Max < (long)low.Hp * max)
            _fightLows[player] = (hp, max);
    }

    /// <summary>The fight is over: the lows of everyone still standing count.</summary>
    private static void CommitFightLows()
    {
        foreach ((ulong player, (int hp, int max)) in _fightLows)
            if (!_fallen.Contains(player) && _stats.RecordHp(player, hp, max))
                _log?.Write($"{Where} {NameOf(player)} hp low {hp}/{max}");
        _fightLows.Clear();
        _fallen.Clear();
    }

    public static void OnCombatEnd(IRunState run)
    {
        DebuffBonusTracker.Clear();
        CommitFightLows();
        _stats.EndFight();
        Save();
        _log?.Write($"{Where} fight end, saved");
        Touch();
    }

    /// <param name="saved">The run as the game just saved it; the game works out everyone's badges from it.</param>
    public static void OnRunEnded(bool isVictory, SerializableRun? saved)
    {
        // Heart of the Spire ends a won run a second time as a defeat; the game's own history keeps the win.
        if (_stats.Finished && _stats.Victory == true && !isVictory)
        {
            _log?.Write($"{Where} ignored a later \"defeat\" for a run already won");
            return;
        }
        if (isVictory) CommitFightLows(); // the last fight may end the run before its own end-of-fight
        _stats.EndFight();
        _stats.Finished = true;
        _stats.Victory = isVictory;
        _log?.Write($"{Where} run ended: {(isVictory ? "victory" : "defeat")}");
        RecordBadges(isVictory, saved);
        Save();
        Touch();
    }

    /// <summary>
    /// The same badges the game shows on its end screen and writes to its run history, for every player. Asked of the
    /// game directly, so they're there for guests in co-op too. Abandoned runs get none, as in the game.
    /// </summary>
    private static void RecordBadges(bool isVictory, SerializableRun? saved)
    {
        if (saved == null || RunManager.Instance.IsAbandoned) return;
        foreach (SerializablePlayer player in saved.Players)
        {
            try
            {
                List<EarnedBadge> badges = ScoreUtility.GetBadges(saved, player.NetId, isVictory)
                    .Select(b => new EarnedBadge { Id = b.Id, Rarity = b.Rarity.ToString().ToLowerInvariant() })
                    .ToList();
                _stats.SetBadges(player.NetId, badges);
                foreach (EarnedBadge b in badges) _log?.Write($"{Where} {NameOf(player.NetId)} badge {b.Id} ({b.Rarity})");
            }
            catch (Exception e)
            {
                LogError($"badges for {NameOf(player.NetId)}", e);
            }
        }
    }

    public static void LogError(string where, Exception e)
    {
        _log?.Write($"ERROR in {where}: {e}");
        Log.Error($"[WhoCarried] {where}: {e.Message}");
    }

    private static void Save()
    {
        try { RunStatsStore.Save(_stats, StatsPath); }
        catch (Exception e) { LogError("save", e); }
    }

    private static string NameOf(ulong? playerId) =>
        playerId is ulong id ? _names.GetValueOrDefault(id, id.ToString()) : "UNATTRIBUTED";

    private static string Describe(Creature? c)
    {
        if (c == null) return "null";
        if (c.Player != null) return $"player {NameOf(c.Player.NetId)}";
        if (c.PetOwner != null) return $"pet {c.Monster?.Id.Entry ?? "?"} of {NameOf(c.PetOwner.NetId)}";
        return c.Monster?.Id.Entry ?? "?";
    }

    private static string StackIds(PlayerChoiceContext? context) =>
        string.Join(",", context?.ModelStack?.Select(m => m.Id.Entry) ?? Enumerable.Empty<string>());
}
