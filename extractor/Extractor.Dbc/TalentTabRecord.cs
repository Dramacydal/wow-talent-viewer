namespace Extractor.Dbc;

/// <summary>
/// Domain model for one TalentTab.dbc row. TalentTab.dbc actually has THREE vanilla layouts
/// (not two like Talent.dbc, and the boundaries don't line up with Talent.dbc's):
///   0.7.0.3694-0.7.6.3712: no BackgroundFile, no OrderIndex
///   0.8.0.3734 (this build only): +BackgroundFile, still no OrderIndex
///   0.9.0.3807 onward: +OrderIndex too
/// See .claude-docs/gotchas.md.
/// </summary>
public sealed record TalentTabRecord(
    int Id,
    string Name,
    int SpellIconId,
    int RaceMask,
    int ClassMask,
    int? OrderIndex,
    string? BackgroundFile);
