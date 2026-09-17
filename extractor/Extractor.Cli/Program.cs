using Extractor.Mpq;

if (args.Length < 1 || args[0] != "spike-dbc")
{
    Console.WriteLine("Usage: extractor spike-dbc <client-dir>");
    return 1;
}

var clientDir = args[1];
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
{
    Console.WriteLine($"File not found in archive chain: {internalPath}");
    return 1;
}

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
