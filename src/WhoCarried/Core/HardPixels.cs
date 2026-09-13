namespace WhoCarried.Core;

/// <summary>
/// Hard-edged pixel art from a big smooth render: each block of pixels becomes the one colour that covers most of it.
/// Unlike averaging, it never blends two colours into a new one, so edges come out stepped like hand-placed pixels.
/// </summary>
public static class HardPixels
{
    public readonly record struct Result(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// Shrinks <paramref name="rgba"/> (RGBA8, <paramref name="width"/>×<paramref name="height"/>) by
    /// <paramref name="block"/> in each direction; rows and columns that don't fill a whole block are dropped. Fully
    /// transparent pixels count as one colour whatever RGB they carry. On a tie, the colour that reached the top count
    /// first in reading order wins.
    /// </summary>
    public static Result Majority(byte[] rgba, int width, int height, int block)
    {
        if (width <= 0 || height <= 0 || block <= 0 || rgba.Length != width * height * 4)
            throw new ArgumentException($"expected a {width}x{height} RGBA image ({width * height * 4} bytes), got {rgba.Length} bytes");
        int w = width / block, h = height / block;
        var pixels = new byte[w * h * 4];
        var counts = new Dictionary<uint, int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                counts.Clear();
                uint best = 0;
                int bestCount = 0;
                for (int dy = 0; dy < block; dy++)
                    for (int dx = 0; dx < block; dx++)
                    {
                        int i = ((y * block + dy) * width + x * block + dx) * 4;
                        uint colour = rgba[i + 3] == 0 ? 0u : (uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]);
                        int n = counts.TryGetValue(colour, out int seen) ? seen + 1 : 1;
                        counts[colour] = n;
                        if (n > bestCount)
                        {
                            bestCount = n;
                            best = colour;
                        }
                    }
                int o = (y * w + x) * 4;
                pixels[o] = (byte)(best >> 24);
                pixels[o + 1] = (byte)(best >> 16);
                pixels[o + 2] = (byte)(best >> 8);
                pixels[o + 3] = (byte)best;
            }
        return new Result(pixels, w, h);
    }
}
