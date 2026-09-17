namespace Extractor.Dbc;

/// <summary>
/// Domain model for one ChrClasses.dbc row. NOTE: TalentTab's ClassMask is derived from
/// this row's <b>Id</b>, not PlayerClass — classMask = 1 &lt;&lt; (Id - 1) (e.g. Mage,
/// Id=8, gives classMask=128). PlayerClass is a boolean-ish "is a real player class" flag
/// (always 1 for all 9 vanilla classes); it is NOT the classMask shift amount despite the
/// name suggesting otherwise — confirmed empirically, see gotchas.md.
/// </summary>
public sealed record ChrClassRecord(
    int Id,
    int PlayerClass,
    string Name,
    string PetNameToken,
    string? Filename);
