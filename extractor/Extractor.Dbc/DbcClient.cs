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

        return new SpellRecord(
            Id: id,
            Name: row.Field<string>("Name_lang"),
            NameSubtext: row.Field<string>("NameSubtext_lang"),
            Description: row.Field<string>("Description_lang"),
            SpellIconId: row.Field<int>("SpellIconID"));
    }

    public SpellIconRecord? GetSpellIcon(string build, int id)
    {
        var index = LoadIndexedById("SpellIcon", build);
        if (!index.TryGetValue(id, out var row))
            return null;

        return new SpellIconRecord(id, row.Field<string>("TextureFilename"));
    }

    private static int[] ReadIntArray(DBCD.DBCDRow row, string fieldName, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = (int)row[fieldName, i];
        return result;
    }
}
