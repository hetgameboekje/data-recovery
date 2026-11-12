using System.Diagnostics;

namespace RecoveryCatalog;

static class Log
{
    static readonly object Gate = new();
    public static string? FilePath { get; set; }

    public static void Info(string msg) => Write(msg, false);
    public static void Warn(string msg) => Write("WARN  " + msg, true);

    static void Write(string msg, bool warn)
    {
        lock (Gate)
        {
            Progress.ClearLine();
            if (warn) Console.Error.WriteLine(msg); else Console.WriteLine(msg);
            if (FilePath != null)
            {
                try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}"); } catch { }
            }
        }
    }
}

/// <summary>A single self-overwriting status line, refreshed at most twice a second.</summary>
sealed class Progress(string stage, long total = 0, long totalBytes = 0)
{
    static readonly object Gate = new();
    static int lastLen;
    readonly Stopwatch sw = Stopwatch.StartNew();
    long lastDraw = -1000;
    long done, bytes;

    public static void ClearLine()
    {
        if (lastLen == 0 || Console.IsOutputRedirected) return;
        Console.Write("\r" + new string(' ', lastLen) + "\r");
        lastLen = 0;
    }

    public void Add(long bytesRead)
    {
        Interlocked.Increment(ref done);
        Interlocked.Add(ref bytes, bytesRead);
        Draw(false);
    }

    public void Set(long count, long byteCount)
    {
        done = count;
        bytes = byteCount;
        Draw(false);
    }

    public void Done(long? count = null, long? byteCount = null)
    {
        if (count.HasValue) done = count.Value;
        if (byteCount.HasValue) bytes = byteCount.Value;
        Draw(true);
        lock (Gate)
        {
            if (!Console.IsOutputRedirected) Console.WriteLine();
            lastLen = 0;
        }
    }

    void Draw(bool force)
    {
        var ms = sw.ElapsedMilliseconds;
        if (!force && ms - Interlocked.Read(ref lastDraw) < 500) return;
        if (Console.IsOutputRedirected && !force) return;
        lock (Gate)
        {
            lastDraw = ms;
            var d = Interlocked.Read(ref done);
            var b = Interlocked.Read(ref bytes);
            var secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var count = total > 0 ? $"{d:N0}/{total:N0}" : $"{d:N0}";
            var size = totalBytes > 0 ? $"{Fmt.Bytes(b)}/{Fmt.Bytes(totalBytes)}" : Fmt.Bytes(b);
            var rate = stage == "Scan" ? $"{d / secs:N0} files/s" : $"{Fmt.Bytes((long)(b / secs))}/s";
            var line = $"  {stage}: {count} files, {size}, {rate}, {sw.Elapsed:hh\\:mm\\:ss}";
            if (Console.IsOutputRedirected) { Console.WriteLine(line); return; }
            var pad = Math.Max(0, lastLen - line.Length);
            Console.Write("\r" + line + new string(' ', pad));
            lastLen = line.Length;
        }
    }
}

static class Fmt
{
    public static string Bytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB", "PB"];
        double v = b;
        int i = 0;
        while (Math.Abs(v) >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.##} {u[i]}";
    }

    public static string Hex(byte[]? h) => h == null ? "" : Convert.ToHexString(h).ToLowerInvariant();
}
