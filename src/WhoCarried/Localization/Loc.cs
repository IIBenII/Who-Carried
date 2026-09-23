using System.Globalization;
using System.Text.Json;

namespace WhoCarried.Localization;

/// <summary>Small, game-independent formatting boundary. The game supplies lookup and culture.</summary>
public static class Loc
{
    public static Func<string, string?>? Lookup { get; set; }
    public static Func<CultureInfo>? CurrentCulture { get; set; }
    public static CultureInfo Culture => CurrentCulture?.Invoke() ?? CultureInfo.InvariantCulture;
    private static readonly Dictionary<string, string> English = Read("eng");

    public static Dictionary<string, string> Read(string language)
    {
        using Stream? stream = typeof(Loc).Assembly.GetManifestResourceStream($"WhoCarried.Localization.{language}.json");
        if (stream == null) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string Text(string key, params object[] args)
    {
        string? translated = null;
        try { translated = Lookup?.Invoke(key); }
        catch (Exception) { /* Game tables may not be ready during startup. */ }
        foreach (string? template in new[] { translated, English.GetValueOrDefault(key) })
        {
            if (string.IsNullOrWhiteSpace(template)) continue;
            try { return string.Format(Culture, template, args); }
            catch (FormatException) { /* A broken translation must not hide the panel. */ }
        }
        return $"[{key}]";
    }
}
