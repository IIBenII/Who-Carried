using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class ReplayTests
{
    private static readonly string[] Log =
    {
        "Run Recap v0.1.0 - run SEED1:1789137571 started 2026-09-11 15:39", // written before the rename
        "player 11 = Moth (The Tailor) #5c350f",
        "player 22 = Ironside (The Necrobinder) #ee82ee",
        "[F2 A1] fight start: Toadpoles",
        "[F2 A1] Ironside <- Pet:OSTY (Osty) 12 hp | target TOADPOLE, blocked 3, dealer pet OSTY of Ironside, stack [UNLEASH]",
        "[F2 A1] Moth <- Card:THETAILOR-TRUE_KNIT (True Knit) 0 hp | target SPHERIC_GUARDIAN, blocked 7, dealer player Moth, stack [THETAILOR-TRUE_KNIT]",
        "[F2 A1] Ironside applied 3 DOOM_POWER (Doom) | target TOADPOLE, applier player Ironside, stack [BLIGHT]",
        "[F2 A1] enemy applied 1 WEAK_POWER (Weak) | target TOADPOLE, applier TOADPOLE, stack []",
        "[F2 A1] Moth received 2 FRAIL_POWER (Frail) | applier CORPSE_SLUG",
        "[F2 A1] Ironside prevented 5 via WEAK_POWER (Weak) | CORPSE_SLUG hit Moth for 14.25 (x0.75, block 8)",
        "[F2 A1] Moth +9 bonus via VULNERABLE_POWER (Vulnerable) on Ironside's hit (x1.5, 18 hp)",
        "[F2 A1] Ironside <- Power:DOOM_POWER (Doom) 20 hp | target TOADPOLE, doom kill",
        "[F2 A1] fight end, saved",
        "--- resumed run SEED1:1789137571: 1 fights restored ---",
        "player 11 = Moth (The Tailor) #5c350f",
        "[F51 A4] fight start: The Corrupt Heart [boss]",
        "[F51 A4] Moth <- Card:THETAILOR-GOLD_AXE (Gold Axe) 30 hp | target THE_CORRUPT_HEART, blocked 0, dealer player Moth, stack [THETAILOR-GOLD_AXE]",
        "[F51 A4] fight end, saved",
        "[F51 A4] run ended: victory",
        "[F51 A4] run ended: defeat",
    };

    [Test]
    public static void ReplayRebuildsStatsWithTodaysRules()
    {
        LogReplay.Result r = LogReplay.Parse(Log, id => id == "UNLEASH" ? "Unleash" : null);
        Check.Equal("SEED1:1789137571", r.RunKey, "run key");
        Check.Equal(2, r.Players.Count, "players listed once, even after a resume");
        Check.Equal("5c350f", r.Players[0].ColorHex, "colour");
        PlayerTotals ironside = r.Stats.Get(22)!;
        Check.Equal(12 + 20, ironside.DamageDealt, "HP removed, doom kill included");
        Check.Equal(3, ironside.BlockRemoved, "block removed");
        Check.Equal(12, ironside.Sources["Pet:OSTY>UNLEASH"].Amount, "Osty via Unleash");
        Check.Equal("Osty via Unleash", ironside.Sources["Pet:OSTY>UNLEASH"].Label, "label from the title lookup");
        Check.Equal(30, r.Stats.Get(11)!.DamageDealt, "a hit fully into block adds no damage…");
        Check.Equal(7, r.Stats.Get(11)!.BlockRemoved, "…but counts as block removed");
        Check.Equal(3, ironside.DebuffsApplied["Power:DOOM_POWER"].Amount, "debuffs applied");
        Check.True(r.Stats.Players.Keys.All(k => k != RunStats.UnattributedKey), "enemy-applied debuffs are skipped");
        Check.Equal(2, r.Stats.Get(11)!.DebuffsReceived["Power:FRAIL_POWER"].Amount, "received");
        Check.Equal(5, ironside.DebuffPrevented["Power:WEAK_POWER"].Amount, "prevented");
        Check.Equal(9, r.Stats.Get(11)!.DebuffBonus["Power:VULNERABLE_POWER"].Amount, "bonus");
        Check.Equal(2, r.Stats.Fights.Count, "fights");
        Check.Equal(4, r.Stats.Fights[1].Act, "act 4");
        Check.Equal("The Corrupt Heart", r.Stats.Fights[1].Label, "room tag isn't part of the name");
        Check.Equal("boss", r.Stats.Fights[1].Room, "room");
        Check.Equal("", r.Stats.Fights[0].Room, "older logs have no room");
        Check.Equal(12 + 20, r.Stats.Fights[0].DamageByPlayer["22"], "fight damage dealt");
        Check.Equal<bool?>(true, r.Victory, "a victory isn't undone by a later defeat");
    }

    [Test]
    public static void ReplayReadsTheHeaderItWrites()
    {
        string header = LogReplay.HeaderLine("0.2.0", "SEED2:1789237700", new DateTime(2026, 9, 12, 19, 28, 0));
        Check.Equal("Who Carried v0.2.0 - run SEED2:1789237700 started 2026-09-12 19:28", header, "header");
        Check.Equal("SEED2:1789237700", LogReplay.Parse(new[] { header }).RunKey, "run key");
    }

    [Test]
    public static void PrettyTurnsIdsIntoNames()
    {
        Check.Equal("Unleash", LogReplay.Pretty("UNLEASH"), "plain");
        Check.Equal("Wild Strike", LogReplay.Pretty("INTOTHESPIREVERSE-WILD_STRIKE"), "modded prefix dropped");
    }

    [Test]
    public static void RunHistoryReadsDecksDefenseAndResult()
    {
        const string json = """
        {
          "win": true, "start_time": 1789137571, "run_time": 4899, "ascension": 6, "seed": "3YKUYH5798ZF",
          "map_point_history": [
            [ { "map_point_type": "ancient", "player_stats": [ { "player_id": 11, "damage_taken": 0, "hp_healed": 65 } ] },
              { "map_point_type": "monster", "player_stats": [ { "player_id": 11, "damage_taken": 7, "hp_healed": 3 } ] } ],
            [ { "map_point_type": "boss", "player_stats": [ { "player_id": 11, "damage_taken": 5, "hp_healed": 0 } ] } ]
          ],
          "players": [ { "id": 11, "character": "CHARACTER.THETAILOR-THE_TAILOR",
                         "deck": [ { "id": "CARD.STRIKE_TAILOR" }, { "id": "CARD.THETAILOR-SCRAP", "current_upgrade_level": 1 } ] } ]
        }
        """;
        RunHistory h = RunHistory.Parse(json);
        Check.True(h.Win, "win");
        Check.Equal(3, h.Floors, "floors across acts");
        Check.Equal(1789137571L, h.StartTime, "start");
        Check.Equal(4899L, h.RunTime, "run time");
        Check.Equal(6, h.Ascension, "ascension");
        Check.Equal("3YKUYH5798ZF", h.Seed, "seed");
        Check.Equal("monster", h.Rooms![2], "room by floor");
        Check.Equal("boss", h.Rooms![3], "floors count on across acts");
        RunHistory.PlayerRecord p = h.Players.Single();
        Check.Equal("CHARACTER.THETAILOR-THE_TAILOR", p.CharacterId, "character");
        Check.Equal(12, p.Taken, "damage taken");
        Check.Equal(3, p.Healed, "healed, starting HP on the first point excluded");
        Check.Equal("CARD.THETAILOR-SCRAP:1", $"{p.Deck[1].CardId}:{p.Deck[1].Upgrades}", "upgrades");
    }
}
