using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunStatsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.json");

    private static RunStats Sample(string key = "SEED:123")
    {
        var s = new RunStats { RunKey = key };
        s.BeginFight(1, 2, "Cultist");
        s.RecordDamage(1, new SourceRef(SourceKind.Power, "POISON_POWER", "Poison"), 9);
        s.EndFight();
        s.RecordBlocked(1, 3);
        return s;
    }

    [Test]
    public static void RoundTripKeepsEverything()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample(), path);
        RunStats? loaded = RunStatsStore.LoadIfResumable(path, "SEED:123");
        Check.True(loaded != null, "loaded");
        Check.Equal(9, loaded!.Get(1)!.DamageDealt, "damage");
        Check.Equal(3, loaded.Get(1)!.Blocked, "blocked");
        SourceTotal poison = loaded.Get(1)!.Sources["Power:POISON_POWER"];
        Check.Equal(SourceKind.Power, poison.Kind, "kind");
        Check.Equal("Poison", poison.Label, "label");
        Check.Equal(1, loaded.Fights.Count, "fights");
        Check.Equal("Cultist", loaded.Fights[0].Label, "fight label");
        Check.Equal(9, loaded.Fights[0].DamageByPlayer["1"], "fight damage");
    }

    [Test]
    public static void EnumsAreWrittenAsNames()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample(), path);
        Check.True(File.ReadAllText(path).Contains("\"Power\""), "kind written as \"Power\"");
    }

    [Test]
    public static void DifferentRunIsNotResumed()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample("SEED:123"), path);
        Check.True(RunStatsStore.LoadIfResumable(path, "OTHER:1") == null, "other run key ignored");
    }

    [Test]
    public static void FinishedRunIsNotResumed()
    {
        string path = TempFile();
        RunStats s = Sample();
        s.Finished = true;
        RunStatsStore.Save(s, path);
        Check.True(RunStatsStore.LoadIfResumable(path, "SEED:123") == null, "finished run ignored");
    }

    // In co-op a guest's new run is stamped with the guest's clock, but a loaded save carries the host's, so the
    // same run comes back with a different start time.
    [Test]
    public static void LoadedSaveResumesTheSameSeedDespiteANewStartTime()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample("SEED:1789237698"), path);
        RunStats? loaded = RunStatsStore.LoadIfResumable(path, "SEED:1789237700", loadedFromSave: true);
        Check.True(loaded != null, "resumed");
        Check.Equal(9, loaded!.Get(1)!.DamageDealt, "stats kept");
        Check.Equal("SEED:1789237700", loaded.RunKey, "takes the game's current key");
    }

    [Test]
    public static void NewRunOnTheSameSeedStartsFresh()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample("SEED:1789237698"), path);
        Check.True(RunStatsStore.LoadIfResumable(path, "SEED:1789240000", loadedFromSave: false) == null, "new run");
    }

    [Test]
    public static void LoadedSaveOfAnotherSeedOrAFinishedRunStartsFresh()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample("SEED:1789237698"), path);
        Check.True(RunStatsStore.LoadIfResumable(path, "OTHER:1789237700", loadedFromSave: true) == null, "other seed");
        RunStats finished = Sample("SEED:1789237698");
        finished.Finished = true;
        RunStatsStore.Save(finished, path);
        Check.True(RunStatsStore.LoadIfResumable(path, "SEED:1789237700", loadedFromSave: true) == null, "finished");
    }

    [Test]
    public static void CorruptOrMissingFileIsIgnored()
    {
        string path = TempFile();
        Check.True(RunStatsStore.LoadIfResumable(path, "SEED:123") == null, "missing file");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Check.True(RunStatsStore.LoadIfResumable(path, "SEED:123") == null, "corrupt file");
    }
}
