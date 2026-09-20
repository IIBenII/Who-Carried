using MegaCrit.Sts2.Core.Localization;

namespace WhoCarried.Game;

internal static class GameText
{
    public static string CardType(string type) => Native("gameplay_ui", "CARD_TYPE." + type.ToUpperInvariant(), type);
    public static string Relic => Native("gameplay_ui", "RELIC_RARITY.NONE", "Relic");
    public static string Block => Native("static_hover_tips", "BLOCK.title", "Block");
    public static string Native(string table, string key, string fallback)
    {
        try { return LocString.Exists(table, key) ? Title(new LocString(table, key), fallback) : fallback; }
        catch (Exception) { return fallback; }
    }

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
