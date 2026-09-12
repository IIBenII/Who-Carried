using Godot;
using RunRecap.Game;

namespace RunRecap.UI;

/// <summary>Renders a control offscreen in a SubViewport and saves it as a PNG. Never throws.</summary>
internal static class PngExporter
{
    /// <param name="onDone">Called with null on success, or an error message.</param>
    public static void Save(Control content, int width, string path, Action<string?> onDone)
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
                string? error = null;
                try
                {
                    Image image = viewport.GetTexture().GetImage();
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    Error result = image.SavePng(path);
                    if (result != Error.Ok) error = result.ToString();
                }
                catch (Exception e)
                {
                    error = e.Message;
                    Tracker.LogError("export", e);
                }
                viewport.QueueFree();
                onDone(error);
            });
        });
    }
}
