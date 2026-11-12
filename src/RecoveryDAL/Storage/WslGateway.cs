using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Storage;

/// <summary>
/// Talks to wsl.exe. Linux partitions are attached with  wsl --mount \\.\PHYSICALDRIVEn --partition p --type ext4 --options ro
/// and appear under /mnt/wsl/PHYSICALDRIVEnpP, which Windows reaches as \\wsl.localhost\&lt;distro&gt;\mnt\wsl\....
/// </summary>
public sealed class WslGateway : IWslGateway
{
    static readonly string WslExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
    readonly TimeSpan timeout = TimeSpan.FromMinutes(2);

    public bool IsInstalled()
    {
        if (!File.Exists(WslExe)) return false;
        try { return Run(["--list", "--quiet"], out _) == 0; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception) { return false; }
    }

    public IReadOnlyList<string> GetDistributions()
    {
        if (Run(["--list", "--quiet"], out var output) != 0) return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(s => s.Length > 0).ToList();
    }

    public string MountReadOnly(int diskNumber, int partition, string fileSystem)
    {
        var code = Run(["--mount", $@"\\.\PHYSICALDRIVE{diskNumber}", "--partition", partition.ToString(),
                        "--type", fileSystem, "--options", "ro"], out var output);
        if (code != 0) throw new IOException(output.Trim());
        var distro = UsableDistro() ?? throw new InvalidOperationException("No Linux distribution installed (docker-desktop cannot be used).");
        return $@"\\wsl.localhost\{distro}\mnt\wsl\PHYSICALDRIVE{diskNumber}p{partition}\";
    }

    public void Unmount(int diskNumber)
    {
        if (Run(["--unmount", $@"\\.\PHYSICALDRIVE{diskNumber}"], out var output) != 0) throw new IOException(output.Trim());
    }

    public VolumeIdentity? GetMountIdentity(string windowsPath, int diskNumber, int partition, string fileSystem)
    {
        var distro = UsableDistro();
        if (distro == null) return null;
        var linuxPath = $"/mnt/wsl/PHYSICALDRIVE{diskNumber}p{partition}";
        if (Run(["-d", distro, "-e", "findmnt", "-n", "-b", "-o", "UUID,SIZE,LABEL", linuxPath], out var output) != 0) return null;
        var parts = output.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], out var size)) return null;
        return new VolumeIdentity(parts[0], size, parts.Length > 2 ? parts[2] : "", fileSystem, windowsPath);
    }

    /// <summary>The first installed distribution that is a real Linux (docker-desktop has no findmnt/wslpath).</summary>
    string? UsableDistro() => GetDistributions().FirstOrDefault(d => !d.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase));

    int Run(string[] args, out string output)
    {
        var psi = new ProcessStartInfo(WslExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["WSL_UTF8"] = "1";   // otherwise wsl.exe writes UTF-16
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start wsl.exe");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new IOException("wsl.exe did not respond within " + timeout.TotalSeconds + " s");
        }
        output = (stdout.Result + stderr.Result).Replace("\0", "");
        return p.ExitCode;
    }
}
