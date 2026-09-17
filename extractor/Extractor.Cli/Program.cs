using System.Security.Cryptography;
using Extractor.Dbc;
using Extractor.Mpq;

if (args.Length < 1)
{
    Console.WriteLine("Usage: extractor spike-dbc <client-dir>");
    Console.WriteLine("       extractor spike-patch-verify <client-dir> <internal-path>");
    return 1;
}

return args[0] switch
{
    "spike-dbc" => SpikeDbc(args[1]),
    "spike-patch-verify" => SpikePatchVerify(args[1], args[2]),
    "grep-listfile" => GrepListfile(args[1], args[2]),
    "dump-file" => DumpFile(args[1], args[2], args[3]),
    "spike-dbcd" => SpikeDbcd(args[1], args[2]),
    "spike-talenttab" => SpikeTalentTab(args[1], args[2]),
    "classes-with-talents" => ClassesWithTalents(args[1], args[2]),
    "list-talenttabs" => ListTalentTabs(args[1], args[2]),
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
