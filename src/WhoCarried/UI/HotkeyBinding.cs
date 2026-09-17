using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// The key that opens the recap. Core stores only its enum name so the setting remains engine-independent.
/// </summary>
internal static class HotkeyBinding
{
    private static Key _bound = Key.F8;
    private static Key _ignoredUntilReleased = Key.None;
    private static string _dataDir = "";
    private static bool _loaded;

    public static Key Bound => _bound;

    public static string? Name => _bound == Key.None ? null : _bound.ToString();

    /// <summary>Returns whether the hotkey is held, except for the press that just captured it.</summary>
    public static bool IsDown()
    {
        if (_bound == Key.None) return false;
        if (_ignoredUntilReleased == _bound)
        {
            if (Input.IsKeyPressed(_bound)) return false;
            _ignoredUntilReleased = Key.None;
        }
        return Input.IsKeyPressed(_bound);
    }

    /// <summary>Reads the current setting, falling back to F8 for an invalid value.</summary>
    public static void Load(string dataDir)
    {
        _dataDir = dataDir;
        _loaded = true;
        _ignoredUntilReleased = Key.None;
        try
        {
            (Settings settings, string? error) = Settings.Load(dataDir);
            if (error != null) Tracker.Note($"settings: {error}; using {Settings.DefaultHotkey}");
            _bound = Parse(settings.Hotkey, out bool unknown);
            if (unknown) Tracker.Note($"settings: don't know the key '{settings.Hotkey}'; using {Settings.DefaultHotkey}");
        }
        catch (Exception e)
        {
            _bound = Key.F8;
            Tracker.LogError("reading settings (using F8)", e);
        }
    }

    /// <summary>Binds a key (or none), saves it, and absorbs the press until its release.</summary>
    public static void Set(Key key)
    {
        _bound = key;
        _ignoredUntilReleased = key;
        try
        {
            if (!_loaded) return;
            string? error = Settings.Save(new Settings { Hotkey = key == Key.None ? "" : key.ToString() }, _dataDir);
            Tracker.Note(error == null ? $"hotkey set to {Name ?? "none"}" : $"hotkey set to {Name ?? "none"} but not saved: {error}");
        }
        catch (Exception e)
        {
            Tracker.LogError("saving the hotkey", e);
        }
    }

    private static Key Parse(string? name, out bool unknown)
    {
        unknown = false;
        if (string.IsNullOrEmpty(name)) return Key.None;
        if (Enum.TryParse(name, ignoreCase: true, out Key key) && Enum.IsDefined(typeof(Key), key)) return key;
        unknown = true;
        return Key.F8;
    }
}
