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

    private static int[] ReadIntArray(DBCD.DBCDRow row, string fieldName, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = (int)row[fieldName, i];
        return result;
    }
}
