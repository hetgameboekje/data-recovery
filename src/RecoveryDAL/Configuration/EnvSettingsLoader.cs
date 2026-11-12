using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;

namespace RecoveryDAL.Configuration;

/// <summary>
/// Builds <see cref="AppSettings"/> from a .env file. Real environment variables override the file,
/// so secrets can also come from the machine instead of a file. Relative paths are relative to the .env file.
/// </summary>
public static class EnvSettingsLoader
{
    public const string FileName = ".env";

    /// <param name="envFile">Explicit .env path; otherwise searched from the current folder upwards, then next to the exe.</param>
    /// <param name="sqliteFileOverride">Command-line --db: use this SQLite file regardless of the .env.</param>
    public static AppSettings Load(string? envFile = null, string? sqliteFileOverride = null)
    {
        var path = envFile != null ? Path.GetFullPath(envFile) : Find();
        if (envFile != null && !File.Exists(path)) throw new ConfigurationException($".env file not found: {path}");

        Dictionary<string, string> file;
        try { file = path != null ? EnvFile.Parse(File.ReadAllText(path)) : new(StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            throw new ConfigurationException($"Cannot read {path}: {ex.Message}", ex);
        }

        var baseDir = path != null ? Path.GetDirectoryName(path)! : Directory.GetCurrentDirectory();
        string? Get(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } env ? env
                                 : file.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
        string Resolve(string p) => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));

        DatabaseProvider provider;
        string connection;
        if (sqliteFileOverride != null)
        {
            provider = DatabaseProvider.Sqlite;
            connection = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(sqliteFileOverride) }.ToString();
        }
        else
        {
            provider = (Get("DB_PROVIDER") ?? "sqlite").ToLowerInvariant() switch
            {
                "sqlite" => DatabaseProvider.Sqlite,
                "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
                var other => throw new ConfigurationException($"DB_PROVIDER '{other}' is not supported. Use sqlite or sqlserver."),
            };
            connection = Get("DB_CONNECTION") ?? (provider == DatabaseProvider.Sqlite
                ? "Data Source=catalog.db"
                : throw new ConfigurationException("DB_CONNECTION is required for SQL Server."));
            if (provider == DatabaseProvider.Sqlite)
            {
                var b = new SqliteConnectionStringBuilder(connection);
                b.DataSource = Resolve(b.DataSource);
                connection = b.ToString();
            }
        }

        var dictionaries = Get("DICTIONARIES_FILE") is { } d ? Resolve(d) : FindDictionaries(baseDir);
        var logDir = Get("LOG_DIR") is { } l ? Resolve(l)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecoveryToolkit", "logs");

        return new AppSettings
        {
            Provider = provider,
            ConnectionString = connection,
            SafeConnectionDescription = Describe(provider, connection),
            DictionariesFile = dictionaries,
            LogDirectory = logDir,
            EnvFile = path,
            Network = new NetworkExportSettings
            {
                Protocol = (Get("NET_PROTOCOL") ?? "sftp").ToLowerInvariant(),
                Host = Get("NET_HOST"),
                Port = int.TryParse(Get("NET_PORT"), out var port) ? port : 22,
                User = Get("NET_USER"),
                Password = Get("NET_PASSWORD"),
                PrivateKeyFile = Get("NET_PRIVATE_KEY") is { } k ? Resolve(k) : null,
                PrivateKeyPassphrase = Get("NET_PRIVATE_KEY_PASSPHRASE"),
                HostKeyFingerprint = Get("NET_HOST_KEY_FINGERPRINT"),
                RemoteDirectory = Get("NET_REMOTE_DIR"),
                BacklogDirectory = Get("BACKLOG_DIR") is { } bl ? Resolve(bl) : null,
                OverrideDirectory = Get("OVERRIDE_DIR") is { } od ? Resolve(od) : null,
            },
        };
    }

    /// <summary>Connection info without passwords, safe for the UI and logs.</summary>
    public static string Describe(DatabaseProvider provider, string connection)
    {
        if (provider == DatabaseProvider.Sqlite)
            return "SQLite: " + new SqliteConnectionStringBuilder(connection).DataSource;
        var b = new SqlConnectionStringBuilder(connection);
        var auth = b.IntegratedSecurity ? "Windows login" : $"user {b.UserID}";
        return $"SQL Server: {b.DataSource} / {b.InitialCatalog} ({auth})";
    }

    static string? Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; dir != null && i < 6; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    static string FindDictionaries(string baseDir)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "config", "dictionaries.xml"),
                     Path.Combine(AppContext.BaseDirectory, "config", "dictionaries.xml"),
                 })
            if (File.Exists(candidate)) return candidate;
        return Path.Combine(AppContext.BaseDirectory, "config", "dictionaries.xml");
    }
}
