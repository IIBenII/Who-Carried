using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class SettingsTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"));

    [Test]
    public static void DefaultsToF8WithoutAFile()
    {
        (Settings settings, string? error) = Settings.Load(NewDir());

        Check.Equal("F8", settings.Hotkey, "hotkey");
        Check.Equal<string?>(null, error, "error");
    }

    [Test]
    public static void SavesAndLoadsAKey()
    {
        string dir = NewDir();

        string? saveError = Settings.Save(new Settings { Hotkey = "F9" }, dir);
        (Settings settings, string? loadError) = Settings.Load(dir);

        Check.Equal<string?>(null, saveError, "save error");
        Check.Equal<string?>(null, loadError, "load error");
        Check.Equal("F9", settings.Hotkey, "hotkey");
    }

    [Test]
    public static void KeepsAnUnboundHotkey()
    {
        string dir = NewDir();

        Settings.Save(new Settings { Hotkey = "" }, dir);
        (Settings settings, _) = Settings.Load(dir);

        Check.Equal("", settings.Hotkey, "hotkey");
    }

    [Test]
    public static void FallsBackAndReportsWhenTheFileIsMalformed()
    {
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ not json");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal("F8", settings.Hotkey, "hotkey");
        Check.True(error != null, "reported the problem");
    }

    [Test]
    public static void FallsBackAndReportsWhenTheHotkeyIsNull()
    {
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": null }");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal("F8", settings.Hotkey, "hotkey");
        Check.True(error != null, "reported the problem");
    }

    [Test]
    public static void KeepsAKeyNameItDoesNotRecognise()
    {
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": \"Banana\" }");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal("Banana", settings.Hotkey, "hotkey");
        Check.Equal<string?>(null, error, "error");
    }

    [Test]
    public static void WritesTheFileWhereItSaysItDoes()
    {
        string dir = NewDir();

        Settings.Save(new Settings { Hotkey = "F7" }, dir);

        Check.True(File.Exists(Path.Combine(dir, "settings.json")), "settings.json exists");
    }
}
