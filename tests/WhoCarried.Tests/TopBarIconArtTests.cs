using System.Xml.Linq;
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The top-bar icon's numbers, and the SVG they describe.</summary>
public static class TopBarIconArtTests
{
    [Test]
    public static void TheBigRenderSplitsIntoWholeBlocks()
    {
        Check.Equal(468, TopBarIconArt.RenderWidth, "render width");
        Check.Equal(416, TopBarIconArt.RenderHeight, "render height");
        Check.Equal(0, TopBarIconArt.RenderWidth % TopBarIconArt.Block, "no leftover columns");
        Check.Equal(0, TopBarIconArt.RenderHeight % TopBarIconArt.Block, "no leftover rows");
        Check.Equal(117, TopBarIconArt.ArtWidth, "art width");
        Check.Equal(104, TopBarIconArt.ArtHeight, "art height, like the Map's 104");
    }

    [Test]
    public static void TheArtFillsTheMapsBoxAtOnePixelPerUnit()
    {
        float k = Math.Min(TopBarIconArt.Width / (float)TopBarIconArt.ArtWidth, TopBarIconArt.Height / (float)TopBarIconArt.ArtHeight);
        Check.Near(TopBarIconArt.Width, TopBarIconArt.ArtWidth * k, "drawn width", tolerance: 0.5);
        Check.Near(TopBarIconArt.Height, TopBarIconArt.ArtHeight * k, "drawn height", tolerance: 0.5);
        Check.Near(1.0, k * TopBarIconArt.ArtScale, "one art unit per screen pixel at 1080p", tolerance: 0.01);
        Check.Near(TopBarIconArt.Slot, TopBarIconArt.BoxTop * 2 + TopBarIconArt.Height, "the box is centred in the slot");
    }

    [Test]
    public static void TheSvgCanvasMatchesAndUsesOnlyShapes()
    {
        XElement svg = XElement.Load(SvgPath());
        Check.Equal($"0 0 {TopBarIconArt.Width} {TopBarIconArt.Height}", (string?)svg.Attribute("viewBox"), "viewBox");
        // Text needs fonts the game's SVG renderer doesn't have; images would mean embedded art; filters don't render.
        string[] banned = { "text", "image", "filter", "use" };
        foreach (XElement e in svg.Descendants())
            Check.True(!banned.Contains(e.Name.LocalName), $"<{e.Name.LocalName}> in the icon");
    }

    private static string SvgPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WhoCarried.sln")))
                return Path.Combine(dir.FullName, "src", "WhoCarried", "UI", "Art", "TopBarIcon.svg");
        throw new CheckFailed("couldn't find the repo root above " + AppContext.BaseDirectory);
    }
}
