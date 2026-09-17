using System.Text.RegularExpressions;

namespace Extractor.Storage;

/// <summary>
/// Reads DATABASE_URL straight out of a Symfony .env-style file and converts it to a
/// MySqlConnector connection string — so callers pass a file PATH, never the credential
/// itself. Keeps the actual password out of CLI args / shell history / this session's own
/// transcript.
/// </summary>
public static partial class EnvFileConnectionString
{
    [GeneratedRegex(@"^DATABASE_URL\s*=\s*""?mysql://(?<user>[^:]+):(?<pass>[^@]+)@(?<host>[^:/]+):(?<port>\d+)/(?<db>[^?""]+)", RegexOptions.Multiline)]
    private static partial Regex DatabaseUrlRegex();

    public static string ReadMySqlConnectionString(string envFilePath)
    {
        if (!File.Exists(envFilePath))
            throw new FileNotFoundException($"Env file not found: {envFilePath}");

        var text = File.ReadAllText(envFilePath);
        var match = DatabaseUrlRegex().Match(text);
        if (!match.Success)
            throw new FormatException($"No mysql:// DATABASE_URL found in {envFilePath}");

        return $"Server={match.Groups["host"].Value};" +
               $"Port={match.Groups["port"].Value};" +
               $"Database={match.Groups["db"].Value};" +
               $"User={match.Groups["user"].Value};" +
               $"Password={match.Groups["pass"].Value};";
    }
}
