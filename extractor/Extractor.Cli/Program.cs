using System.Security.Cryptography;
using Extractor.Blp;
using Extractor.Dbc;
using Extractor.Mpq;
using Extractor.Storage;
using MySqlConnector;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
    "grep-raw-desc" => GrepRawDescriptions(args[1], args[2], args[3]),
    "spike-patch-verify" => SpikePatchVerify(args[1], args[2]),
    "grep-listfile" => GrepListfile(args[1], args[2]),
    "dump-blp-png" => DumpBlpPng(args[1], args[2], args[3]),
    "dump-file" => DumpFile(args[1], args[2], args[3]),
    "spike-dbcd" => SpikeDbcd(args[1], args[2]),
    "spike-talenttab" => SpikeTalentTab(args[1], args[2]),
    "classes-with-talents" => ClassesWithTalents(args[1], args[2]),
    "list-talenttabs" => ListTalentTabs(args[1], args[2]),
    "spike-spell" => SpikeSpell(args[1], args[2]),
    "raw-talent-record" => RawTalentRecord(args[1], int.Parse(args[2])),
    "raw-talent-by-id" => RawTalentById(args[1], int.Parse(args[2])),
    "raw-talenttab-by-id" => RawTalentTabById(args[1], int.Parse(args[2])),
    "talents-in-tab" => TalentsInTab(args[1], args[2], int.Parse(args[3])),
    "talent-prereqs" => TalentPrereqs(args[1], args[2], int.Parse(args[3])),
    "spike-chrclasses" => SpikeChrClasses(args[1], args[2]),
    "spike-format-description" => SpikeFormatDescription(args[1], args[2], int.Parse(args[3])),
    "resolve-icon" => ResolveIcon(args[1], args[2], int.Parse(args[3])),
    "extract-icon" => ExtractIcon(args[1], args[2], int.Parse(args[3]), args[4], args[5]),
    "extract-build" => ExtractBuild(args[1], args[2], args[3], args[4]),
    "extract-class-icons" => ExtractClassIcons(args[1], args[2], args[3]),
    "find-spell-attr" => FindSpellAttr(args[1], args[2], args[3], args[4], args[5]),
    "dump-spell-ranges" => DumpSpellRanges(args[1], args[2]),
    "spike-ability-fields" => SpikeAbilityFields(args[1], args[2], int.Parse(args[3])),
    "spike-spell-full" => SpikeSpellFull(args[1], args[2], int.Parse(args[3])),
    "dump-shapeshift-forms" => DumpShapeshiftForms(args[1], args[2]),
    "dump-equip-patterns" => DumpEquipPatterns(args[1], args[2]),
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
/// <summary>Decodes one arbitrary BLP (any path, any size - not just Icons/*) straight to a
/// PNG file on disk, bypassing the content-hash storage pipeline entirely. For inspecting a
/// texture (dimensions, content) before deciding how to use it - e.g. checking a sprite
/// sheet's real pixel size against UV coordinates found in a .lua before writing crop code.</summary>
static int DumpBlpPng(string clientDir, string internalPathNoExt, string outFile)
{
    var dataDir = Path.Combine(clientDir, "Data");
    using var iface = MpqArchive.Open(Path.Combine(dataDir, "interface.MPQ"));
    PatchChain.ApplyAll(iface, dataDir);

    var resolved = IconFileResolver.Resolve(iface, internalPathNoExt);
    if (resolved is null)
        return Fail($"Not found (neither .blp nor .tga): {internalPathNoExt}");
    if (!resolved.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
        return Fail($"Not a BLP: {resolved}");

    var pngBytes = Extractor.Blp.BlpConverter.ConvertToPng(iface.ReadFile(resolved));
    File.WriteAllBytes(outFile, pngBytes);

    using var img = SixLabors.ImageSharp.Image.Load(outFile);
    Console.WriteLine($"{resolved}: {img.Width}x{img.Height} -> {outFile}");
    return 0;
}

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

// Independent cross-check of OrderIndex, reading raw bytes directly - bypasses DBCD/
// DbcClient entirely, to rule out a mapping bug on our side vs. real (possibly tied) data.
// Layout: ID, Name_lang(9 field-slots: 8 locale offsets + mask), SpellIconID, RaceMask,
// ClassMask, OrderIndex, BackgroundFile - matches fieldCount=15/recordSize=60, the layout
// covering 0.9.0.3807-0.12.0.3988 and 1.0.0.3980-1.12.3.6141 (see dbd-definitions/
// TalentTab.dbd). Fails loudly if fieldCount doesn't match, rather than silently reading
// the wrong offset for an older/newer build with a different layout.
static int RawTalentTabById(string clientDir, int targetId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var bytes = archive.ReadFile(@"DBFilesClient\TalentTab.dbc");

    var recordCount = (int)BitConverter.ToUInt32(bytes, 4);
    var fieldCount = (int)BitConverter.ToUInt32(bytes, 8);
    var recordSize = (int)BitConverter.ToUInt32(bytes, 12);
    if (fieldCount != 15 || recordSize != 60)
        return Fail($"Unexpected TalentTab.dbc layout (fieldCount={fieldCount}, recordSize={recordSize}) - " +
                     "this command only knows the ID/Name_lang(9)/SpellIconID/RaceMask/ClassMask/OrderIndex/BackgroundFile layout (fieldCount=15).");

    for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
    {
        var offset = 20 + recordIndex * recordSize;
        var id = BitConverter.ToInt32(bytes, offset);
        if (id != targetId) continue;

        int I(int fieldOffset) => BitConverter.ToInt32(bytes, offset + fieldOffset);
        var spellIconId = I(40);  // field 10: (1 + 9) * 4
        var raceMask = I(44);     // field 11
        var classMask = I(48);    // field 12
        var orderIndex = I(52);   // field 13

        Console.WriteLine($"found id={id} at physical record index {recordIndex} (byte {offset}): " +
                           $"spellIconId={spellIconId} raceMask={raceMask} classMask={classMask} orderIndex={orderIndex}");
        return 0;
    }

    return Fail($"No record with ID={targetId} found among {recordCount} records.");
}

// One-off raw dump of every SpellRange.dbc row (numeric fields only - id/minRange/maxRange/
// flags, skipping the DisplayName_lang/DisplayNameShort_lang string block) to verify the
// Flags bit-0x1-means-melee assumption against real vanilla data before trusting it in
// DbcClient.GetSpellRange. Layout: ID<32>, RangeMin<32>, RangeMax<32>, Flags<32>, then two
// locstring blocks (not read here) - fieldCount=22/recordSize=88, confirmed via
// raw-dbc-header.
static int DumpSpellRanges(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var bytes = archive.ReadFile(@"DBFilesClient\SpellRange.dbc");

    var recordCount = (int)BitConverter.ToUInt32(bytes, 4);
    var fieldCount = (int)BitConverter.ToUInt32(bytes, 8);
    var recordSize = (int)BitConverter.ToUInt32(bytes, 12);
    if (fieldCount != 22 || recordSize != 88)
        return Fail($"Unexpected SpellRange.dbc layout (fieldCount={fieldCount}, recordSize={recordSize}) - this command only knows the 22-field/88-byte vanilla layout.");

    for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
    {
        var offset = 20 + recordIndex * recordSize;
        var id = BitConverter.ToInt32(bytes, offset);
        var minRange = BitConverter.ToSingle(bytes, offset + 4);
        var maxRange = BitConverter.ToSingle(bytes, offset + 8);
        var flags = BitConverter.ToInt32(bytes, offset + 12);
        Console.WriteLine($"id={id} minRange={minRange} maxRange={maxRange} flags=0x{flags:X} (melee bit 0x1: {(flags & 0x1) != 0})");
    }

    return 0;
}

// One-off spike: dump every field needed for the "active ability" tooltip rows (cost/range/
// cast time/cooldown) for one spell, to verify real vanilla values before trusting the
// formatting logic on them.
static int SpikeAbilityFields(string clientDir, string build, int spellId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var spell = client.GetSpell(build, spellId);
    if (spell is null)
        return Fail($"Spell {spellId} not found");

    var range = client.GetSpellRange(build, spell.RangeIndex);
    var castTimeMs = client.GetSpellCastTimeMs(build, spell.CastingTimeIndex);

    Console.WriteLine($"{spell.Name} (spell {spellId}):");
    Console.WriteLine($"  Attributes=0x{spell.Attributes:X8}");
    Console.WriteLine($"  PowerType={spell.PowerType} ManaCost={spell.ManaCost}");
    Console.WriteLine($"  RangeIndex={spell.RangeIndex} -> {(range is null ? "NOT FOUND" : $"min={range.MinRange} max={range.MaxRange} flags=0x{range.Flags:X} isMelee={range.IsMelee}")}");
    Console.WriteLine($"  CastingTimeIndex={spell.CastingTimeIndex} -> {(castTimeMs is null ? "NOT FOUND" : $"{castTimeMs} ms")}");
    Console.WriteLine($"  RecoveryTime={spell.RecoveryTime} CategoryRecoveryTime={spell.CategoryRecoveryTime}");

    return 0;
}

// One-off: dump every SpellRecord field for a spell, to debug an escape-sequence formatting
// bug ($t reading EffectAmplitude=0 for a totem's periodic-tick spell - see gotchas.md).
static int SpikeSpellFull(string clientDir, string build, int spellId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var spell = client.GetSpell(build, spellId);
    if (spell is null)
        return Fail($"Spell {spellId} not found");

    var durationMs = client.GetSpellDurationMs(build, spell.DurationIndex);

    Console.WriteLine($"{spell.Name} (spell {spellId}):");
    Console.WriteLine($"  EffectBasePoints=[{string.Join(",", spell.EffectBasePoints)}]");
    Console.WriteLine($"  EffectAuraPeriod=[{string.Join(",", spell.EffectAuraPeriod)}]");
    Console.WriteLine($"  EffectAmplitude=[{string.Join(",", spell.EffectAmplitude)}]");
    Console.WriteLine($"  EffectRadiusIndex=[{string.Join(",", spell.EffectRadiusIndex)}]");
    Console.WriteLine($"  EffectMiscValue=[{string.Join(",", spell.EffectMiscValue)}]");
    Console.WriteLine($"  EffectPointsPerCombo=[{string.Join(",", spell.EffectPointsPerCombo)}]");
    Console.WriteLine($"  DurationIndex={spell.DurationIndex} -> {(durationMs is null ? "NOT FOUND" : $"{durationMs} ms")}");
    Console.WriteLine($"  ProcChance={spell.ProcChance} ProcCharges={spell.ProcCharges} CumulativeAura={spell.CumulativeAura}");
    Console.WriteLine($"  MaxTargetLevel={spell.MaxTargetLevel?.ToString() ?? "n/a"} EffectChainTargets={(spell.EffectChainTargets is null ? "n/a" : string.Join(",", spell.EffectChainTargets))}");
    Console.WriteLine($"  Attributes=0x{spell.Attributes:X8} PowerType={spell.PowerType} ManaCost={spell.ManaCost}");
    Console.WriteLine($"  RangeIndex={spell.RangeIndex} CastingTimeIndex={spell.CastingTimeIndex}");
    Console.WriteLine($"  RecoveryTime={spell.RecoveryTime} CategoryRecoveryTime={spell.CategoryRecoveryTime}");
    Console.WriteLine($"  ShapeshiftMask=0x{spell.ShapeshiftMask:X8} ShapeshiftExclude={(spell.ShapeshiftExclude is null ? "n/a" : $"0x{spell.ShapeshiftExclude:X8}")}");
    Console.WriteLine($"  EquippedItemClass={spell.EquippedItemClass} EquippedItemSubclass=0x{spell.EquippedItemSubclass:X8} EquippedItemInvTypes={(spell.EquippedItemInvTypes is null ? "n/a" : $"0x{spell.EquippedItemInvTypes:X8}")}");

    return 0;
}

static int DumpShapeshiftForms(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    foreach (var form in client.GetAllShapeshiftForms(build))
        Console.WriteLine($"  id={form.Id} bit={form.Id - 1} (mask 0x{1 << (form.Id - 1):X8}) name=\"{form.Name}\"");

    return 0;
}

/// <summary>Every distinct (EquippedItemClass, EquippedItemSubclass) pair across every real
/// spell referenced by any talent rank in this build - to check whether ResolveEquipRequirement
/// should match a class-2 (Weapon) subclass mask exactly, or by intersection, before deciding
/// which (see the exact-match-vs-narrower-real-restriction question this was written to
/// answer).</summary>
static int DumpEquipPatterns(string clientDir, string build)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var talents = client.ReadTalents(build);
    var seen = new Dictionary<(int, int), (int spellId, string name)>();
    foreach (var talent in talents)
    {
        foreach (var spellId in talent.SpellRanks)
        {
            if (spellId == 0) continue;
            var spell = client.GetSpell(build, spellId);
            if (spell is null || spell.EquippedItemClass == -1) continue;

            var key = (spell.EquippedItemClass, spell.EquippedItemSubclass);
            seen.TryAdd(key, (spellId, spell.Name));
        }
    }

    foreach (var ((itemClass, subclass), (spellId, name)) in seen.OrderBy(kv => kv.Key))
        Console.WriteLine($"  class={itemClass} subclass=0x{subclass:X8}  e.g. spell {spellId} \"{name}\"");

    return 0;
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

/// <summary>Crops the 9 vanilla class icons out of the character-creation-screen sprite
/// sheet (Interface\Glues\CharacterCreate\UI-CharacterCreate-Classes.blp) and writes each
/// into `classes.icon_path`. One-off, NOT part of extract-build: `classes` is a global
/// table (not scoped by client_build_id - a class doesn't change between patches), so this
/// only ever needs to run once against any single build that has the sprite (confirmed
/// visually against 1.12.1.5875 - see gotchas.md). UV coordinates below are copied from
/// CLASS_ICON_TCOORDS in the client's own Interface\GlueXML\CharacterCreate.lua, not
/// guessed - verified the resulting 256x256 sprite crops cleanly into a 4-col grid (with a
/// few px of anti-bleed trim on some edges, matching the slightly-off 0.49609375-style
/// fractions) by decoding it and looking at it (dump-blp-png).</summary>
static int ExtractClassIcons(string clientDir, string envFilePath, string storageIconsDir)
{
    // Own sibling folder, not mixed into storage/icons/ - these are class-selection-screen
    // portraits, a different kind of asset from ability/talent icons and talent-tab
    // backgrounds (which likewise get their own storage/backgrounds/, see BuildExtractor).
    var classIconsDir = Path.Combine(Path.GetDirectoryName(storageIconsDir.TrimEnd('/', '\\')) ?? ".", "class-icons");

    var classIconTexCoords = new Dictionary<string, (float X0, float X1, float Y0, float Y1)>
    {
        ["warrior"] = (0f, 0.25f, 0f, 0.25f),
        ["mage"] = (0.25f, 0.49609375f, 0f, 0.25f),
        ["rogue"] = (0.49609375f, 0.7421875f, 0f, 0.25f),
        ["druid"] = (0.7421875f, 0.98828125f, 0f, 0.25f),
        ["hunter"] = (0f, 0.25f, 0.25f, 0.5f),
        ["shaman"] = (0.25f, 0.49609375f, 0.25f, 0.5f),
        ["priest"] = (0.49609375f, 0.7421875f, 0.25f, 0.5f),
        ["warlock"] = (0.7421875f, 0.98828125f, 0.25f, 0.5f),
        ["paladin"] = (0f, 0.25f, 0.5f, 0.75f),
    };

    var connectionString = EnvFileConnectionString.ReadMySqlConnectionString(envFilePath);
    var dataDir = Path.Combine(clientDir, "Data");

    using var iface = MpqArchive.Open(Path.Combine(dataDir, "interface.MPQ"));
    PatchChain.ApplyAll(iface, dataDir);

    const string spritePath = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-Classes";
    var resolved = IconFileResolver.Resolve(iface, spritePath);
    if (resolved is null)
        return Fail($"Sprite sheet not found: {spritePath}");

    using var sprite = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(
        Extractor.Blp.BlpConverter.ConvertToPng(iface.ReadFile(resolved)));

    Directory.CreateDirectory(classIconsDir);

    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    foreach (var (slug, coords) in classIconTexCoords)
    {
        var rect = new SixLabors.ImageSharp.Rectangle(
            (int)Math.Round(coords.X0 * sprite.Width),
            (int)Math.Round(coords.Y0 * sprite.Height),
            (int)Math.Round((coords.X1 - coords.X0) * sprite.Width),
            (int)Math.Round((coords.Y1 - coords.Y0) * sprite.Height));

        using var cropped = sprite.Clone(ctx => ctx.Crop(rect));
        using var output = new MemoryStream();
        cropped.SaveAsPng(output);
        var pngBytes = output.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();

        var outPath = Path.Combine(classIconsDir, $"{hash}.png");
        if (!File.Exists(outPath))
            File.WriteAllBytes(outPath, pngBytes);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE classes SET icon_path = @icon WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@icon", hash);
        cmd.Parameters.AddWithValue("@slug", slug);
        var rows = cmd.ExecuteNonQuery();

        Console.WriteLine($"{slug}: {rect} -> {hash}.png (rows updated: {rows})");
    }

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

// One-off diagnostic: does any talent rank's spell for a given class/build have one of the
// given Spell.Attributes bits set (e.g. SPELL_ATTR_PASSIVE=0x40, SPELL_ATTR_IS_ABILITY=0x10)?
// Reads the talent list from OUR OWN database (already extracted, has names/ranks/tab
// grouping ready) and cross-checks Attributes against the live client via DBCD - not from
// Spell.dbc data cached in the DB, since Attributes isn't part of SpellRecord (see
// DbcClient.GetSpellAttributes docblock).
static int FindSpellAttr(string clientDir, string build, string envFilePath, string classSlug, string hexFlagsCsv)
{
    var flags = hexFlagsCsv.Split(',').Select(s => Convert.ToInt32(s.Trim(), 16)).ToArray();

    var connectionString = EnvFileConnectionString.ReadMySqlConnectionString(envFilePath);
    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    var rows = new List<(int SpellId, string Name, int RankIndex, string TabName)>();
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            SELECT tr.spell_id, tr.name, tr.rank_index, tt.name AS tab_name
            FROM talent_ranks tr
            JOIN talents t ON t.id = tr.talent_id
            JOIN talent_tabs tt ON tt.id = t.talent_tab_id
            JOIN client_builds cb ON cb.id = tt.client_build_id
            JOIN classes c ON c.id = tt.character_class_id
            WHERE cb.label = @build AND c.slug = @slug
            ORDER BY tt.order_index, tt.id, t.tier, t.column_index, tr.rank_index
            """;
        cmd.Parameters.AddWithValue("@build", build);
        cmd.Parameters.AddWithValue("@slug", classSlug);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
    }

    if (rows.Count == 0)
        return Fail($"No talent ranks found for class \"{classSlug}\" on build \"{build}\" - wrong slug/build, or not extracted yet?");

    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var matchCount = 0;
    foreach (var (spellId, name, rankIndex, tabName) in rows)
    {
        var attrs = client.GetSpellAttributes(build, spellId);
        if (attrs is null)
        {
            Console.WriteLine($"[{tabName}] {name} (rank {rankIndex}, spell {spellId}): NOT FOUND in Spell.dbc");
            continue;
        }

        var matched = flags.Where(f => (attrs.Value & f) != 0).ToArray();
        if (matched.Length == 0) continue;

        matchCount++;
        var matchedHex = string.Join(", ", matched.Select(f => $"0x{f:X8}"));
        Console.WriteLine($"[{tabName}] {name} (rank {rankIndex}, spell {spellId}): Attributes=0x{attrs.Value:X8} matches [{matchedHex}]");
    }

    Console.WriteLine($"--- {matchCount} of {rows.Count} talent ranks matched ---");
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

/// <summary>Scans every talent rank's RAW (pre-SpellDescriptionFormatter) Spell.Description
/// text for a given regex - used to check the source data straight from the DBC for escape
/// syntax the formatter doesn't handle, rather than trusting the already-formatted text
/// stored in MySQL (a leftover "$" there would already reveal an unresolved token, but only
/// for tokens whose failure mode leaves "$" behind - checking the raw text directly is the
/// only way to be sure about a syntax hypothesis, e.g. "+"/"-" modifiers).</summary>
static int GrepRawDescriptions(string clientDir, string build, string pattern)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);
    var regex = new System.Text.RegularExpressions.Regex(pattern);

    var talents = client.ReadTalents(build);
    var seenSpellIds = new HashSet<int>();
    var matches = 0;

    foreach (var t in talents)
    {
        foreach (var spellId in t.SpellRanks.Where(id => id != 0))
        {
            if (!seenSpellIds.Add(spellId)) continue; // same spell can back multiple talents

            var spell = client.GetSpell(build, spellId);
            if (spell?.Description is null) continue;

            if (regex.IsMatch(spell.Description))
            {
                matches++;
                Console.WriteLine($"spell {spellId} \"{spell.Name}\": {spell.Description}");
            }
        }
    }

    Console.WriteLine($"{matches} match(es) out of {seenSpellIds.Count} distinct raw spell descriptions checked");
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

static int TalentPrereqs(string clientDir, string build, int talentId)
{
    using var archive = OpenPatchedDbc(clientDir);
    var client = MakeDbcClient(archive);

    var talents = client.ReadTalents(build);
    var talent = talents.FirstOrDefault(t => t.Id == talentId);
    if (talent is null)
        return Fail($"Talent {talentId} not found");

    Console.WriteLine($"Talent #{talent.Id} tabId={talent.TabId} tier={talent.Tier} col={talent.ColumnIndex} flags={talent.Flags}");
    for (var i = 0; i < talent.PrereqTalent.Length; i++)
    {
        if (talent.PrereqTalent[i] == 0) continue;
        var prereq = talents.FirstOrDefault(t => t.Id == talent.PrereqTalent[i]);
        var prereqSpell = prereq is null ? null : client.GetSpell(build, prereq.SpellRanks.First(id => id != 0));
        Console.WriteLine($"  PrereqTalent[{i}]={talent.PrereqTalent[i]} (\"{prereqSpell?.Name ?? "?"}\", tabId={prereq?.TabId}) PrereqRank[{i}]={talent.PrereqRank[i]}");
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
