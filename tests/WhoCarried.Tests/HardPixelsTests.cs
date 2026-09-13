using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>Turning a big smooth render into hard pixels: each block becomes its most common colour.</summary>
public static class HardPixelsTests
{
    private static readonly byte[] Red = { 200, 40, 30, 255 }, Blue = { 30, 60, 200, 255 }, Clear = { 0, 0, 0, 0 };

    /// <summary>An image from rows of pixels, each pixel 4 bytes.</summary>
    private static byte[] Image(params byte[][] pixels) => pixels.SelectMany(p => p).ToArray();

    private static byte[] Pixel(HardPixels.Result r, int x, int y) => r.Pixels.Skip((y * r.Width + x) * 4).Take(4).ToArray();

    [Test]
    public static void TheMostCommonColourInABlockWins()
    {
        HardPixels.Result r = HardPixels.Majority(Image(Red, Blue, Red, Red), 2, 2, 2);
        Check.Equal(1, r.Width, "width");
        Check.Equal(1, r.Height, "height");
        Check.True(Pixel(r, 0, 0).SequenceEqual(Red), "three reds beat one blue");
    }

    [Test]
    public static void NoColourAppearsThatWasntInItsBlock()
    {
        var rng = new Random(11);
        const int w = 24, h = 16, block = 4;
        var rgba = new byte[w * h * 4];
        rng.NextBytes(rgba);
        HardPixels.Result r = HardPixels.Majority(rgba, w, h, block);
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                byte[] got = Pixel(r, x, y);
                bool found = false;
                for (int dy = 0; dy < block && !found; dy++)
                    for (int dx = 0; dx < block && !found; dx++)
                    {
                        int i = ((y * block + dy) * w + x * block + dx) * 4;
                        byte[] source = rgba[i + 3] == 0 ? Clear : rgba[i..(i + 4)];
                        found = source.SequenceEqual(got);
                    }
                Check.True(found, $"pixel {x},{y} came from its own block");
            }
    }

    [Test]
    public static void SeeThroughPixelsVoteTogetherWhateverColourTheyCarry()
    {
        // Renderers leave junk RGB in fully transparent pixels. Counted apart, the blue would win this tie on order.
        byte[] junk1 = { 9, 9, 9, 0 }, junk2 = { 200, 0, 0, 0 }, junk3 = { 0, 0, 77, 0 };
        HardPixels.Result r = HardPixels.Majority(Image(Blue, junk1, junk2, junk3), 2, 2, 2);
        Check.True(Pixel(r, 0, 0).SequenceEqual(Clear), "three see-through pixels win, as plain transparent");
    }

    [Test]
    public static void LeftoverRowsAndColumnsAreDropped()
    {
        var rgba = new byte[5 * 7 * 4];
        HardPixels.Result r = HardPixels.Majority(rgba, 5, 7, 2);
        Check.Equal(2, r.Width, "width");
        Check.Equal(3, r.Height, "height");
        Check.Equal(2 * 3 * 4, r.Pixels.Length, "bytes");
    }

    [Test]
    public static void ATieGoesToTheColourThatReachedTheTopCountFirst()
    {
        // Reading order red, blue, blue, red: blue gets to two first.
        HardPixels.Result r = HardPixels.Majority(Image(Red, Blue, Blue, Red), 2, 2, 2);
        Check.True(Pixel(r, 0, 0).SequenceEqual(Blue), "blue");
    }

    [Test]
    public static void AnImageOfTheWrongSizeIsRefused()
    {
        bool threw = false;
        try { HardPixels.Majority(new byte[10], 2, 2, 2); }
        catch (ArgumentException) { threw = true; }
        Check.True(threw, "10 bytes isn't a 2x2 RGBA image");
    }
}
