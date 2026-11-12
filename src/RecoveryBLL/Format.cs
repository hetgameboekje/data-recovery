namespace RecoveryBLL;

public static class Format
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

    /// <summary>"2026-11-12T11:27:43.971Z" -> "2026-11-12 11:27" (UTC).</summary>
    public static string Stamp(string? iso) => iso is { Length: >= 16 } ? iso[..16].Replace('T', ' ') : iso ?? "never";
}
