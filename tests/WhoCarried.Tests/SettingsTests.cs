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
        Check.Equal("", settings.ShareUrl, "share url stays empty");
    }

    [Test]
    public static void SavesAndLoadsTheShareUrl()
    {
        string dir = NewDir();

        string? saveError = Settings.Save(new Settings { Hotkey = "F8", ShareUrl = "http://localhost:3000" }, dir);
        (Settings settings, string? loadError) = Settings.Load(dir);

        Check.Equal<string?>(null, saveError, "save error");
        Check.Equal<string?>(null, loadError, "load error");
        Check.Equal("http://localhost:3000", settings.ShareUrl, "share url");
        Check.Equal("F8", settings.Hotkey, "hotkey");
    }

    [Test]
    public static void AnOlderSettingsFileHasNoShareUrl()
    {
        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": \"F8\" }");

        (Settings settings, string? error) = Settings.Load(dir);

        Check.Equal<string?>(null, error, "error");
        Check.Equal("", settings.ShareUrl, "share url");
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

    [Test]
    public static void EffectSourcesAreOffUnlessTurnedOn()
    {
        Check.True(!Settings.Load(NewDir()).Settings.ExperimentalEffectSources, "no file");

        string dir = NewDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": \"F9\" }");
        Check.True(!Settings.Load(dir).Settings.ExperimentalEffectSources, "a file from before the setting");

        File.WriteAllText(Settings.PathIn(dir), "{ \"hotkey\": \"F9\", \"experimentalEffectSources\": true }");
        (Settings on, string? error) = Settings.Load(dir);
        Check.Equal<string?>(null, error, "error");
        Check.True(on.ExperimentalEffectSources, "turned on by hand");
        Check.Equal("F9", on.Hotkey, "hotkey kept");
    }

    [Test]
    public static void ChangingTheHotkeyKeepsTheOtherSettings()
    {
        string dir = NewDir();
        Settings.Save(new Settings { Hotkey = "F8", ExperimentalEffectSources = true }, dir);
        Settings loaded = Settings.Load(dir).Settings;

        Settings rebound = loaded.WithHotkey("F10");
        Settings.Save(rebound, dir);

        Check.Equal("F10", rebound.Hotkey, "new key");
        Check.Equal("F8", loaded.Hotkey, "the original is left alone");
        Check.True(Settings.Load(dir).Settings.ExperimentalEffectSources, "still on after a rebind");
        Check.Equal("", Settings.Load(dir).Settings.ShareUrl, "share url left empty");
    }

    [Test]
    public static void ChangingTheHotkeyKeepsTheShareUrl()
    {
        string dir = NewDir();
        Settings.Save(new Settings { Hotkey = "F8", ShareUrl = "http://localhost:3000" }, dir);

        Settings.Save(Settings.Load(dir).Settings.WithHotkey("F10"), dir);

        Check.Equal("F10", Settings.Load(dir).Settings.Hotkey, "new key");
        Check.Equal("http://localhost:3000", Settings.Load(dir).Settings.ShareUrl, "share url kept");
    }
}
