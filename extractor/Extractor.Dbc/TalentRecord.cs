namespace Extractor.Dbc;

/// <summary>
/// Domain model for one Talent.dbc row, normalized across both vanilla layouts
/// (pre-0.10.0.3892 lacks RequiredSpellId — see .claude-docs/gotchas.md).
/// </summary>
public sealed record TalentRecord(
    int Id,
    int TabId,
    int Tier,
    int ColumnIndex,
    int[] SpellRanks,
    int[] PrereqTalent,
    int[] PrereqRank,
    int Flags,
    int? RequiredSpellId);
