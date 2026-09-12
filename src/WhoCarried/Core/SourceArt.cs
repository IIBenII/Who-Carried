namespace WhoCarried.Core;

/// <summary>
/// Which picture stands for a damage source: a card's portrait, a relic's, potion's, power's or orb's icon. A pet's
/// hit shows the card that sent it in ("Osty via Unleash" → Unleash). Keys are resolved to textures by the game layer.
/// </summary>
public static class SourceArt
{
    public const string Card = "card:", Relic = "relic:", Potion = "potion:", Orb = "orb:";

    /// <summary>A kind of content only a mod has: its picture comes from the model itself.</summary>
    public const string Model = "model:";

    /// <summary>
    /// A readable name for an id whose own name couldn't be found: "SOMEMOD-FLAMMA_RUNE" → "Flamma Rune", or "Flamma"
    /// when <paramref name="kindWord"/> is "Rune" (the content's own kind, which would only repeat). A mod's prefix
    /// before the first "-" goes; the rest is title-cased.
    /// </summary>
    public static string Readable(string id, string? kindWord = null)
    {
        string name = id.Contains('-') ? id[(id.IndexOf('-') + 1)..] : id;
        List<string> words = name.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && !string.IsNullOrEmpty(kindWord) && string.Equals(words[^1], kindWord, StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    /// <param name="sourceKey">A <see cref="SourceRef.Key"/>, e.g. "Card:SOVEREIGN_BLADE" or "Pet:OSTY>UNLEASH".</param>
    public static string? Key(string sourceKey)
    {
        int colon = sourceKey.IndexOf(':');
        if (colon < 0) return null;
        string kind = sourceKey[..colon], id = sourceKey[(colon + 1)..];
        if (id.Length == 0) return null;
        return kind switch
        {
            nameof(SourceKind.Card) => Card + id,
            nameof(SourceKind.Relic) => Relic + id,
            nameof(SourceKind.Potion) => Potion + id,
            nameof(SourceKind.Power) => DebuffBuilder.IconPrefix + id,
            nameof(SourceKind.Orb) => Orb + id,
            nameof(SourceKind.Other) => Model + id,
            nameof(SourceKind.Pet) when id.Contains(Attribution.ViaSeparator) =>
                Card + id[(id.IndexOf(Attribution.ViaSeparator) + 1)..],
            _ => null,
        };
    }
}
