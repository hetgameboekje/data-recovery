using System.Runtime.InteropServices;
using System.Text;
using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Storage;

/// <summary>
/// The Windows folders the toolkit keeps away from. Built once from the real system (not hard-coded "C:"),
/// so it also works when Windows lives on another drive or a user profile is elsewhere.
/// </summary>
public sealed class WindowsProtectedLocations : IProtectedLocations
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

    readonly Lazy<IReadOnlyList<string>> noGo;

    public WindowsProtectedLocations()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        SystemDrive = Path.GetPathRoot(string.IsNullOrEmpty(windows) ? Environment.SystemDirectory : windows) ?? @"C:\";
        noGo = new Lazy<IReadOnlyList<string>>(Build);
    }

    public string SystemDrive { get; }
    public IReadOnlyList<string> NoGoFolders => noGo.Value;

    IReadOnlyList<string> Build()
    {
        var list = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),    // ProgramData
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),          // this user's AppData (also if redirected)
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        foreach (var name in new[] { "Recovery", "System Volume Information", "$Recycle.Bin", "$WinREAgent", "$Windows.~BT", "$Windows.~WS",
                                     "$SysReset", "Config.Msi", "Boot" })
            list.Add(Path.Combine(SystemDrive, name));

        // Every profile's AppData, plus the template/system profiles. The Users folder itself is then refused
        // automatically, because it contains these.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var users = string.IsNullOrEmpty(profile) ? Path.Combine(SystemDrive, "Users") : Path.GetDirectoryName(profile)!;
        list.Add(Path.Combine(users, "Default"));
        list.Add(Path.Combine(users, "Default User"));
        list.Add(Path.Combine(users, "All Users"));
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(users))
                list.Add(Path.Combine(dir, "AppData"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (!string.IsNullOrEmpty(profile)) list.Add(Path.Combine(profile, "AppData"));

        var all = list.Where(p => !string.IsNullOrWhiteSpace(p))
                      .Select(p => Normalize(p!))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        // Keep the outermost folders only (AppData, not also AppData\Roaming): shorter list, clearer messages.
        return all.Where(p => !all.Any(o => p.Length > o.Length && p.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && full.Length > 6 && full[5] == ':') full = full[4..];

        // Expand 8.3 names (C:\PROGRA~1) for the part of the path that exists.
        var existing = full;
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent == null) { existing = null!; break; }
            existing = parent;
        }
        if (existing != null)
        {
            var sb = new StringBuilder(1024);
            var n = GetLongPathNameW(existing, sb, (uint)sb.Capacity);
            if (n > 0 && n < sb.Capacity) full = sb.ToString() + full[existing.Length..];
        }

        // Resolve junctions/symlinks on the way down (e.g. "C:\Documents and Settings" -> C:\Users).
        var root = Path.GetPathRoot(full) ?? "";
        var parts = full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            try
            {
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget != null && info.ResolveLinkTarget(true) is { } target)
                    current = target.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var result = Path.GetFullPath(current);
        return result.Length > 3 ? result.TrimEnd('\\') : result;
    }
}
