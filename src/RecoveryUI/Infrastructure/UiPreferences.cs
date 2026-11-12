namespace RecoveryUI.Infrastructure;

/// <summary>
/// Small per-user on/off switches (e.g. "explanation hidden"), one file each under %LOCALAPPDATA%\RecoveryToolkit.
/// A convenience only: if the folder cannot be written, the default is used.
/// </summary>
public static class UiPreferences
{
    static string FileFor(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecoveryToolkit", "ui-" + name);

    public static bool Get(string name)
    {
        try { return File.Exists(FileFor(name)) && File.ReadAllText(FileFor(name)).Trim() == "1"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void Set(string name, bool on)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FileFor(name))!);
            File.WriteAllText(FileFor(name), on ? "1" : "0");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
