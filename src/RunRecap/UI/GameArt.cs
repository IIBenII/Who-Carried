using Godot;

namespace RunRecap.UI;

/// <summary>
/// The game's own UI art the recap is dressed in: the ancient card frame and banner, energy gems, the top bar, map
/// room icons, stats-screen icons. Loaded on first use and cached; a missing texture comes back as null and the
/// recap draws without it.
/// </summary>
internal static class GameArt
{
    private const string Ui = "res://images/atlases/ui_atlas.sprites/";
    private const string Map = "res://images/atlases/compressed.sprites/map/";
    private const string Stats = "res://images/packed/statistics_screen/";

    public const string Frame = "frame", Banner = "banner", Energy = "energy", TopBar = "top_bar", Floor = "floor",
        Timer = "timer", Ascension = "ascension", Heart = "heart", Deck = "deck", Swords = "swords", Trophy = "trophy",
        Cards = "cards", Achievements = "achievements", Share = "share", Block = "block", Skull = "skull", Brush = "brush",
        Dot = "dot", Monster = "monster", Elite = "elite", Boss = "boss", Unknown = "unknown", Perfect = "perfect";

    private static readonly Dictionary<string, string> Paths = new()
    {
        [Frame] = Ui + "card/card_frame_ancient_s.tres",
        [Banner] = Ui + "card/ancient_banner.tres",
        [Energy] = Ui + "card/energy_colorless.tres",
        [TopBar] = Ui + "top_bar/top_bar.tres",
        [Floor] = Ui + "top_bar/top_bar_floor.tres",
        [Timer] = Ui + "top_bar/timer_icon.tres",
        [Ascension] = Ui + "top_bar/top_bar_ascension.tres",
        [Heart] = Ui + "top_bar/top_bar_heart.tres",
        [Deck] = Ui + "top_bar/top_bar_deck.tres",
        [Swords] = Stats + "stats_swords.png",
        [Trophy] = Stats + "stats_trophy.png",
        [Cards] = Stats + "stats_cards.png",
        [Achievements] = Stats + "stats_achievements.png",
        [Share] = Stats + "share_stats.png",
        [Block] = "res://images/ui/combat/block.png",
        [Skull] = "res://images/ui/emote/skull.png",
        [Brush] = Map + "map_circle_0.tres",
        [Dot] = Map + "map_dot.tres",
        [Monster] = Ui + "map/icons/map_monster.tres",
        [Elite] = Ui + "map/icons/map_elite.tres",
        [Boss] = Ui + "map/icons/map_burly_monster.tres",
        [Unknown] = Ui + "map/icons/map_unknown.tres",
        [Perfect] = "res://images/ui/game_over_screen/badge_perfect.png",
    };

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    public static Texture2D? Get(string name)
    {
        // The game disposes pictures it unloads, which kills our handle too: look a dead one up again.
        if (Cache.TryGetValue(name, out Texture2D? cached) && (cached == null || GodotObject.IsInstanceValid(cached))) return cached;
        Texture2D? texture = null;
        try
        {
            if (Paths.TryGetValue(name, out string? path) && ResourceLoader.Exists(path))
                texture = Trim(ResourceLoader.Load<Texture2D>(path));
        }
        catch (Exception)
        {
            // drawn without it
        }
        Cache[name] = texture;
        return texture;
    }

    /// <summary>
    /// An atlas sprite without the transparent margin the atlas packer gives back (the game positions its sprites
    /// with it); we size the picture itself, so a map icon fills its box instead of sitting small in the middle.
    /// </summary>
    public static Texture2D? Trim(Texture2D? texture) =>
        texture is AtlasTexture atlas && atlas.Margin.Size != Vector2.Zero
            ? new AtlasTexture { Atlas = atlas.Atlas, Region = atlas.Region, FilterClip = atlas.FilterClip }
            : texture;

    /// <summary>The map icon for a fight's room; an unknown room gets the plain monster icon.</summary>
    public static Texture2D? Room(string room) => Get(room switch
    {
        "elite" => Elite,
        "boss" => Boss,
        "unknown" => Unknown,
        _ => Monster,
    });
}
