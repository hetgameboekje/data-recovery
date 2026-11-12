using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Storage;

/// <summary>
/// The run-state journal as a small JSON file next to the logs (run-state.json). A lock file serialises
/// the read-modify-write between the desktop app and rtcatalog; the JSON is replaced atomically, so a
/// crash in the middle of a write never leaves half a file.
/// </summary>
public sealed class RunStateFile(string directory) : IRunStateStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly string file = Path.Combine(directory, "run-state.json");
    readonly string lockFile = Path.Combine(directory, "run-state.lock");

    public string Location => file;

    public IReadOnlyList<RunLease> Update(Action<List<RunLease>> change)
    {
        Directory.CreateDirectory(directory);
        using var gate = AcquireLock();
        var list = Read();
        var before = JsonSerializer.Serialize(list, Json);
        change(list);
        var after = JsonSerializer.Serialize(list, Json);
        if (after != before)
        {
            var temp = file + ".tmp";
            File.WriteAllText(temp, after);
            File.Move(temp, file, overwrite: true);
        }
        return list.ToList();
    }

    List<RunLease> Read()
    {
        if (!File.Exists(file)) return [];
        try { return JsonSerializer.Deserialize<List<RunLease>>(File.ReadAllText(file), Json) ?? []; }
        catch (JsonException)
        {
            // A damaged journal must not block the toolkit: keep it for inspection and start empty.
            File.Move(file, file + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            return [];
        }
    }

    FileStream AcquireLock()
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // DeleteOnClose: an abandoned lock disappears with the process that held it.
                return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (sw.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(50);
            }
        }
    }
}

public sealed class ProcessInfo : IProcessInfo
{
    static readonly Lazy<(int Id, long Start)> Me = new(() =>
    {
        using var p = Process.GetCurrentProcess();
        return (p.Id, p.StartTime.ToUniversalTime().Ticks);
    });

    public int CurrentId => Me.Value.Id;
    public long CurrentStartTicks => Me.Value.Start;

    public bool IsAlive(int processId, long startTicks)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            if (p.HasExited) return false;
            // Same id but a different start time = the id was reused by another program: the owner is gone.
            return Math.Abs(p.StartTime.ToUniversalTime().Ticks - startTicks) < TimeSpan.TicksPerSecond;
        }
        catch (ArgumentException) { return false; }                 // no such process
        catch (InvalidOperationException) { return false; }          // exited meanwhile
        catch (Win32Exception) { return true; }                      // cannot inspect (other user/elevated): assume alive
    }
}
