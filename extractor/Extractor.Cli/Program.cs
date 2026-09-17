using System.Security.Cryptography;
using Extractor.Blp;
using Extractor.Dbc;
using Extractor.Mpq;
using Extractor.Storage;
using MySqlConnector;

if (args.Length < 1)
{
    Console.WriteLine("Usage: extractor spike-dbc <client-dir>");
    Console.WriteLine("       extractor spike-patch-verify <client-dir> <internal-path>");
    return 1;
}

return args[0] switch
{
    "spike-dbc" => SpikeDbc(args[1]),
    "raw-dbc-header" => RawDbcHeader(args[1], args[2]),
    "spike-patch-verify" => SpikePatchVerify(args[1], args[2]),
    "grep-listfile" => GrepListfile(args[1], args[2]),
    "dump-file" => DumpFile(args[1], args[2], args[3]),
    "spike-dbcd" => SpikeDbcd(args[1], args[2]),
    "spike-talenttab" => SpikeTalentTab(args[1], args[2]),
    "classes-with-talents" => ClassesWithTalents(args[1], args[2]),
    "list-talenttabs" => ListTalentTabs(args[1], args[2]),
    "spike-spell" => SpikeSpell(args[1], args[2]),
    "raw-talent-record" => RawTalentRecord(args[1], int.Parse(args[2])),
    "raw-talent-by-id" => RawTalentById(args[1], int.Parse(args[2])),
    "talents-in-tab" => TalentsInTab(args[1], args[2], int.Parse(args[3])),
    "spike-chrclasses" => SpikeChrClasses(args[1], args[2]),
    "spike-format-description" => SpikeFormatDescription(args[1], args[2], int.Parse(args[3])),
    "resolve-icon" => ResolveIcon(args[1], args[2], int.Parse(args[3])),
    "extract-icon" => ExtractIcon(args[1], args[2], int.Parse(args[3]), args[4], args[5]),
    "extract-build" => ExtractBuild(args[1], args[2], args[3], args[4]),
    _ => Fail($"Unknown command: {args[0]}")
};

static int Fail(string message)
{
    Console.WriteLine(message);
    return 1;
}

static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

/// <summary>Opens dbc.MPQ and applies the client's full patch chain (however many patch-N.MPQ
/// files actually exist — see PatchChain, this is not hardcoded to 2).</summary>
static MpqArchive OpenPatchedDbc(string clientDir)
{
    var dataDir = Path.Combine(clientDir, "Data");
    var archive = MpqArchive.Open(Path.Combine(dataDir, "dbc.MPQ"));
    PatchChain.ApplyAll(archive, dataDir);
    return archive;
}

static DbcClient MakeDbcClient(MpqArchive archive) =>
    new(archive, Path.Combine(AppContext.BaseDirectory, "dbd-definitions"));

// Diagnostic: dump the MPQ's internal "(listfile)" pseudo-file (if present) and grep it,
// so we can find out what a DBC file was actually called before assuming it doesn't exist.
static int GrepListfile(string clientDir, string pattern)
{
    var dataDir = Path.Combine(clientDir, "Data");
    var archiveNames = new List<string> { "base.MPQ", "dbc.MPQ", "interface.MPQ", "misc.MPQ" };
    archiveNames.AddRange(PatchChain.DiscoverInOrder(dataDir).Select(Path.GetFileName)!);

    foreach (var name in archiveNames)
    {
        var path = Path.Combine(dataDir, name!);
        if (!File.Exists(path)) continue;

        using var archive = MpqArchive.Open(path);
        if (!archive.HasFile("(listfile)"))
        {
            Console.WriteLine($"[{name}] no internal (listfile)");
            continue;
        }

        var bytes = archive.ReadFile("(listfile)");
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        var matches = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"[{name}] (listfile) has {text.Split('\n').Length} entries, {matches.Count} match '{pattern}':");
        foreach (var m in matches)
            Console.WriteLine($"  {m.Trim()}");
    }

    return 0;
}

