namespace Extractor.Blp;

/// <summary>
/// Global (not per-build) cache of icon identity between extractor runs — see
/// .claude-docs/architecture.md's icon_sources table. Keyed by the MPQ internal path
/// (e.g. "Interface\Icons\Spell_Fire_FlameBolt.blp") plus the sha256 of that file's raw
/// bytes, so an unchanged icon across builds is recognized without decoding it again.
/// The real backing store will be the MySQL icon_sources table (Phase 2); this interface
/// lets IconPipeline stay unchanged when that lands.
/// </summary>
public interface IIconSourceStore
{
    /// <summary>Returns the cached icon_path (PNG content hash) if this exact mpqPath+sourceHash
    /// combination was seen before, else null (new file, or content changed since last time).</summary>
    string? TryGetIconPath(string mpqPath, string sourceHash);

    void Upsert(string mpqPath, string sourceHash, string iconPath);
}
