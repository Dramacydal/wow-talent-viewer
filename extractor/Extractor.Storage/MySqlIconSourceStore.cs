using Extractor.Blp;
using MySqlConnector;

namespace Extractor.Storage;

/// <summary>Real backing store for IIconSourceStore — the icon_sources table (global, not
/// build-scoped, see .claude-docs/architecture.md). Replaces the JsonFileIconSourceStore
/// placeholder used during earlier spikes.
/// <paramref name="getTransaction"/> is called per-command rather than captured once,
/// because BuildExtractor creates this store before its transaction exists (field
/// initializer ordering) and the transaction can be replaced (e.g. after commit/rollback).
/// </summary>
public sealed class MySqlIconSourceStore(MySqlConnection connection, Func<MySqlTransaction?> getTransaction) : IIconSourceStore
{
    private MySqlCommand Command(string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = getTransaction();
        cmd.CommandText = sql;
        return cmd;
    }

    public string? TryGetIconPath(string mpqPath, string sourceHash)
    {
        using var cmd = Command("SELECT icon_path FROM icon_sources WHERE mpq_path = @mpqPath AND source_hash = @sourceHash");
        cmd.Parameters.AddWithValue("@mpqPath", mpqPath);
        cmd.Parameters.AddWithValue("@sourceHash", sourceHash);
        return cmd.ExecuteScalar() as string;
    }

    public void Upsert(string mpqPath, string sourceHash, string iconPath)
    {
        using var cmd = Command("""
            INSERT INTO icon_sources (mpq_path, source_hash, icon_path)
            VALUES (@mpqPath, @sourceHash, @iconPath)
            ON DUPLICATE KEY UPDATE source_hash = @sourceHash, icon_path = @iconPath
            """);
        cmd.Parameters.AddWithValue("@mpqPath", mpqPath);
        cmd.Parameters.AddWithValue("@sourceHash", sourceHash);
        cmd.Parameters.AddWithValue("@iconPath", iconPath);
        cmd.ExecuteNonQuery();
    }
}
