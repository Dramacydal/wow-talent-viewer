namespace Extractor.Mpq;

/// <summary>
/// SpellIcon.dbc's TextureFilename has no extension (e.g. "Interface\Icons\Spell_Fire_FlameBolt").
/// Empirically every client from 0.5.5.3494 through 1.12.1.5875 stores icons as .blp — but
/// don't hardcode that: WoW's early history used .tga for some textures before standardizing
/// on the custom BLP format, and we haven't checked every client in existence. Ask the archive
/// what's actually there instead of assuming.
/// </summary>
public static class IconFileResolver
{
    private static readonly string[] CandidateExtensions = [".blp", ".tga"];

    /// <summary>Returns the real internal path for a texture filename (no extension), or null
    /// if none of the candidate extensions exist in the archive.</summary>
    public static string? Resolve(MpqArchive archive, string textureFilenameNoExt)
    {
        foreach (var ext in CandidateExtensions)
        {
            var candidate = textureFilenameNoExt + ext;
            if (archive.HasFile(candidate))
                return candidate;
        }
        return null;
    }
}