static int SpikeDbc(string clientDir)
{
    using var archive = OpenPatchedDbc(clientDir);

    const string internalPath = @"DBFilesClient\Talent.dbc";
    if (!archive.HasFile(internalPath))
        return Fail($"File not found in archive chain: {internalPath}");

    var bytes = archive.ReadFile(internalPath);
    Console.WriteLine($"Read {internalPath}: {bytes.Length} bytes");

    var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
    var recordCount = BitConverter.ToUInt32(bytes, 4);
    var fieldCount = BitConverter.ToUInt32(bytes, 8);
    var recordSize = BitConverter.ToUInt32(bytes, 12);
    var stringBlockSize = BitConverter.ToUInt32(bytes, 16);

    Console.WriteLine($"magic={magic} recordCount={recordCount} fieldCount={fieldCount} recordSize={recordSize} stringBlockSize={stringBlockSize}");
    Console.WriteLine($"expected total size = 20 (header) + {recordCount * recordSize} (records) + {stringBlockSize} (strings) = {20 + recordCount * recordSize + stringBlockSize}, actual = {bytes.Length}");

    return 0;
}

/// <summary>Prints a raw WDBC header for any table by name (e.g. "ChrClasses") - lets a
/// build's actual recordSize settle which .dbd layout applies when a build falls in a gap
/// between two BUILD ranges (recordSize = 4 bytes per fixed-width field, tells field count
/// without needing DBCD/a schema at all).</summary>
static int RawDbcHeader(string clientDir, string tableName)
{
    using var archive = OpenPatchedDbc(clientDir);

    var internalPath = $@"DBFilesClient\{tableName}.dbc";
    if (!archive.HasFile(internalPath))
        return Fail($"File not found in archive chain: {internalPath}");

    var bytes = archive.ReadFile(internalPath);
    var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
    var recordCount = BitConverter.ToUInt32(bytes, 4);
    var fieldCount = BitConverter.ToUInt32(bytes, 8);
    var recordSize = BitConverter.ToUInt32(bytes, 12);
    var stringBlockSize = BitConverter.ToUInt32(bytes, 16);

    Console.WriteLine($"{tableName}.dbc: {bytes.Length} bytes, magic={magic} recordCount={recordCount} fieldCount={fieldCount} recordSize={recordSize} stringBlockSize={stringBlockSize}");
    return 0;
}

// Diagnostic: dump a text file straight from a named MPQ archive to stdout (no patch chain).
static int DumpFile(string clientDir, string archiveName, string internalPath)
{
    using var archive = MpqArchive.Open(Path.Combine(clientDir, "Data", archiveName));
    if (!archive.HasFile(internalPath))
        return Fail($"'{internalPath}' not found in {archiveName}");

    Console.WriteLine(System.Text.Encoding.ASCII.GetString(archive.ReadFile(internalPath)));
    return 0;
}

static int SpikeDbcd(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var talents = client.ReadTalents(build);
    Console.WriteLine($"DBCD loaded {talents.Count} Talent rows for build {build}");

    foreach (var t in talents.Take(5))
    {
        Console.WriteLine($"  #{t.Id} tab={t.TabId} tier={t.Tier} col={t.ColumnIndex} " +
                           $"ranks=[{string.Join(",", t.SpellRanks)}] " +
                           $"prereqTalent=[{string.Join(",", t.PrereqTalent)}] " +
                           $"prereqRank=[{string.Join(",", t.PrereqRank)}] " +
                           $"flags={t.Flags} requiredSpell={t.RequiredSpellId?.ToString() ?? "n/a"}");
    }

    return 0;
}

static int SpikeTalentTab(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var tabs = client.ReadTalentTabs(build);
    Console.WriteLine($"DBCD loaded {tabs.Count} TalentTab rows for build {build}");

    foreach (var t in tabs.Take(5))
    {
        Console.WriteLine($"  #{t.Id} name=\"{t.Name}\" icon={t.SpellIconId} raceMask={t.RaceMask} classMask={t.ClassMask} " +
                           $"order={t.OrderIndex?.ToString() ?? "n/a"} bg={t.BackgroundFile ?? "n/a"}");
    }

    return 0;
}

