using DBCD.Providers;
using Extractor.Mpq;

namespace Extractor.Dbc;

/// <summary>
/// Thin wrapper around DBCD, schema-driven from vendored WoWDBDefs (see dbd-definitions/,
/// pinned to a specific WoWDBDefs commit — .claude-docs/architecture.md). Reads directly
/// from an MpqArchive, no on-disk extraction step.
/// </summary>
public sealed class DbcClient
{
    private readonly global::DBCD.DBCD dbcd;
    private readonly Dictionary<string, global::DBCD.IDBCDStorage> storageCache = new();

    // DBCD.IO bug/quirk for "inline" ($id$, not $noninline,id$) primary keys, which is what
    // every vanilla WDBC table uses: WDBCRow.Id (and therefore the storage dictionary's key,
    // and DBCDRow.ID) is set to the *physical record index + 1*, NOT the real parsed ID field
    // value — the real value only overwrites it for $noninline,id$ fields (a WDC-family
    // concept, irrelevant to WDBC). The real ID is still readable as a normal named field via
    // row.Field<int>("ID"); it's just never used as the dictionary key. So storage[id] /
    // storage.TryGetValue(id, ...) and row.ID are both unreliable for vanilla tables — build
    // our own id -> row index instead. Confirmed on real data (extractor/Extractor.Cli
    // raw-talent-by-id): Talent.dbc's physically-first record has real ID=26, but DBCD's
    // dictionary key / row.ID for it was 1. See .claude-docs/gotchas.md.
    private readonly Dictionary<string, Dictionary<int, global::DBCD.DBCDRow>> idIndexCache = new();

    public DbcClient(MpqArchive archive, string definitionsDirectory)
    {
        var dbcProvider = new MpqDbcProvider(archive);
        var dbdProvider = new FilesystemDBDProvider(definitionsDirectory);
        dbcd = new global::DBCD.DBCD(dbcProvider, dbdProvider);
    }

    /// <summary>Loads (or returns the cached load of) a table for this build. Avoids re-parsing
    /// the whole table — e.g. Spell.dbc — once per row we look up.</summary>
    private global::DBCD.IDBCDStorage Load(string tableName, string build, global::DBCD.Locale locale = global::DBCD.Locale.None)
    {
        var key = $"{tableName}|{build}|{locale}";
        if (!storageCache.TryGetValue(key, out var storage))
        {
            storage = dbcd.Load(tableName, build, locale);
            storageCache[key] = storage;
        }
        return storage;
    }

    /// <summary>The correct id -> row map, keyed by the real "ID" field (see class remarks),
    /// not by storage's own (broken, for inline-id WDBC tables) dictionary key.</summary>
    private Dictionary<int, global::DBCD.DBCDRow> LoadIndexedById(string tableName, string build, global::DBCD.Locale locale = global::DBCD.Locale.None)
    {
        var key = $"{tableName}|{build}|{locale}";
        if (!idIndexCache.TryGetValue(key, out var index))
        {
            var storage = Load(tableName, build, locale);
            index = new Dictionary<int, global::DBCD.DBCDRow>(storage.Count);
            foreach (var row in storage.Values)
                index[row.Field<int>("ID")] = row;
            idIndexCache[key] = index;
        }
        return index;
    }

    public IReadOnlyList<TalentRecord> ReadTalents(string build)
    {
        var index = LoadIndexedById("Talent", build);
        var hasRequiredSpellId = Load("Talent", build).AvailableColumns.Contains("RequiredSpellID");

        var records = new List<TalentRecord>(index.Count);
        foreach (var row in index.Values)
        {
            var spellRanks = ReadIntArray(row, "SpellRank", 9);
            var prereqTalent = ReadIntArray(row, "PrereqTalent", 3);
            var prereqRank = ReadIntArray(row, "PrereqRank", 3);

            records.Add(new TalentRecord(
                Id: row.Field<int>("ID"),
                TabId: row.Field<int>("TabID"),
                Tier: row.Field<int>("TierID"),
                ColumnIndex: row.Field<int>("ColumnIndex"),
                SpellRanks: spellRanks,
                PrereqTalent: prereqTalent,
                PrereqRank: prereqRank,
                Flags: row.Field<int>("Flags"),
                RequiredSpellId: hasRequiredSpellId ? row.Field<int>("RequiredSpellID") : null));
        }

        return records;
    }

