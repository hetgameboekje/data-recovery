using System.Globalization;
using System.Text;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Storage;

/// <summary>
/// The undo record of every Clean up run: one CSV per run in &lt;log folder&gt;\cleanup (from;to;size), readable in Excel.
/// A run that was undone gets ".undone" added to its name, so it is kept but not offered again.
/// </summary>
public sealed class CleanupLogFile(string logDirectory) : ICleanupLogStore
{
    readonly string folder = Path.Combine(logDirectory, "cleanup");

    public string Save(IReadOnlyList<CleanupLogEntry> entries)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"cleanup-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var sb = new StringBuilder("from;to;size\r\n");
        foreach (var e in entries) sb.Append(Quote(e.From)).Append(';').Append(Quote(e.To)).Append(';').Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return file;
    }

    public (string File, IReadOnlyList<CleanupLogEntry> Entries)? Latest()
    {
        if (!Directory.Exists(folder)) return null;
        var file = Directory.EnumerateFiles(folder, "cleanup-*.csv").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (file == null) return null;
        var entries = new List<CleanupLogEntry>();
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8).Skip(1))
        {
            var parts = Split(line);
            if (parts.Count == 3 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                entries.Add(new CleanupLogEntry(parts[0], parts[1], size));
        }
        return (file, entries);
    }

    public void MarkUndone(string file) => File.Move(file, file + ".undone", overwrite: false);

    static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    static List<string> Split(string line)
    {
        var parts = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cur.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ';') { parts.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        parts.Add(cur.ToString());
        return parts;
    }
}