// Bypasses DBCD entirely: parses one Talent.dbc record by hand from raw bytes, at the known
// byte offsets for layout B (ID,TabID,TierID,ColumnIndex,SpellRank[9],PrereqTalent[3],
// PrereqRank[3],Flags,RequiredSpellID, all int32, matching WoWDBDefs' declared field order).
// Used to check whether DBCD's field mapping is trustworthy for this table.
static int RawTalentRecord(string clientDir, int recordIndex)
{
    using var archive = OpenPatchedDbc(clientDir);
    var bytes = archive.ReadFile(@"DBFilesClient\Talent.dbc");

    var recordCount = BitConverter.ToUInt32(bytes, 4);
    var recordSize = BitConverter.ToUInt32(bytes, 12);
    if (recordIndex < 0 || recordIndex >= recordCount)
        return Fail($"recordIndex out of range (0..{recordCount - 1})");

    var offset = 20 + recordIndex * (int)recordSize;
    int I(int fieldOffset) => BitConverter.ToInt32(bytes, offset + fieldOffset);

    var id = I(0);
    var tabId = I(4);
    var tier = I(8);
    var col = I(12);
    var ranks = Enumerable.Range(0, 9).Select(i => I(16 + i * 4)).ToArray();
    var prereqTalent = Enumerable.Range(0, 3).Select(i => I(52 + i * 4)).ToArray();
    var prereqRank = Enumerable.Range(0, 3).Select(i => I(64 + i * 4)).ToArray();
    var flags = I(76);
    var requiredSpell = recordSize >= 84 ? I(80) : (int?)null;

    Console.WriteLine($"raw record[{recordIndex}] @byte {offset}: id={id} tabId={tabId} tier={tier} col={col} " +
                       $"ranks=[{string.Join(",", ranks)}] prereqTalent=[{string.Join(",", prereqTalent)}] " +
                       $"prereqRank=[{string.Join(",", prereqRank)}] flags={flags} requiredSpell={requiredSpell?.ToString() ?? "n/a"}");

    return 0;
}

// Scans every raw record (not just index 0) for the one whose real ID field equals
// targetId — WDBC records are NOT necessarily physically sorted by ID, so "record at
// index 0" and "record with ID==1" can be two entirely different rows.
static int RawTalentById(string clientDir, int targetId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var bytes = archive.ReadFile(@"DBFilesClient\Talent.dbc");

    var recordCount = (int)BitConverter.ToUInt32(bytes, 4);
    var recordSize = (int)BitConverter.ToUInt32(bytes, 12);

    for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
    {
        var offset = 20 + recordIndex * recordSize;
        var id = BitConverter.ToInt32(bytes, offset);
        if (id != targetId) continue;

        int I(int fieldOffset) => BitConverter.ToInt32(bytes, offset + fieldOffset);
        var tabId = I(4);
        var tier = I(8);
        var col = I(12);
        var ranks = Enumerable.Range(0, 9).Select(i => I(16 + i * 4)).ToArray();

        Console.WriteLine($"found id={id} at physical record index {recordIndex} (byte {offset}): " +
                           $"tabId={tabId} tier={tier} col={col} ranks=[{string.Join(",", ranks)}]");
        return 0;
    }

    return Fail($"No record with ID={targetId} found among {recordCount} records.");
}

// Full pipeline for one icon, through IconPipeline (resolve -> hash -> cache check -> decode
// only on miss). storeFile is the JSON stand-in for the real MySQL icon_sources table.
static int ExtractIcon(string clientDir, string build, int spellIconId, string storagePath, string storeFile)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var icon = client.GetSpellIcon(build, spellIconId);
    if (icon is null)
        return Fail($"SpellIcon {spellIconId} not found");

    var dataDir = Path.Combine(clientDir, "Data");
    using var iface = MpqArchive.Open(Path.Combine(dataDir, "interface.MPQ"));
    PatchChain.ApplyAll(iface, dataDir);

    var store = new JsonFileIconSourceStore(storeFile);
    var pipeline = new IconPipeline(iface, store, storagePath);
    var result = pipeline.Extract(icon.TextureFilename);

    Console.WriteLine($"icon '{icon.TextureFilename}': outcome={result.Outcome} iconPath={result.IconPath ?? "n/a"} (store now has {store.Count} entries)");
    return 0;
}

static int ResolveIcon(string clientDir, string build, int spellIconId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var icon = client.GetSpellIcon(build, spellIconId);
    if (icon is null)
        return Fail($"SpellIcon {spellIconId} not found");

    // The icon lives in interface.MPQ (+ patches), not dbc.MPQ — open that chain too.
    var dataDir = Path.Combine(clientDir, "Data");
    using var iface = MpqArchive.Open(Path.Combine(dataDir, "interface.MPQ"));
    PatchChain.ApplyAll(iface, dataDir);

    var resolved = IconFileResolver.Resolve(iface, icon.TextureFilename);
    Console.WriteLine($"SpellIcon {spellIconId}: TextureFilename=\"{icon.TextureFilename}\" resolved={resolved ?? "NOT FOUND (neither .blp nor .tga)"}");

    return 0;
}

