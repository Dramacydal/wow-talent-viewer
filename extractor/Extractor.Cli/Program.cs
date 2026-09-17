using System.Security.Cryptography;
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
    _ => Fail($"Unknown command: {args[0]}")
};

static int Fail(string message)
{
    Console.WriteLine(message);
    return 1;
}

static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

static int SpikeDbc(string clientDir)
{
    var dataDir = Path.Combine(clientDir, "Data");

    using var archive = MpqArchive.Open(Path.Combine(dataDir, "dbc.MPQ"));
    Console.WriteLine("Opened dbc.MPQ");

    foreach (var patch in new[] { "patch.MPQ", "patch-2.MPQ" })
    {
        var patchPath = Path.Combine(dataDir, patch);
        if (File.Exists(patchPath))
        {
            archive.ApplyPatch(patchPath);
            Console.WriteLine($"Applied patch: {patch}");
        }
    }

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

// Proves the patch chain is actually honored, not just "applied without error": reads the
// same internal file from each MPQ layer standalone (dbc.MPQ, patch.MPQ, patch-2.MPQ on
// their own) and compares hashes against a read through the real chained archive. The
// chained read must match whichever standalone layer last touched the file, and differ from
// earlier layers that had different content for it.
static int SpikePatchVerify(string clientDir, string internalPath)
{
    var dataDir = Path.Combine(clientDir, "Data");
    var layers = new[] { "dbc.MPQ", "patch.MPQ", "patch-2.MPQ" };

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

    using var chained = MpqArchive.Open(Path.Combine(dataDir, "dbc.MPQ"));
    foreach (var layer in layers.Skip(1))
    {
        var path = Path.Combine(dataDir, layer);
        if (File.Exists(path))
            chained.ApplyPatch(path);
    }

    var chainedBytes = chained.ReadFile(internalPath);
    var chainedHash = Sha256Hex(chainedBytes);
    Console.WriteLine($"[chained dbc.MPQ+patches] {chainedBytes.Length} bytes, sha256={chainedHash}");
    Console.WriteLine();

    var expectedLayer = layers.Reverse().FirstOrDefault(l => perLayer.ContainsKey(l));
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
