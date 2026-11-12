using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Validation;

/// <summary>
/// Keeps the toolkit away from places where scanning or writing can harm or stall Windows:
///   - the Windows drive itself (C:\): only a subfolder may be chosen, e.g. C:\Recovery;
///   - Windows, Program Files, ProgramData, every user's AppData, recovery/system folders: never, nor anything inside;
///   - a folder that CONTAINS one of those (C:\Users, C:\Users\&lt;name&gt;): never, choose a subfolder such as Pictures.
/// Other drives (D:\, H:\, ...) may be used whole. Network paths are refused (not supported yet).
/// Paths are normalised first (8.3 names, junctions such as "C:\Documents and Settings", "..").
/// </summary>
public static class PathSafetyValidator
{
    public static string Validate(string path, PathUse use, IProtectedLocations locations)
    {
        var what = use == PathUse.Destination ? "a destination" : "scanned";
        if (string.IsNullOrWhiteSpace(path))
            throw new RecoveryServiceException(use == PathUse.Destination ? "Choose a destination folder or drive." : "Choose a folder or drive to scan.");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new RecoveryServiceException($"Network locations cannot be {what} yet. Choose a local drive or folder.");

        if (path.Length == 2 && path[1] == ':') path += "\\";   // "C:" means the drive, not C:'s current folder
        string full;
        try { full = locations.Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RecoveryServiceException($"Not a valid path: {path} ({ex.Message})", ex);
        }

        var system = locations.SystemDrive.TrimEnd('\\');
        if (Same(full, system))
            throw new RecoveryServiceException(
                $"{system}\\ is the Windows drive and cannot be {what} as a whole. Choose a subfolder, e.g. {system}\\Recovery" +
                (use == PathUse.ScanSource ? @" or C:\Users\<name>\Pictures." : "."));

        var nogos = locations.NoGoFolders.Select(n => n.TrimEnd('\\')).ToList();
        foreach (var n in nogos)
            if (Same(full, n) || Inside(full, n))
                throw new RecoveryServiceException($"{full} is a Windows, program or AppData folder ({n}) and cannot be {what}.");
        foreach (var n in nogos)
        {
            if (Inside(n, full))
                throw new RecoveryServiceException(
                    $"{full} contains a Windows, program or AppData folder ({n}) and cannot be {what} as a whole. Choose a subfolder (for example Pictures or Documents).");
        }
        return full;
    }

    static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>True if <paramref name="path"/> is strictly below <paramref name="folder"/>.</summary>
    static bool Inside(string path, string folder) =>
        path.Length > folder.Length + 1 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
