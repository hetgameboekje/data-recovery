using RecoveryBLL.Interfaces;

namespace RecoveryUI.Infrastructure;

/// <summary>IOperationLog for the desktop app: daily log file + an event the main window shows in its log panel.</summary>
public sealed class UiLog(string logDirectory) : IOperationLog
{
    static readonly object Gate = new();
    public string FilePath { get; } = Path.Combine(logDirectory, $"toolkit-{DateTime.Now:yyyyMMdd}.log");
    public event Action<string, bool>? LineWritten;

    public void Info(string message) => Write(message, false);
    public void Warn(string message) => Write(message, true);

    void Write(string message, bool warn)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {(warn ? "WARN  " : "")}{message}{Environment.NewLine}");
            }
            catch (IOException) { /* logging must never stop a scan */ }
        }
        LineWritten?.Invoke(message, warn);
    }
}

/// <summary>
/// Runs one long operation at a time on a worker thread, with cancellation.
/// Shared by all controllers so two scans can never run at once.
/// </summary>
public sealed class OperationRunner
{
    CancellationTokenSource? cts;
    public bool IsBusy => cts != null;
    public event Action<bool, string?>? BusyChanged;

    public async Task<bool> RunAsync(string name, Action<CancellationToken> work, Action<Exception> onError)
    {
        if (cts != null) return false;
        cts = new CancellationTokenSource();
        BusyChanged?.Invoke(true, name);
        try
        {
            var token = cts.Token;
            await Task.Run(() => work(token));
            return true;
        }
        catch (Exception ex)
        {
            onError(ex);
            return false;
        }
        finally
        {
            cts.Dispose();
            cts = null;
            BusyChanged?.Invoke(false, null);
        }
    }

    public void Cancel() => cts?.Cancel();
}