// Full pipeline for one build: DBC -> MySQL, idempotent (safe to re-run on the same build).
// envFilePath points at a Symfony .env-style file (e.g. web/.env.local) containing
// DATABASE_URL — never pass a raw connection string/password on the command line.
static int ExtractBuild(string clientDir, string build, string envFilePath, string storageIconsDir)
{
    var connectionString = EnvFileConnectionString.ReadMySqlConnectionString(envFilePath);
    var dataDir = Path.Combine(clientDir, "Data");
    var dbdDefinitionsDir = Path.Combine(AppContext.BaseDirectory, "dbd-definitions");

    using var dbcArchive = MpqArchive.Open(Path.Combine(dataDir, "dbc.MPQ"));
    PatchChain.ApplyAll(dbcArchive, dataDir);

    using var interfaceArchive = MpqArchive.Open(Path.Combine(dataDir, "interface.MPQ"));
    PatchChain.ApplyAll(interfaceArchive, dataDir);

    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    var extractor = new BuildExtractor(connection, dbcArchive, interfaceArchive, dbdDefinitionsDir, build, storageIconsDir, msg => Console.WriteLine($"  {msg}"));
    var summary = extractor.Run();

    Console.WriteLine($"Build {build} (client_build_id={summary.ClientBuildId}): " +
                       $"classes={summary.ClassesUpserted}, " +
                       $"tabs={summary.TalentTabsUpserted} ({summary.TalentTabsSkipped} skipped), " +
                       $"talents={summary.TalentsUpserted} ({summary.TalentsSkippedOrphanedTab} skipped), " +
                       $"ranks={summary.RanksUpserted}, " +
                       $"prereqs={summary.PrerequisitesUpserted} ({summary.PrerequisitesSkippedOrphaned} skipped)");

    return 0;
}

static int SpikeFormatDescription(string clientDir, string build, int spellId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);
    var formatter = new SpellDescriptionFormatter(client, build);

    var spell = client.GetSpell(build, spellId);
    if (spell is null)
        return Fail($"Spell {spellId} not found");

    Console.WriteLine($"raw:       {spell.Description}");
    Console.WriteLine($"formatted: {formatter.Format(spell)}");
    return 0;
}

static int SpikeChrClasses(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var classes = client.ReadChrClasses(build);
    Console.WriteLine($"DBCD loaded {classes.Count} ChrClasses rows for build {build}");
    foreach (var c in classes.OrderBy(c => c.PlayerClass))
    {
        Console.WriteLine($"  #{c.Id} playerClass={c.PlayerClass} classMaskFromId={1 << (c.Id - 1)} " +
                           $"name=\"{c.Name}\" petToken=\"{c.PetNameToken}\" filename={c.Filename ?? "n/a"}");
    }

    return 0;
}

static int TalentsInTab(string clientDir, string build, int tabId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var talents = client.ReadTalents(build).Where(t => t.TabId == tabId).OrderBy(t => t.Tier).ThenBy(t => t.ColumnIndex);
    foreach (var t in talents)
    {
        var firstRankId = t.SpellRanks.First(id => id != 0);
        var spell = client.GetSpell(build, firstRankId);
        Console.WriteLine($"  talent #{t.Id} tier={t.Tier} col={t.ColumnIndex}: {spell?.Name ?? "?"} (spell {firstRankId})");
    }

    return 0;
}

static int SpikeSpell(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var talents = client.ReadTalents(build);
    var sample = talents.Take(3);

    foreach (var t in sample)
    {
        Console.WriteLine($"Talent #{t.Id} (tab={t.TabId}, tier={t.Tier}, col={t.ColumnIndex}):");
        foreach (var spellId in t.SpellRanks.Where(id => id != 0))
        {
            var spell = client.GetSpell(build, spellId);
            if (spell is null)
            {
                Console.WriteLine($"  spell {spellId}: NOT FOUND");
                continue;
            }

            var icon = client.GetSpellIcon(build, spell.SpellIconId);
            Console.WriteLine($"  spell {spellId}: name=\"{spell.Name}\" subtext=\"{spell.NameSubtext}\" " +
                               $"spellIconId={spell.SpellIconId} icon=\"{icon?.TextureFilename ?? "n/a"}\"");
            Console.WriteLine($"    desc: {spell.Description}");
        }
    }

    return 0;
}

static int ListTalentTabs(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);
    var tabs = client.ReadTalentTabs(build);

    Console.WriteLine($"{build}: {tabs.Count} TalentTab rows total");
    foreach (var t in tabs.OrderBy(t => t.Id))
    {
        Console.WriteLine($"  #{t.Id} name=\"{t.Name}\" icon={t.SpellIconId} raceMask={t.RaceMask} classMask={t.ClassMask} " +
                           $"order={t.OrderIndex?.ToString() ?? "n/a"} bg={t.BackgroundFile ?? "n/a"}");
    }

    return 0;
}

