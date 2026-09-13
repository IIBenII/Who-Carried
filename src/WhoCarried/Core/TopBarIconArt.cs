namespace WhoCarried.Core;

/// <summary>
/// The top-bar button's icon. Its SVG canvas is <see cref="Width"/>×<see cref="Height"/> units, one unit per screen
/// pixel at 1080p (the Map icon's box). The game's SVG renderer draws it at <see cref="RenderScale"/>, and every
/// <see cref="Block"/>×<see cref="Block"/> block becomes one hard pixel: art at <see cref="ArtScale"/>× with stepped
/// edges, like the game's own icons (the Map's art is 114×104), which the game then shrinks the same way.
/// </summary>
public static class TopBarIconArt
{
    public const int Width = 72, Height = 64, Block = 4;
    public const float ArtScale = 1.625f;
    public const float RenderScale = ArtScale * Block;

    public static int RenderWidth => (int)MathF.Round(Width * RenderScale);
    public static int RenderHeight => (int)MathF.Round(Height * RenderScale);
    public static int ArtWidth => RenderWidth / Block;
    public static int ArtHeight => RenderHeight / Block;

    /// <summary>The game's top-bar buttons are 80×80; the icon box sits at y 8–72 like the Map's.</summary>
    public const float Slot = 80f, BoxTop = 8f;

    /// <summary>Hover and press, as the game's top-bar buttons do them (no tilt: the owner wants it straight).</summary>
    public const float HoverGrow = 1.1f, HoverBright = 1.1f, PressDim = 0.4f, SettleSeconds = 1f;

    public const string SvgResource = "WhoCarried.TopBarIcon.svg";
}
