using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryDAL;
using RecoveryDAL.Configuration;

namespace RecoveryBLL.Tests;

sealed class ListLog : IOperationLog
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public void Info(string message) => Lines.Enqueue(message);
    public void Warn(string message) => Lines.Enqueue("WARN " + message);
}

/// <summary>Protects no folder, but knows the real Windows drive (so the catalog in %TEMP% is "on the Windows drive": a warning, not a refusal).</summary>
sealed class NoProtection : IProtectedLocations
{
    public string SystemDrive => System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    public IReadOnlyList<string> NoGoFolders => [];
    public string Normalize(string path) => System.IO.Path.GetFullPath(path).TrimEnd('\\');
}

/// <summary>Step 2 of a deletion, answered by the test. Records how often it was asked.</summary>
sealed class TestConfirmer(bool answer) : IDeletionConfirmer
{
    public int Asked { get; private set; }
    public bool ConfirmFinal(DeletionBatch batch) { Asked++; return answer; }
}

/// <summary>Reads like the real reader, but runs <see cref="BeforeOpen"/> first (used to "unplug" a disk mid-run).</summary>
sealed class HookedReader : IFileContentReader
{
    readonly IFileContentReader real = new RecoveryDAL.Storage.FileContentReader();
    public Action<string>? BeforeOpen { get; set; }
    public Stream OpenRead(string fullPath, bool sequential)
    {
        BeforeOpen?.Invoke(fullPath);
        return real.OpenRead(fullPath, sequential);
    }
}

/// <summary>A temp folder that deletes itself.</summary>
sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rt-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string relative, byte[] content)
    {
        var p = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, content);
        return p;
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

static class TestHost
{
    public static string DictionariesFile => System.IO.Path.Combine(AppContext.BaseDirectory, "config", "dictionaries.xml");

    /// <summary>
    /// The real BLL + DAL wiring, against a SQLite file in <paramref name="dir"/>.
    /// <paramref name="realProtection"/> false: test folders live in %TEMP% (inside AppData), which the real
    /// protected-folder list refuses, so most tests use a list that protects nothing.
    /// <paramref name="configure"/>: replace services (a confirmer that says yes, a reader that "unplugs" a disk...).
    /// </summary>
    public static ServiceProvider Create(string dir, ListLog log, AppSettings? settings = null, bool realProtection = false,
                                         Action<IServiceCollection>? configure = null)
    {
        settings ??= new AppSettings
        {
            Provider = DatabaseProvider.Sqlite,
            ConnectionString = $"Data Source={System.IO.Path.Combine(dir, "catalog.db")}",
            SafeConnectionDescription = "test",
            DictionariesFile = DictionariesFile,
            LogDirectory = dir,
        };
        var services = new ServiceCollection()
            .AddRecoveryDal(settings)
            .AddRecoveryBll()
            .AddSingleton<IOperationLog>(log);
        if (!realProtection) services.AddSingleton<IProtectedLocations>(new NoProtection());
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<Services.CatalogService>().Initialize();
        return sp;
    }

    /// <summary>Runs <paramref name="test"/> against a throw-away SQL Server LocalDB database.</summary>
    public static void WithLocalDb(TempDir dir, Action<ServiceProvider> test)
    {
        var dbName = "RtTest_" + Guid.NewGuid().ToString("N")[..8];
        var cs = $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True";
        try
        {
            using var sp = Create(dir.Path, new ListLog(), new AppSettings
            {
                Provider = DatabaseProvider.SqlServer, ConnectionString = cs, SafeConnectionDescription = "test",
                DictionariesFile = DictionariesFile, LogDirectory = dir.Path,
            });
            test(sp);
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            using var c = new Microsoft.Data.SqlClient.SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END";
            cmd.ExecuteNonQuery();
        }
    }

    public static byte[] Random(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }
}
