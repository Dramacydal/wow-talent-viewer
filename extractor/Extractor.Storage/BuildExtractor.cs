using Extractor.Blp;
using Extractor.Dbc;
using Extractor.Mpq;
using MySqlConnector;

namespace Extractor.Storage;

public sealed record ExtractionSummary(
    int ClientBuildId,
    int ClassesUpserted,
    int TalentTabsUpserted,
    int TalentTabsSkipped,
    int TalentsUpserted,
    int TalentsSkippedOrphanedTab,
    int RanksUpserted,
    int PrerequisitesUpserted,
    int PrerequisitesSkippedOrphaned);

/// <summary>
/// Writes one client build's full talent tree into MySQL, matching the schema in
/// web/src/Entity/*.php exactly. Idempotent — safe to re-run on the same build (upserts by
/// the unique keys the schema defines), see .claude-docs/architecture.md.
///
/// Enforces the CLAUDE.md MUST rule: DBC IDs (TabID, PrereqTalent, TalentTab.ClassMask) are
/// only meaningful within THIS build and are resolved to our own internal IDs during this
/// run; anything that doesn't resolve (orphaned TabID, a classMask that doesn't map to
/// exactly one real class, an unresolvable PrereqTalent) is skipped with a log line, never
/// written as a broken/guessed foreign key.
///
/// Performance: measured round-trip latency to the user's remote MariaDB is ~330ms EVEN for
/// a trivial "SELECT 1" — real network distance, not something fixable client-side. With
/// ~1500 rank rows alone, one round trip per row means ~8+ minutes on round trips no matter
/// how cheap each query is server-side. The only real lever is round-trip COUNT: everything
/// that doesn't need to see its own generated ID back is written as one multi-row
/// `INSERT ... VALUES (...), (...), ... ON DUPLICATE KEY UPDATE col = VALUES(col)` per phase
/// (or per progress batch, for ranks); talents/tabs (which the rest of the run needs real
/// IDs for) do one multi-row INSERT plus one follow-up SELECT to fetch the whole
/// id/source_id mapping back, instead of a round trip per row. See gotchas.md.
/// </summary>
public sealed class BuildExtractor(
    MySqlConnection connection,
    MpqArchive dbcArchive,
    MpqArchive interfaceArchive,
    string dbdDefinitionsDir,
    string build,
    string storageIconsDir,
    Action<string> log)
{
    // Only these class IDs actually exist in ChrClasses.dbc for all of vanilla (gaps at
    // 6 and 10) — see .claude-docs/gotchas.md. A classMask bit outside this set (or with
    // more than one bit set, e.g. the debug "Test"/"TomTest" tabs) means "not a real,
    // fully-wired class tab for this build" and gets skipped.
    private static readonly HashSet<int> KnownClassIds = [1, 2, 3, 4, 5, 7, 8, 9, 11];

    private readonly DbcClient dbcClient = new(dbcArchive, dbdDefinitionsDir);
    private IconPipeline iconPipeline = null!;
    private MySqlTransaction? transaction;

    public ExtractionSummary Run()
    {
        // Constructed here, not as a field initializer: the closure needs `transaction`,
        // which only exists once Run() starts (field initializers can't forward-reference
        // other instance fields in C#).
        iconPipeline = new IconPipeline(interfaceArchive, new MySqlIconSourceStore(connection, () => transaction), storageIconsDir);

        // Commit per phase (not one giant transaction for the whole run): a single
        // transaction makes every row invisible to any other connection — including the
        // user just watching the tables — until the very end. Upserts are keyed on stable
        // unique constraints, so partial progress from an interrupted run is always safe to
        // resume.
        var clientBuildId = InTransaction(UpsertClientBuild);
        var classesUpserted = InTransaction(UpsertClasses);

        var talents = dbcClient.ReadTalents(build); // read once, reused below

        var (tabIdMap, tabsUpserted, tabsSkipped) = InTransaction(() => UpsertTalentTabs(clientBuildId));
        var (talentIdMap, talentsUpserted, talentsSkipped) = InTransaction(() => UpsertTalents(clientBuildId, tabIdMap, talents));
        var ranksUpserted = UpsertTalentRanksWithProgress(talentIdMap, talents);
        var (prereqUpserted, prereqSkipped) = InTransaction(() => UpsertPrerequisites(talentIdMap, talents));

        return new ExtractionSummary(
            clientBuildId, classesUpserted,
            tabsUpserted, tabsSkipped,
            talentsUpserted, talentsSkipped,
            ranksUpserted,
            prereqUpserted, prereqSkipped);
    }

    private T InTransaction<T>(Func<T> work)
    {
        transaction = connection.BeginTransaction();
        try
        {
            var result = work();
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            transaction = null;
        }
    }

    /// <summary>Ranks are the bulk of the row count. Commits every ProgressBatchSize talents
    /// (one multi-row INSERT per batch — see class docblock) and logs progress, so both the
    /// console and a live look at the tables show forward motion instead of one big silent
    /// transaction.</summary>
    private const int ProgressBatchSize = 25;

    private int UpsertTalentRanksWithProgress(Dictionary<int, int> talentIdMap, IReadOnlyList<TalentRecord> talents)
    {
        var total = 0;
        var processed = 0;

        foreach (var batch in talents.Chunk(ProgressBatchSize))
        {
            total += InTransaction(() => UpsertTalentRanks(talentIdMap, batch));
            processed += batch.Length;
            log($"ranks: {processed}/{talents.Count} talents processed ({total} ranks written so far)");
        }

        return total;
    }

    private MySqlCommand Command(string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>Builds one multi-row "INSERT ... VALUES (...), (...) ON DUPLICATE KEY UPDATE
    /// col = VALUES(col)" command — the core round-trip-count fix, see class docblock.
    /// `updateColumns` defaults to all non-key columns if not given.</summary>
    private MySqlCommand BuildBatchInsert(string table, string[] columns, IReadOnlyList<object?[]> rows, string[]? updateColumns = null)
    {
        var cmd = Command("");
        var valueGroups = new List<string>(rows.Count);

        for (var r = 0; r < rows.Count; r++)
        {
            var placeholders = new string[columns.Length];
            for (var i = 0; i < columns.Length; i++)
            {
                var paramName = $"@p{r}_{i}";
                placeholders[i] = paramName;
                cmd.Parameters.AddWithValue(paramName, rows[r][i] ?? DBNull.Value);
            }
            valueGroups.Add($"({string.Join(",", placeholders)})");
        }

        var updateClause = string.Join(", ", (updateColumns ?? columns).Select(c => $"{c} = VALUES({c})"));
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", columns)}) VALUES {string.Join(",", valueGroups)} " +
                           $"ON DUPLICATE KEY UPDATE {updateClause}";
        return cmd;
    }

    private int UpsertClientBuild()
    {
        var buildNumber = int.Parse(build.Split('.').Last());

        using var cmd = Command("""
            INSERT INTO client_builds (label, build_number)
            VALUES (@label, @buildNumber)
            ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id), build_number = @buildNumber
            """);
        cmd.Parameters.AddWithValue("@label", build);
        cmd.Parameters.AddWithValue("@buildNumber", buildNumber);
        cmd.ExecuteNonQuery();
        return (int)cmd.LastInsertedId;
    }

    private int UpsertClasses()
    {
        var classes = dbcClient.ReadChrClasses(build);
        if (classes.Count == 0) return 0;

        var rows = classes
            .Select(c => new object?[] { c.Id, (c.Filename ?? c.Name).ToLowerInvariant(), c.Name })
            .ToList();

        using var cmd = BuildBatchInsert("classes", ["id", "slug", "name"], rows, updateColumns: ["slug", "name"]);
        cmd.ExecuteNonQuery();
        return classes.Count;
    }

    private (Dictionary<int, int> tabIdMap, int upserted, int skipped) UpsertTalentTabs(int clientBuildId)
    {
        var map = new Dictionary<int, int>();
        var rows = new List<object?[]>();
        var skipped = 0;

        foreach (var tab in dbcClient.ReadTalentTabs(build))
        {
            var classId = TryResolveClassId(tab.ClassMask);
            if (classId is null)
            {
                log($"skip TalentTab #{tab.Id} \"{tab.Name}\": classMask={tab.ClassMask} doesn't map to exactly one known class");
                skipped++;
                continue;
            }

            var iconPath = ResolveIcon(tab.SpellIconId);
            rows.Add([clientBuildId, classId.Value, tab.Id, tab.Name, iconPath, tab.OrderIndex, tab.BackgroundFile]);
        }

        if (rows.Count > 0)
        {
            using var cmd = BuildBatchInsert(
                "talent_tabs",
                ["client_build_id", "character_class_id", "source_tab_id", "name", "icon_path", "order_index", "background_file"],
                rows,
                updateColumns: ["character_class_id", "name", "icon_path", "order_index", "background_file"]);
            cmd.ExecuteNonQuery();
        }

        // One follow-up SELECT instead of a LAST_INSERT_ID round trip per row — a multi-row
        // INSERT only reports the first row's generated id, so we need this regardless.
        using (var select = Command("SELECT id, source_tab_id FROM talent_tabs WHERE client_build_id = @buildId"))
        {
            select.Parameters.AddWithValue("@buildId", clientBuildId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                map[reader.GetInt32(1)] = reader.GetInt32(0);
        }

        return (map, rows.Count, skipped);
    }

    private (Dictionary<int, int> talentIdMap, int upserted, int skipped) UpsertTalents(
        int clientBuildId, Dictionary<int, int> tabIdMap, IReadOnlyList<TalentRecord> talents)
    {
        var map = new Dictionary<int, int>();
        var rows = new List<object?[]>();
        var skipped = 0;

        foreach (var talent in talents)
        {
            if (!tabIdMap.TryGetValue(talent.TabId, out var talentTabId))
            {
                log($"skip Talent #{talent.Id} (tier={talent.Tier}, col={talent.ColumnIndex}): TabID={talent.TabId} doesn't resolve to a real TalentTab in this build");
                skipped++;
                continue;
            }

            var maxRank = talent.SpellRanks.Count(id => id != 0);
            rows.Add([clientBuildId, talentTabId, talent.Id, talent.Tier, talent.ColumnIndex, maxRank]);
        }

        if (rows.Count > 0)
        {
            using var cmd = BuildBatchInsert(
                "talents",
                ["client_build_id", "talent_tab_id", "source_talent_id", "tier", "column_index", "max_rank"],
                rows,
                updateColumns: ["talent_tab_id", "tier", "column_index", "max_rank"]);
            cmd.ExecuteNonQuery();
        }

        using (var select = Command("SELECT id, source_talent_id FROM talents WHERE client_build_id = @buildId"))
        {
            select.Parameters.AddWithValue("@buildId", clientBuildId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                map[reader.GetInt32(1)] = reader.GetInt32(0);
        }

        return (map, rows.Count, skipped);
    }

    private int UpsertTalentRanks(Dictionary<int, int> talentIdMap, IReadOnlyList<TalentRecord> talents)
    {
        var rows = new List<object?[]>();

        foreach (var talent in talents)
        {
            if (!talentIdMap.TryGetValue(talent.Id, out var ourTalentId))
                continue; // already logged as skipped in UpsertTalents

            for (var i = 0; i < talent.SpellRanks.Length; i++)
            {
                var spellId = talent.SpellRanks[i];
                if (spellId == 0) continue;

                var spell = dbcClient.GetSpell(build, spellId);
                if (spell is null)
                {
                    log($"skip rank {i + 1} of Talent #{talent.Id}: spell {spellId} not found in Spell.dbc");
                    continue;
                }

                var iconPath = ResolveIcon(spell.SpellIconId);
                rows.Add([ourTalentId, i + 1, spellId, spell.Name, spell.Description, iconPath]);
            }
        }

        if (rows.Count == 0) return 0;

        using var cmd = BuildBatchInsert(
            "talent_ranks",
            ["talent_id", "rank_index", "spell_id", "name", "description", "icon_path"],
            rows,
            updateColumns: ["spell_id", "name", "description", "icon_path"]);
        cmd.ExecuteNonQuery();
        return rows.Count;
    }

    private (int upserted, int skipped) UpsertPrerequisites(Dictionary<int, int> talentIdMap, IReadOnlyList<TalentRecord> talents)
    {
        var rows = new List<object?[]>();
        var skipped = 0;

        foreach (var talent in talents)
        {
            if (!talentIdMap.TryGetValue(talent.Id, out var ourTalentId))
                continue;

            for (var i = 0; i < talent.PrereqTalent.Length; i++)
            {
                var requiresSourceId = talent.PrereqTalent[i];
                if (requiresSourceId == 0) continue;

                if (!talentIdMap.TryGetValue(requiresSourceId, out var requiresOurId))
                {
                    log($"skip prerequisite of Talent #{talent.Id}: requires Talent #{requiresSourceId}, which doesn't exist/resolve in this build");
                    skipped++;
                    continue;
                }

                rows.Add([ourTalentId, requiresOurId, talent.PrereqRank[i]]);
            }
        }

        if (rows.Count == 0) return (0, skipped);

        using var cmd = BuildBatchInsert(
            "talent_prerequisites",
            ["talent_id", "requires_talent_id", "requires_rank"],
            rows,
            updateColumns: ["requires_rank"]);
        cmd.ExecuteNonQuery();

        return (rows.Count, skipped);
    }

    private string? ResolveIcon(int spellIconId)
    {
        var icon = dbcClient.GetSpellIcon(build, spellIconId);
        if (icon is null) return null;

        var result = iconPipeline.Extract(icon.TextureFilename);
        return result.IconPath;
    }

    /// <summary>classMask must have exactly one bit set, and that bit's class must actually
    /// exist — otherwise this isn't a real, fully-wired class tab for this build (debug tabs,
    /// or a class mid-rollout with a temporary/wrong classMask — see gotchas.md).</summary>
    private static int? TryResolveClassId(int classMask)
    {
        if (classMask <= 0 || (classMask & (classMask - 1)) != 0)
            return null; // zero, negative, or more than one bit set

        var classId = System.Numerics.BitOperations.TrailingZeroCount(classMask) + 1;
        return KnownClassIds.Contains(classId) ? classId : null;
    }
}
