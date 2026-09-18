namespace Extractor.Dbc;

/// <summary>
/// Domain model for one Spell.dbc row. Beyond name/description/icon, carries exactly the
/// fields needed to resolve the "$s1"-style escape sequences found in Description — see
/// SpellDescriptionFormatter and .claude-docs/gotchas.md for the escape-sequence catalog
/// (reverse-engineered from github.com/sidsukana/QSpellWork, verified against every
/// sequence that actually appears in our own extracted vanilla talent descriptions).
/// </summary>
public sealed record SpellRecord(
    int Id,
    string Name,
    string NameSubtext,
    string Description,
    int SpellIconId,
    int[] EffectBasePoints,
    float[] EffectAmplitude,
    int[] EffectRadiusIndex,
    int[] EffectMiscValue,
    float[] EffectPointsPerCombo,
    int DurationIndex,
    int ProcChance,
    int ProcCharges,
    int CumulativeAura,
    int? MaxTargetLevel,
    int[]? EffectChainTargets,
    int Attributes,
    int PowerType,
    int ManaCost,
    int RangeIndex,
    int CastingTimeIndex,
    int RecoveryTime,
    int CategoryRecoveryTime);

/// <summary>SpellIcon.dbc row — unchanged for the whole of vanilla. TextureFilename has no
/// extension and no "Interface\Icons\" prefix (e.g. "Spell_Fire_Fireball").</summary>
public sealed record SpellIconRecord(int Id, string TextureFilename);

/// <summary>SpellRange.dbc row. Flags bit 0x1 marks a melee-range entry (per WoWDBDefs'
/// column comment: "&1: Melee (ignore RangeMin, set to 0)") - the real client shows the
/// localized display name ("Melee Range") instead of a numeric yard range for these,
/// confirmed against real vanilla data (see gotchas.md).</summary>
public sealed record SpellRangeRecord(int Id, float MinRange, float MaxRange, int Flags)
{
    public bool IsMelee => (Flags & 0x1) != 0;
}