// Which classes actually have a TalentTab row yet, for one build — used to date when each
// class's talent tree was introduced (they didn't all show up at once).
static int ClassesWithTalents(string clientDir, string build)
{
    (int Mask, string Name)[] classMasks =
    [
        (1, "Warrior"), (2, "Paladin"), (4, "Hunter"), (8, "Rogue"), (16, "Priest"),
        (64, "Shaman"), (128, "Mage"), (256, "Warlock"), (1024, "Druid"),
    ];

    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);
    var tabs = client.ReadTalentTabs(build);

    var seenMasks = tabs.Select(t => t.ClassMask).Distinct().ToHashSet();
    var present = classMasks.Where(c => seenMasks.Contains(c.Mask)).Select(c => c.Name);
    var unknownMasks = seenMasks.Except(classMasks.Select(c => c.Mask));

    Console.WriteLine($"{build}: {tabs.Count} tabs, classes=[{string.Join(",", present)}]" +
                       (unknownMasks.Any() ? $" unknownMasks=[{string.Join(",", unknownMasks)}]" : ""));

    return 0;
}

// Proves the patch chain is actually honored, not just "applied without error": reads the
// same internal file from each MPQ layer standalone (dbc.MPQ + however many patch-N.MPQ
// actually exist) and compares hashes against a read through the real chained archive. The
// chained read must match whichever standalone layer last touched the file, and differ from
// earlier layers that had different content for it.
static int SpikePatchVerify(string clientDir, string internalPath)
{
    var dataDir = Path.Combine(clientDir, "Data");
    var layers = new List<string> { "dbc.MPQ" };
    layers.AddRange(PatchChain.DiscoverInOrder(dataDir).Select(Path.GetFileName)!);

    Console.WriteLine($"Checking '{internalPath}' across layers: {string.Join(", ", layers)}");
    Console.WriteLine();

    var perLayer = new Dictionary<string, (int size, string hash)>();
    foreach (var layer in layers)
    {
        var path = Path.Combine(dataDir, layer);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[{layer}] archive not present, skipping");
            continue;
        }

        using var standalone = MpqArchive.Open(path);
        if (!standalone.HasFile(internalPath))
        {
            Console.WriteLine($"[{layer}] does not contain the file (expected for patches that don't touch it)");
            continue;
        }

        var bytes = standalone.ReadFile(internalPath);
        var hash = Sha256Hex(bytes);
        perLayer[layer] = (bytes.Length, hash);
        Console.WriteLine($"[{layer}] standalone: {bytes.Length} bytes, sha256={hash}");
    }

    Console.WriteLine();

    using var chained = OpenPatchedDbc(clientDir);
    var chainedBytes = chained.ReadFile(internalPath);
    var chainedHash = Sha256Hex(chainedBytes);
    Console.WriteLine($"[chained dbc.MPQ+patches] {chainedBytes.Length} bytes, sha256={chainedHash}");
    Console.WriteLine();

    var expectedLayer = Enumerable.Reverse(layers).FirstOrDefault(l => perLayer.ContainsKey(l));
    if (expectedLayer is null)
        return Fail("File was found in no layer at all — nothing to verify.");

    var (expectedSize, expectedHash) = perLayer[expectedLayer];
    if (chainedHash == expectedHash)
    {
        Console.WriteLine($"OK: chained read matches the last layer that has this file ('{expectedLayer}') exactly.");
    }
    else
    {
        Console.WriteLine($"MISMATCH: chained read ({chainedBytes.Length}b, {chainedHash}) does NOT match '{expectedLayer}' ({expectedSize}b, {expectedHash}).");
        return 1;
    }

    var differsFromEarlier = perLayer
        .Where(kv => kv.Key != expectedLayer)
        .Where(kv => kv.Value.hash == chainedHash)
        .ToList();

    if (differsFromEarlier.Count > 0 && perLayer.Count > 1)
    {
        Console.WriteLine($"Note: content is identical across layers {string.Join(", ", differsFromEarlier.Select(kv => kv.Key))} and the chained result — this file happens not to have changed between those patch levels, so this run alone doesn't prove patching took effect. Try a file/build where the content actually differs between layers.");
    }
    else if (perLayer.Count > 1)
    {
        Console.WriteLine("Content genuinely differs between at least two layers, and the chained read picked the newest one — patch chain confirmed working.");
    }

    return 0;
}
