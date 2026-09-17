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

    public DbcClient(MpqArchive archive, string definitionsDirectory)
    {
        var dbcProvider = new MpqDbcProvider(archive);
        var dbdProvider = new FilesystemDBDProvider(definitionsDirectory);
        dbcd = new global::DBCD.DBCD(dbcProvider, dbdProvider);
    }

    public IReadOnlyList<TalentRecord> ReadTalents(string build)
    {
        var storage = dbcd.Load("Talent", build);
        var hasRequiredSpellId = storage.AvailableColumns.Contains("RequiredSpellID");

        var records = new List<TalentRecord>(storage.Count);
        foreach (var row in storage.Values)
        {
            var spellRanks = ReadIntArray(row, "SpellRank", 9);
            var prereqTalent = ReadIntArray(row, "PrereqTalent", 3);
            var prereqRank = ReadIntArray(row, "PrereqRank", 3);

            records.Add(new TalentRecord(
                Id: row.ID,
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
        var storage = dbcd.Load("TalentTab", build, global::DBCD.Locale.EnUS);
        var hasOrderIndex = storage.AvailableColumns.Contains("OrderIndex");
        var hasBackgroundFile = storage.AvailableColumns.Contains("BackgroundFile");

        var records = new List<TalentTabRecord>(storage.Count);
        foreach (var row in storage.Values)
        {
            records.Add(new TalentTabRecord(
                Id: row.ID,
                Name: row.Field<string>("Name_lang"),
                SpellIconId: row.Field<int>("SpellIconID"),
                RaceMask: row.Field<int>("RaceMask"),
                ClassMask: row.Field<int>("ClassMask"),
                OrderIndex: hasOrderIndex ? row.Field<int>("OrderIndex") : null,
                BackgroundFile: hasBackgroundFile ? row.Field<string>("BackgroundFile") : null));
        }

        return records;
    }

    private static int[] ReadIntArray(DBCD.DBCDRow row, string fieldName, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = (int)row[fieldName, i];
        return result;
    }
}
