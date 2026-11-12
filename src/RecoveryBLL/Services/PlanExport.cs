using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>The copy plan as a CSV file (";"-separated, UTF-8 with BOM so Excel opens it correctly). Used by the CLI and the app.</summary>
public static class PlanExport
{
    public static void WriteCsv(CopyPlan plan, IReadOnlyDictionary<long, string> diskNames, TextWriter w)
    {
        static string Q(string? v) => v == null ? "" : "\"" + v.Replace("\"", "\"\"") + "\"";
        w.WriteLine("action;source_disk;source_path;size;modified_utc;category;destination_disk;destination_path;note");
        foreach (var i in plan.Items.OrderBy(i => i.Action).ThenBy(i => i.SourceDiskId).ThenBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase))
            w.WriteLine(string.Join(';',
                i.Action.ToString(), Q(diskNames.GetValueOrDefault(i.SourceDiskId)), Q(i.SourcePath), i.Size.ToString(),
                new DateTime(i.MtimeTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss"), Q(i.Category),
                Q(i.DestDiskId is { } d ? diskNames.GetValueOrDefault(d) : null), Q(i.DestPath), Q(i.Note)));
        foreach (var f in plan.Deletions.Files)
            w.WriteLine(string.Join(';', "DeleteIfApproved", "", Q(f.Path), f.Size.ToString(), "", "", "", "",
                Q($"{f.Reason} (approval code {plan.Deletions.Code})")));
    }

    public static void WriteCsv(CopyPlan plan, IReadOnlyDictionary<long, string> diskNames, string path)
    {
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        WriteCsv(plan, diskNames, w);
    }
}
