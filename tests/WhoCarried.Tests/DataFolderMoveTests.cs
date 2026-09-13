using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class DataFolderMoveTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"));

    private static void Write(string path, string text, DateTime writtenUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    [Test]
    public static void MovesEveryFileAndRemovesTheOldFolder()
    {
        string root = NewRoot();
        string from = Path.Combine(root, "mod", "data"), to = Path.Combine(root, "user", "WhoCarried");
        Write(Path.Combine(from, "current_run.dat"), "stats", DateTime.UtcNow);
        Write(Path.Combine(from, "images", "run.png"), "png", DateTime.UtcNow);

        (int moved, string? error) = DataFolderMove.Run(from, to);

        Check.Equal(2, moved, "files moved");
        Check.Equal<string?>(null, error, "error");
        Check.Equal("stats", File.ReadAllText(Path.Combine(to, "current_run.dat")), "stats file");
        Check.Equal("png", File.ReadAllText(Path.Combine(to, "images", "run.png")), "nested file");
        Check.True(!Directory.Exists(from), "old folder removed");
    }

    [Test]
    public static void KeepsANewerFileAlreadyInTheNewFolder()
    {
        string root = NewRoot();
        string from = Path.Combine(root, "old"), to = Path.Combine(root, "new");
        Write(Path.Combine(from, "events.log"), "old log", DateTime.UtcNow.AddHours(-2));
        Write(Path.Combine(to, "events.log"), "new log", DateTime.UtcNow);

        (int moved, _) = DataFolderMove.Run(from, to);

        Check.Equal(0, moved, "files moved");
        Check.Equal("new log", File.ReadAllText(Path.Combine(to, "events.log")), "kept the newer log");
        Check.True(File.Exists(Path.Combine(from, "events.log")), "left the older copy where it was");
    }

    [Test]
    public static void ReplacesAnOlderFileInTheNewFolder()
    {
        string root = NewRoot();
        string from = Path.Combine(root, "old"), to = Path.Combine(root, "new");
        Write(Path.Combine(from, "current_run.dat"), "newer stats", DateTime.UtcNow);
        Write(Path.Combine(to, "current_run.dat"), "older stats", DateTime.UtcNow.AddHours(-2));

        (int moved, _) = DataFolderMove.Run(from, to);

        Check.Equal(1, moved, "files moved");
        Check.Equal("newer stats", File.ReadAllText(Path.Combine(to, "current_run.dat")), "took the newer stats");
        Check.True(!Directory.Exists(from), "old folder removed");
    }

    [Test]
    public static void DoesNothingWithoutAnOldFolder()
    {
        string root = NewRoot();
        (int moved, string? error) = DataFolderMove.Run(Path.Combine(root, "missing"), Path.Combine(root, "new"));
        Check.Equal(0, moved, "files moved");
        Check.Equal<string?>(null, error, "error");
        Check.True(!Directory.Exists(Path.Combine(root, "new")), "new folder not made for nothing");
    }
}
