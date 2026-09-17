using System.Text.Json;

namespace Extractor.Blp;

/// <summary>
/// Placeholder IIconSourceStore backed by a single JSON file — stands in for the real
/// MySQL icon_sources table until Phase 2 (Doctrine migrations) exists. Keyed by mpqPath;
/// each entry also carries the sourceHash that produced its iconPath, so a changed file at
/// the same path is detected as a miss rather than silently reusing a stale icon.
/// </summary>
public sealed class JsonFileIconSourceStore : IIconSourceStore
{
    private sealed record Entry(string SourceHash, string IconPath);

    private readonly string filePath;
    private readonly Dictionary<string, Entry> entries;

    public int Count => entries.Count;

    public JsonFileIconSourceStore(string filePath)
    {
        this.filePath = filePath;
        entries = File.Exists(filePath)
            ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(filePath)) ?? new()
            : new();
    }

    public string? TryGetIconPath(string mpqPath, string sourceHash)
    {
        if (entries.TryGetValue(mpqPath, out var entry) && entry.SourceHash == sourceHash)
            return entry.IconPath;
        return null;
    }

    public void Upsert(string mpqPath, string sourceHash, string iconPath)
    {
        entries[mpqPath] = new Entry(sourceHash, iconPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
