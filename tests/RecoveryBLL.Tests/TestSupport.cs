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

    /// <summary>The real BLL + DAL wiring, against a SQLite file in <paramref name="dir"/>.</summary>
    public static ServiceProvider Create(string dir, ListLog log, AppSettings? settings = null)
    {
        settings ??= new AppSettings
        {
            Provider = DatabaseProvider.Sqlite,
            ConnectionString = $"Data Source={System.IO.Path.Combine(dir, "catalog.db")}",
            SafeConnectionDescription = "test",
            DictionariesFile = DictionariesFile,
            LogDirectory = dir,
        };
        var sp = new ServiceCollection()
            .AddRecoveryDal(settings)
            .AddRecoveryBll()
            .AddSingleton<IOperationLog>(log)
            .BuildServiceProvider();
        sp.GetRequiredService<Services.CatalogService>().Initialize();
        return sp;
    }

    public static byte[] Random(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }
}
