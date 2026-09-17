using System.Security.Cryptography;
using Extractor.Mpq;

namespace Extractor.Blp;

public enum IconExtractionOutcome { CacheHit, Decoded, NotFound, UnsupportedFormat }

public sealed record IconExtractionResult(IconExtractionOutcome Outcome, string? IconPath);

/// <summary>
/// Full icon path: resolve extension -> read raw bytes -> skip decode if this exact
/// mpqPath+sourceHash was already seen (see IIconSourceStore) -> else decode BLP to PNG,
/// write it (deduplicated by content hash), and record it for next time.
///
/// Keeps an in-memory cache on top of IIconSourceStore, keyed by the requested texture
/// name: within one extraction run many talent ranks/tabs reference the SAME icon (measured
/// on a real build: 273 rank lookups needed only 105 distinct decodes), and IIconSourceStore
/// is normally a real database — without this, every repeat reference pays a network round
/// trip just to confirm "yes, still the same file", even though nothing after the first
/// lookup can have changed mid-run. This is what actually made a full-build extraction slow;
/// see .claude-docs/gotchas.md.
/// </summary>
public sealed class IconPipeline(MpqArchive interfaceArchive, IIconSourceStore store, string storageIconsDir)
{
    private readonly Dictionary<string, IconExtractionResult> inMemoryCache = new();

    public IconExtractionResult Extract(string textureFilenameNoExt)
    {
        if (inMemoryCache.TryGetValue(textureFilenameNoExt, out var cachedResult))
            return cachedResult;

        var result = ExtractUncached(textureFilenameNoExt);
        inMemoryCache[textureFilenameNoExt] = result;
        return result;
    }

    private IconExtractionResult ExtractUncached(string textureFilenameNoExt)
    {
        var resolvedPath = IconFileResolver.Resolve(interfaceArchive, textureFilenameNoExt);
        if (resolvedPath is null)
            return new IconExtractionResult(IconExtractionOutcome.NotFound, null);

        var rawBytes = interfaceArchive.ReadFile(resolvedPath);
        var sourceHash = Sha256Hex(rawBytes);

        var cached = store.TryGetIconPath(resolvedPath, sourceHash);
        if (cached is not null)
            return new IconExtractionResult(IconExtractionOutcome.CacheHit, cached);

        if (!resolvedPath.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
            return new IconExtractionResult(IconExtractionOutcome.UnsupportedFormat, null);

        var pngBytes = BlpConverter.ConvertToPng(rawBytes);
        var iconHash = Sha256Hex(pngBytes);

        Directory.CreateDirectory(storageIconsDir);
        var outPath = Path.Combine(storageIconsDir, $"{iconHash}.png");
        if (!File.Exists(outPath))
            File.WriteAllBytes(outPath, pngBytes);

        store.Upsert(resolvedPath, sourceHash, iconHash);
        return new IconExtractionResult(IconExtractionOutcome.Decoded, iconHash);
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
