using DBCD.Providers;
using Extractor.Mpq;

namespace Extractor.Dbc;

/// <summary>
/// Feeds DBCD directly from an already-opened (and patch-chained) MpqArchive —
/// no intermediate extraction to disk. DBCD only ever asks for "{tableName}.dbc/.db2",
/// we translate that into the client's real internal path.
/// </summary>
public sealed class MpqDbcProvider(MpqArchive archive) : IDBCProvider
{
    public Stream StreamForTableName(string tableName, string build)
    {
        var internalPath = $@"DBFilesClient\{tableName}.dbc";
        return new MemoryStream(archive.ReadFile(internalPath));
    }
}
