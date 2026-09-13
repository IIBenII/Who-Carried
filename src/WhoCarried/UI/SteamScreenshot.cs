using Godot;
using MegaCrit.Sts2.Core.Platform;
using Steamworks;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Puts exported images in the player's Steam screenshot library, the way the game's own map Share button does. Works
/// the same on every OS, and on a Steam Deck the image shows up in Game Mode's screenshots.
/// </summary>
internal static class SteamScreenshot
{
    private const double AnswerTimeout = 15;

    private static Callback<ScreenshotReady_t>? _ready;
    private static readonly Dictionary<ScreenshotHandle, Action<string?>> Waiting = new();

    /// <summary>True when the game is running on Steam (it isn't when started without Steam).</summary>
    public static bool Available => PlatformUtil.PrimaryPlatform == PlatformType.Steam;

    /// <summary>
    /// Hands Steam the raw pixels, as the game's map Share button does. Steam stores its own JPEG (it re-encodes a PNG
    /// given as a file too, to the byte, so there's nothing to gain from writing one first).
    /// </summary>
    /// <param name="caption">Shown with the screenshot in Steam.</param>
    /// <param name="onDone">Called with null once Steam has it, or an error message.</param>
    public static void Write(Image image, string caption, Action<string?> onDone)
    {
        try
        {
            Image rgb = (Image)image.Duplicate();
            if (rgb.GetFormat() != Image.Format.Rgb8) rgb.Convert(Image.Format.Rgb8);
            byte[] data = rgb.GetData();
            Track(SteamScreenshots.WriteScreenshot(data, (uint)data.Length, rgb.GetWidth(), rgb.GetHeight()), caption, onDone);
        }
        catch (Exception e)
        {
            Tracker.LogError("steam screenshot", e);
            onDone(e.Message);
        }
    }

    private static void Track(ScreenshotHandle handle, string caption, Action<string?> onDone)
    {
        if (handle == ScreenshotHandle.Invalid)
        {
            onDone("Steam didn't take the image");
            return;
        }
        _ready ??= Callback<ScreenshotReady_t>.Create(OnReady);
        SteamScreenshots.SetLocation(handle, caption);
        Waiting[handle] = onDone;
        Later.Run(AnswerTimeout, () =>
        {
            if (Waiting.Remove(handle, out Action<string?>? late)) late("no answer from Steam");
        });
    }

    private static void OnReady(ScreenshotReady_t ready)
    {
        if (!Waiting.Remove(ready.m_hLocal, out Action<string?>? done)) return;
        done(ready.m_eResult == EResult.k_EResultOK ? null : ready.m_eResult.ToString());
    }
}
