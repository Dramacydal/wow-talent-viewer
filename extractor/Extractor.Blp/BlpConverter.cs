using BLPSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Extractor.Blp;

/// <summary>
/// BLP -> PNG, mipmap 0 (full resolution) only — talent icons don't need mip chains.
/// BLPSharp decodes to raw BGRA32 pixels; SixLabors.ImageSharp encodes those to PNG (pure
/// managed, cross-platform, no native asset to vendor — unlike SkiaSharp; license is the
/// Six Labors Split License, free for this project's scale, see architecture.md).
/// </summary>
public static class BlpConverter
{
    public static byte[] ConvertToPng(byte[] blpBytes)
    {
        using var input = new MemoryStream(blpBytes);
        using var blp = new BLPFile(input);
        var pixels = blp.GetPixels(0, out var width, out var height);

        using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}