    public IReadOnlyList<TalentTabRecord> ReadTalentTabs(string build)
    {
        // Name_lang is a locstring — without an explicit locale DBCD exposes it as the raw
        // per-locale string[] instead of resolving one value, so Field<string> throws.
        var index = LoadIndexedById("TalentTab", build, global::DBCD.Locale.EnUS);
        var storage = Load("TalentTab", build, global::DBCD.Locale.EnUS);
        var hasOrderIndex = storage.AvailableColumns.Contains("OrderIndex");
        var hasBackgroundFile = storage.AvailableColumns.Contains("BackgroundFile");

        var records = new List<TalentTabRecord>(index.Count);
        foreach (var row in index.Values)
        {
            records.Add(new TalentTabRecord(
                Id: row.Field<int>("ID"),
                Name: row.Field<string>("Name_lang"),
                SpellIconId: row.Field<int>("SpellIconID"),
                RaceMask: row.Field<int>("RaceMask"),
                ClassMask: row.Field<int>("ClassMask"),
                OrderIndex: hasOrderIndex ? row.Field<int>("OrderIndex") : null,
                BackgroundFile: hasBackgroundFile ? row.Field<string>("BackgroundFile") : null));
        }

        return records;
    }

    public IReadOnlyList<ChrClassRecord> ReadChrClasses(string build)
    {
        var index = LoadIndexedById("ChrClasses", build, global::DBCD.Locale.EnUS);
        var storage = Load("ChrClasses", build, global::DBCD.Locale.EnUS);
        var hasFilename = storage.AvailableColumns.Contains("Filename");

        var records = new List<ChrClassRecord>(index.Count);
        foreach (var row in index.Values)
        {
            records.Add(new ChrClassRecord(
                Id: row.Field<int>("ID"),
                PlayerClass: row.Field<int>("PlayerClass"),
                Name: row.Field<string>("Name_lang"),
                PetNameToken: row.Field<string>("PetNameToken"),
                Filename: hasFilename ? row.Field<string>("Filename") : null));
        }

        return records;
    }

    /// <summary>Looks up a single spell by its real ID (e.g. one talent rank's SpellRank entry).
    /// Loads Spell.dbc once per build and caches it — safe to call per-rank in a loop.</summary>
    public SpellRecord? GetSpell(string build, int id)
    {
        var index = LoadIndexedById("Spell", build, global::DBCD.Locale.EnUS);
        if (!index.TryGetValue(id, out var row))
            return null;

        var storage = Load("Spell", build, global::DBCD.Locale.EnUS);
        var hasMaxTargetLevel = storage.AvailableColumns.Contains("MaxTargetLevel");
        var hasChainTargets = storage.AvailableColumns.Contains("EffectChainTargets");
        var hasShapeshiftExclude = storage.AvailableColumns.Contains("ShapeshiftExclude");
        var hasEquippedItemInvTypes = storage.AvailableColumns.Contains("EquippedItemInvTypes");

        return new SpellRecord(
            Id: id,
            Name: row.Field<string>("Name_lang"),
            NameSubtext: row.Field<string>("NameSubtext_lang"),
            Description: row.Field<string>("Description_lang"),
            SpellIconId: row.Field<int>("SpellIconID"),
            EffectBasePoints: ReadIntArray(row, "EffectBasePoints", 3),
            EffectAuraPeriod: ReadIntArray(row, "EffectAuraPeriod", 3),
            EffectAmplitude: ReadFloatArray(row, "EffectAmplitude", 3),
            EffectRadiusIndex: ReadIntArray(row, "EffectRadiusIndex", 3),
            EffectMiscValue: ReadIntArray(row, "EffectMiscValue", 3),
            EffectPointsPerCombo: ReadFloatArray(row, "EffectPointsPerCombo", 3),
            DurationIndex: row.Field<int>("DurationIndex"),
            ProcChance: row.Field<int>("ProcChance"),
            ProcCharges: row.Field<int>("ProcCharges"),
            CumulativeAura: row.Field<int>("CumulativeAura"),
            MaxTargetLevel: hasMaxTargetLevel ? row.Field<int>("MaxTargetLevel") : null,
            EffectChainTargets: hasChainTargets ? ReadIntArray(row, "EffectChainTargets", 3) : null,
            Attributes: row.Field<int>("Attributes"),
            PowerType: row.Field<int>("PowerType"),
            ManaCost: row.Field<int>("ManaCost"),
            RangeIndex: row.Field<int>("RangeIndex"),
            CastingTimeIndex: row.Field<int>("CastingTimeIndex"),
            RecoveryTime: row.Field<int>("RecoveryTime"),
            CategoryRecoveryTime: row.Field<int>("CategoryRecoveryTime"),
            ShapeshiftMask: row.Field<int>("ShapeshiftMask"),
            ShapeshiftExclude: hasShapeshiftExclude ? row.Field<int>("ShapeshiftExclude") : null,
            EquippedItemClass: row.Field<int>("EquippedItemClass"),
            EquippedItemSubclass: row.Field<int>("EquippedItemSubclass"),
            EquippedItemInvTypes: hasEquippedItemInvTypes ? row.Field<int>("EquippedItemInvTypes") : null);
    }

