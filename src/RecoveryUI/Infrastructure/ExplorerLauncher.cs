using System.Diagnostics;
using RecoveryBLL.Services;
using RecoveryUI.Views;

namespace RecoveryUI.Infrastructure;

/// <summary>
/// "Show in Explorer" for a catalogued file or folder (read-only). An unplugged disk or a file that moved since it was
/// catalogued gets a short explanation instead.
/// </summary>
public static class ExplorerLauncher
{
    public static void Show(IMainView main, DiskService disks, long diskId, string relativePath, bool isFile)
    {
        var mounted = disks.GetMountedRoots();
        if (!mounted.TryGetValue(diskId, out var root))
        {
            var disk = disks.GetCatalogued().FirstOrDefault(d => d.Id == diskId);
            main.ShowInfo("Not plugged in", $"Plug in {disk?.DisplayName ?? "this disk"} to open it in Explorer. The catalog itself works without the disk.");
            return;
        }
        var path = relativePath.Length == 0 ? root : Path.Combine(root, relativePath);
        if (isFile ? !File.Exists(path) : !Directory.Exists(path))
        {
            main.ShowInfo("Not found", $"{path}\n\nis no longer there: it was moved or deleted after the disk was catalogued. Catalog the disk again to update it.");
            return;
        }
        Open(main, isFile ? $"/select,\"{path}\"" : $"\"{path}\"");
    }

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void Select(IMainView main, string file) => Open(main, $"/select,\"{file}\"");

    static void Open(IMainView main, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            main.ShowError("Show in Explorer", ex.Message);
        }
    }
}
