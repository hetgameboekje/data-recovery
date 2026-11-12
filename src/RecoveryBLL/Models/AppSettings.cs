namespace RecoveryBLL.Models;

public enum DatabaseProvider { Sqlite, SqlServer }

/// <summary>
/// Settings from the .env file (and environment variables). Secrets stay in here and are never logged:
/// use <see cref="SafeConnectionDescription"/> for display.
/// </summary>
public sealed class AppSettings
{
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;
    public required string ConnectionString { get; init; }
    public required string SafeConnectionDescription { get; init; }
    public required string DictionariesFile { get; init; }
    public required string LogDirectory { get; init; }
    public string? EnvFile { get; init; }
    public NetworkExportSettings Network { get; init; } = new();
}

/// <summary>Phase 9 (network export via FTP/SFTP). Read now so the .env layout is settled; not used yet.</summary>
public sealed class NetworkExportSettings
{
    public string Protocol { get; init; } = "sftp";
    public string? Host { get; init; }
    public int Port { get; init; } = 22;
    public string? User { get; init; }
    public string? Password { get; init; }
    public string? PrivateKeyFile { get; init; }
    public string? PrivateKeyPassphrase { get; init; }
    public string? HostKeyFingerprint { get; init; }
    public string? RemoteDirectory { get; init; }
    public string? BacklogDirectory { get; init; }
    public string? OverrideDirectory { get; init; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