    /// <summary>Raw Spell.Attributes bitmask (the first of several Attributes/AttributesEx*
    /// flag fields - see dbd-definitions/Spell.dbd), e.g. for checking SPELL_ATTR_PASSIVE
    /// (0x40). Not part of SpellRecord - not needed for description formatting, only for
    /// one-off diagnostics. Returns null if the spell doesn't exist.</summary>
    public int? GetSpellAttributes(string build, int id)
    {
        var index = LoadIndexedById("Spell", build, global::DBCD.Locale.EnUS);
        return index.TryGetValue(id, out var row) ? row.Field<int>("Attributes") : null;
    }

    public SpellIconRecord? GetSpellIcon(string build, int id)
    {
        var index = LoadIndexedById("SpellIcon", build);
        if (!index.TryGetValue(id, out var row))
            return null;

        return new SpellIconRecord(id, row.Field<string>("TextureFilename"));
    }

    /// <summary>SpellDuration.dbc's base Duration in milliseconds, for a Spell's DurationIndex.
    /// One unchanged layout for all of vanilla.</summary>
    public int? GetSpellDurationMs(string build, int durationIndex)
    {
        var index = LoadIndexedById("SpellDuration", build);
        return index.TryGetValue(durationIndex, out var row) ? row.Field<int>("Duration") : null;
    }

    /// <summary>SpellRadius.dbc's Radius in yards, for a Spell's EffectRadiusIndex.
    /// One unchanged layout for all of vanilla.</summary>
    public float? GetSpellRadiusYards(string build, int radiusIndex)
    {
        var index = LoadIndexedById("SpellRadius", build);
        return index.TryGetValue(radiusIndex, out var row) ? row.Field<float>("Radius") : null;
    }

    /// <summary>SpellRange.dbc row for a Spell's RangeIndex. One unchanged layout for all of
    /// vanilla. Flags bit 0x1 = melee range (see SpellRangeRecord.IsMelee).</summary>
    public SpellRangeRecord? GetSpellRange(string build, int rangeIndex)
    {
        var index = LoadIndexedById("SpellRange", build);
        if (!index.TryGetValue(rangeIndex, out var row))
            return null;

        return new SpellRangeRecord(
            Id: rangeIndex,
            MinRange: row.Field<float>("RangeMin"),
            MaxRange: row.Field<float>("RangeMax"),
            Flags: row.Field<int>("Flags"));
    }

    /// <summary>SpellCastTimes.Base in milliseconds, for a Spell's CastingTimeIndex. 0 means
    /// instant cast. One unchanged layout for all of vanilla.</summary>
    public int? GetSpellCastTimeMs(string build, int castingTimeIndex)
    {
        var index = LoadIndexedById("SpellCastTimes", build);
        return index.TryGetValue(castingTimeIndex, out var row) ? row.Field<int>("Base") : null;
    }

    /// <summary>SpellShapeshiftForm.dbc's real localized name for a form ID (1-based, matching
    /// the bit convention in Spell.ShapeshiftMask - see SpellShapeshiftFormRecord).</summary>
    public SpellShapeshiftFormRecord? GetShapeshiftForm(string build, int id)
    {
        var index = LoadIndexedById("SpellShapeshiftForm", build, global::DBCD.Locale.EnUS);
        return index.TryGetValue(id, out var row) ? new SpellShapeshiftFormRecord(id, row.Field<string>("Name_lang")) : null;
    }

    /// <summary>Every SpellShapeshiftForm.dbc row for the build, for diagnostics/verification
    /// (see spike-shapeshift-forms in Extractor.Cli) - not used by the extraction pipeline
    /// itself, which resolves one form at a time via GetShapeshiftForm.</summary>
    public IReadOnlyList<SpellShapeshiftFormRecord> GetAllShapeshiftForms(string build)
    {
        var storage = Load("SpellShapeshiftForm", build, global::DBCD.Locale.EnUS);
        return storage.Values
            .Select(row => new SpellShapeshiftFormRecord(row.Field<int>("ID"), row.Field<string>("Name_lang")))
            .OrderBy(r => r.Id)
            .ToList();
    }

    private static int[] ReadIntArray(DBCD.DBCDRow row, string fieldName, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = (int)row[fieldName, i];
        return result;
    }

    private static float[] ReadFloatArray(DBCD.DBCDRow row, string fieldName, int count)
    {
        var result = new float[count];
        for (var i = 0; i < count; i++)
            result[i] = (float)row[fieldName, i];
        return result;
    }
}
