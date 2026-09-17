using System.Text.RegularExpressions;

namespace Extractor.Mpq;

/// <summary>
/// Discovers and applies a client's patch-N.MPQ chain. Two things that look like edge
/// cases but are real, observed data: the number of patches is NOT fixed at 2
/// (patch.MPQ + patch-2.MPQ) — later clients/private servers can have patch-3.MPQ and
/// beyond; and the filename case is inconsistent across clients (some ship "patch.mpq"
/// lowercase) — matters because the filesystem here is case-sensitive even though the
/// game itself isn't. See .claude-docs/gotchas.md.
/// </summary>
public static partial class PatchChain
{
    [GeneratedRegex(@"^patch(?:-(\d+))?\.mpq$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchFileRegex();

    /// <summary>Applies every patch-N.MPQ found in dataDir to the archive, in ascending order
    /// (bare "patch.mpq" first, then patch-2, patch-3, ... however many exist).</summary>
    public static void ApplyAll(MpqArchive archive, string dataDir)
    {
        foreach (var path in DiscoverInOrder(dataDir))
            archive.ApplyPatch(path);
    }

    public static IReadOnlyList<string> DiscoverInOrder(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            return [];

        return Directory.EnumerateFiles(dataDir)
            .Select(path => (Path: path, Match: PatchFileRegex().Match(Path.GetFileName(path))))
            .Where(x => x.Match.Success)
            .Select(x => (x.Path, Number: x.Match.Groups[1].Success ? int.Parse(x.Match.Groups[1].Value) : 0))
            .OrderBy(x => x.Number)
            .Select(x => x.Path)
            .ToList();
    }
}
