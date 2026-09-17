namespace Extractor.Dbc;

/// <summary>
/// Domain model for one Spell.dbc row — only the fields relevant to displaying a talent
/// rank (name, "Rank N" subtitle, tooltip text, icon reference). Spell.dbc itself has
/// dozens of unrelated combat-mechanics columns we don't touch.
/// </summary>
public sealed record SpellRecord(
    int Id,
    string Name,
    string NameSubtext,
    string Description,
    int SpellIconId);

/// <summary>SpellIcon.dbc row — unchanged for the whole of vanilla. TextureFilename has no
/// extension and no "Interface\Icons\" prefix (e.g. "Spell_Fire_Fireball").</summary>
public sealed record SpellIconRecord(int Id, string TextureFilename);
