using System.Diagnostics;
using RecoveryBLL;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryCli;

/// <summary>IOperationLog for the console: prints, and appends to a daily log file.</summary>
sealed class ConsoleLog(string logDirectory) : IOperationLog
{
    static readonly object Gate = new();
    readonly string file = Path.Combine(logDirectory, $"rtcatalog-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write(message, false);
    public void Warn(string message) => Write("WARN  " + message, true);

    void Write(string message, bool warn)
    {
        lock (Gate)
        {
            ConsoleProgress.ClearLine();
            if (warn) Console.Error.WriteLine(message); else Console.WriteLine(message);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch (IOException) { /* logging must never stop a scan */ }
        }
    }
}

/// <summary>A single self-overwriting status line.</summary>
sealed class ConsoleProgress : IProgress<ProgressInfo>
{
    static readonly object Gate = new();
    static int lastLen;
    string? lastStage;

    public static void ClearLine()
    {
        if (lastLen == 0 || Console.IsOutputRedirected) return;
        Console.Write("\r" + new string(' ', lastLen) + "\r");
        lastLen = 0;
    }

    public void Report(ProgressInfo p)
    {
        lock (Gate)
        {
            bool final = p.Total > 0 && p.Done >= p.Total;
            if (Console.IsOutputRedirected && !final) return;
            var count = p.Total > 0 ? $"{p.Done:N0}/{p.Total:N0}" : $"{p.Done:N0}";
            var size = p.TotalBytes > 0 ? $"{Format.Bytes(p.Bytes)}/{Format.Bytes(p.TotalBytes)}" : Format.Bytes(p.Bytes);
            var rate = p.Stage == "Scan"
                ? $"{p.Done / Math.Max(p.Elapsed.TotalSeconds, 0.001):N0} files/s"
                : $"{Format.Bytes((long)p.BytesPerSecond)}/s";
            var line = $"  {p.Stage}: {count} files, {size}, {rate}, {p.Elapsed:hh\\:mm\\:ss}";
            if (Console.IsOutputRedirected) { Console.WriteLine(line); return; }
            if (lastStage != null && lastStage != p.Stage) { Console.WriteLine(); lastLen = 0; }
            lastStage = p.Stage;
            Console.Write("\r" + line + new string(' ', Math.Max(0, lastLen - line.Length)));
            lastLen = line.Length;
            if (final) { Console.WriteLine(); lastLen = 0; lastStage = null; }
        }
    }
}

static class TablePrinter
{
    public static void Print(string[] head, IReadOnlyList<string[]> rows)
    {
        var w = head.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        string Line(string[] cells) => "  " + string.Join("  ", cells.Select((s, i) => i == 0 ? s.PadRight(w[i]) : s.PadLeft(w[i])));
        Console.WriteLine(Line(head));
        Console.WriteLine("  " + string.Join("  ", w.Select(n => new string('-', n))));
        foreach (var r in rows) Console.WriteLine(Line(r));
    }
}

/// <summary>Step 2 of every deletion in the console: the list once more, then the person has to type DELETE.</summary>
sealed class ConsoleDeletionConfirmer : IDeletionConfirmer
{
    public bool ConfirmFinal(DeletionBatch batch)
    {
        ConsoleProgress.ClearLine();
        Console.WriteLine();
        PrintList(batch);
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("  Not deleted: the final confirmation needs a person at the keyboard.");
            return false;
        }
        Console.Write($"  Step 2 of 2: type DELETE to permanently delete these {batch.Count:N0} files ({Format.Bytes(batch.Bytes)}); anything else keeps them: ");
        var answer = Console.ReadLine();
        var ok = string.Equals(answer?.Trim(), "DELETE", StringComparison.Ordinal);
        Console.WriteLine(ok ? "  Confirmed." : "  Not confirmed: nothing is deleted.");
        return ok;
    }

    public static void PrintList(DeletionBatch batch, int max = 20)
    {
        Console.WriteLine($"  To delete: {batch.Count:N0} files, {Format.Bytes(batch.Bytes)}  (approval code {batch.Code})");
        foreach (var f in batch.Files.Take(max))
            Console.WriteLine($"    {Format.Bytes(f.Size),10}  {f.Path}");
        if (batch.Count > max) Console.WriteLine($"    ... and {batch.Count - max:N0} more (plan --csv lists them all)");
        var reasons = batch.Files.Select(f => f.Reason.Split(';')[0]).Distinct().ToList();
        if (reasons.Count == 1) Console.WriteLine($"  Why: {batch.Files[0].Reason.Split(';')[0]}");
    }
}
