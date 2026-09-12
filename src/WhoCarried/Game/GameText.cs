using MegaCrit.Sts2.Core.Localization;

namespace WhoCarried.Game;

internal static class GameText
{
    /// <summary>Localized text, or <paramref name="fallback"/> if it's missing or throws (common for modded content).</summary>
    public static string Title(LocString? loc, string fallback)
    {
        try
        {
            string? text = loc?.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
