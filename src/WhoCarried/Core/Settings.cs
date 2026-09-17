using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhoCarried.Core;

/// <summary>
/// The mod's settings. The hotkey is a Godot Key enum name ("F8") or an empty string when unbound;
/// it stays a string so Core remains independent of the game and engine assemblies.
/// </summary>
public sealed class Settings
{
    public const string DefaultHotkey = "F8";

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = DefaultHotkey;

    public static string PathIn(string dir) => Path.Combine(dir, "settings.json");

    /// <summary>Loads settings or defaults. A missing file is normal; other failures are returned to the caller.</summary>
    public static (Settings Settings, string? Error) Load(string dir)
    {
        try
        {
            string path = PathIn(dir);
            if (!File.Exists(path)) return (new Settings(), null);
            Settings? saved = JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.Settings);
            if (saved == null) return (new Settings(), "settings.json was empty");
            if (saved.Hotkey == null) return (new Settings(), "settings.json has a null hotkey");
            return (saved, null);
        }
        catch (Exception e)
        {
            return (new Settings(), e.Message);
        }
    }

    /// <summary>Writes settings through a replacement file. Returns an error instead of throwing.</summary>
    public static string? Save(Settings settings, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string path = PathIn(dir);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, WhoCarriedJson.Default.Settings));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
