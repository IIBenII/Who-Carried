using Godot;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>Renders a control offscreen in a SubViewport and hands back the image, or saves it as a PNG. Never throws.</summary>
internal static class PngExporter
{
    /// <summary>Where images go when Steam isn't running: beside the mod's other files in the game's save folder.</summary>
    public static string FallbackFolder => Path.Combine(Tracker.DataDir, "images");

    /// <param name="onDone">Called with null on success, or an error message.</param>
    public static void Save(Control content, int width, string path, Action<string?> onDone)
    {
        Render(content, width, (image, error) =>
        {
            if (image == null)
            {
                onDone(error);
                return;
            }
            onDone(SavePng(image, path));
        });
    }

    /// <summary>Saves the image, making its folder if needed. Returns null on success, or an error message.</summary>
    public static string? SavePng(Image image, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Error result = image.SavePng(path);
            return result == Error.Ok ? null : result.ToString();
        }
        catch (Exception e)
        {
            Tracker.LogError("export", e);
            return e.Message;
        }
    }

    /// <param name="onDone">Called with the image, or null and an error message.</param>
    public static void Render(Control content, int width, Action<Image?, string?> onDone)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, 4096),
            TransparentBg = true,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewport.AddChild(content);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);

        // Frame 1: layout runs and gives us the content height. Frame 2: render at that height, then read back.
        Later.Run(0.15, () =>
        {
            int height = Mathf.CeilToInt(Math.Max(content.Size.Y, content.GetCombinedMinimumSize().Y));
            viewport.Size = new Vector2I(width, Math.Clamp(height, 1, 8192));
            Later.Run(0.15, () =>
            {
                Image? image = null;
                string? error = null;
                try
                {
                    image = viewport.GetTexture().GetImage();
                    if (image == null) error = "no image";
                }
                catch (Exception e)
                {
                    error = e.Message;
                    Tracker.LogError("export", e);
                }
                viewport.QueueFree();
                onDone(image, error);
            });
        });
    }
}
