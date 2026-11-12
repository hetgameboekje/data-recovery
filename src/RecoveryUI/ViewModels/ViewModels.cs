using System.ComponentModel;
using RecoveryBLL;
using RecoveryBLL.Models;
using RecoveryUI.Infrastructure;

namespace RecoveryUI.ViewModels;

// ViewModels: what a view shows, already formatted. Built by controllers from BLL models.

public enum Page { Home, Disks, Scan, Report, Settings }

public sealed class DriveRowViewModel
{
    [Browsable(false)] public required string Root { get; init; }
    [DisplayName("Drive")] public required string Drive { get; init; }
    [DisplayName("Name")] public required string Label { get; init; }
    [DisplayName("Size"), Numeric] public required string Size { get; init; }
    [DisplayName("Free"), Numeric] public required string Free { get; init; }
    [DisplayName("Connection")] public required string Connection { get; init; }
    [DisplayName("Type")] public required string Kind { get; init; }
    [DisplayName("In catalog")] public required string InCatalog { get; init; }
    [DisplayName("Last scan")] public required string LastScan { get; init; }
    [DisplayName("Disk #"), ExpertOnly] public required string DiskNumber { get; init; }
    [DisplayName("Model"), ExpertOnly] public required string Model { get; init; }
    [DisplayName("File system"), ExpertOnly] public required string FileSystem { get; init; }
    [DisplayName("Volume serial"), ExpertOnly] public required string Serial { get; init; }
    [DisplayName("Readers"), ExpertOnly, Numeric] public required string Readers { get; init; }
    [DisplayName("Notes"), ExpertOnly] public required string Notes { get; init; }
    [Browsable(false)] public string ChoiceText => $"{Drive}  {Label} — {Size}, {Connection} {Kind}" + (InCatalog == "yes" ? "  (in catalog)" : "");

    public static DriveRowViewModel From(DriveStatus s) => new()
    {
        Root = s.Volume.Root,
        Drive = s.Volume.Root.TrimEnd('\\'),
        Label = s.Volume.Label.Length > 0 ? s.Volume.Label : "(no name)",
        Size = Format.Bytes(s.Volume.TotalBytes),
        Free = Format.Bytes(s.FreeBytes),
        Connection = s.BusInfo?.Name ?? s.Device?.Bus.ToString() ?? "?",
        Kind = s.Device?.Kind switch { "HDD" => "HDD", "SSD" => "SSD", _ => "unknown" },
        InCatalog = s.CatalogDisk != null ? "yes" : "no",
        LastScan = s.CatalogDisk != null ? Format.Stamp(s.CatalogDisk.LastScan) : "",
        DiskNumber = s.Device?.Number.ToString() ?? "-",
        Model = s.Device?.Model ?? "",
        FileSystem = s.Volume.FileSystem,
        Serial = s.Volume.Serial,
        Readers = s.RecommendedThreads.ToString(),
        Notes = s.BusInfo?.TrimWarning == true && s.Device?.IncursSeekPenalty == false
            ? "SSD with TRIM: deleted files are usually wiped by the drive. " + s.BusInfo.Description
            : s.BusInfo?.Description ?? "",
    };
}

public sealed class CatalogDiskRowViewModel
{
    [DisplayName("Disk")] public required string Name { get; init; }
    [DisplayName("Now on")] public required string NowOn { get; init; }
    [DisplayName("Files"), Numeric] public required string Files { get; init; }
    [DisplayName("Size"), Numeric] public required string Size { get; init; }
    [DisplayName("Last complete scan")] public required string LastScan { get; init; }
    [DisplayName("File system"), ExpertOnly] public required string FileSystem { get; init; }
    [DisplayName("First seen"), ExpertOnly] public required string FirstSeen { get; init; }
}

public sealed class DeviceRowViewModel
{
    [DisplayName("Disk #"), Numeric] public required string Number { get; init; }
    [DisplayName("Model")] public required string Model { get; init; }
    [DisplayName("Size"), Numeric] public required string Size { get; init; }
    [DisplayName("Connection")] public required string Connection { get; init; }
    [DisplayName("Type")] public required string Kind { get; init; }
    [DisplayName("TRIM")] public required string Trim { get; init; }
    [DisplayName("Removable")] public required string Removable { get; init; }
}

public sealed class HomeSummaryViewModel
{
    public required string Disks { get; init; }
    public required string Files { get; init; }
    public required string Unique { get; init; }
    public required string Extra { get; init; }
    public required string Pending { get; init; }
    public required string NextStep { get; init; }
}

public sealed class ScanOptionsViewModel
{
    public int ReadersPerDisk { get; init; }   // 0 = auto
    public bool Sha256 { get; init; }
    public bool ReadImageInfo { get; init; } = true;
    public bool ListOnly { get; init; }
    public IReadOnlyList<string> Excludes { get; init; } = [];
}

public sealed record ProgressViewModel(string Stage, string Detail, int Percent, bool Indeterminate);

public sealed class TypeRowViewModel
{
    [DisplayName("Type")] public required string Type { get; init; }
    [DisplayName("Files"), Numeric] public required string Files { get; init; }
    [DisplayName("Total"), Numeric] public required string Total { get; init; }
    [DisplayName("Unique data"), Numeric] public required string Unique { get; init; }
    [DisplayName("Extra copies"), Numeric] public required string Extra { get; init; }
    [DisplayName("Not yet decided"), Numeric] public required string Pending { get; init; }
}

public sealed class DiskSummaryRowViewModel
{
    [DisplayName("Disk")] public required string Disk { get; init; }
    [DisplayName("Now on")] public required string NowOn { get; init; }
    [DisplayName("Files"), Numeric] public required string Files { get; init; }
    [DisplayName("Total"), Numeric] public required string Total { get; init; }
    [DisplayName("Only copy"), Numeric] public required string OnlyCopy { get; init; }
    [DisplayName("Duplicated"), Numeric] public required string Duplicated { get; init; }
    [DisplayName("...also on another disk"), Numeric, ExpertOnly] public required string OnOtherDisk { get; init; }
    [DisplayName("Not yet decided"), Numeric] public required string Pending { get; init; }
    [DisplayName("Empty files"), Numeric, ExpertOnly] public required string Empty { get; init; }
    [DisplayName("Read errors"), Numeric, ExpertOnly] public required string Errors { get; init; }
    [DisplayName("Last scan"), ExpertOnly] public required string LastScan { get; init; }
}

public sealed class ReportViewModel
{
    public required string Total { get; init; }
    public required string TotalCaption { get; init; }
    public required string Unique { get; init; }
    public required string Extra { get; init; }
    public required string Pending { get; init; }
    public required string Needed { get; init; }
    public required string PendingHint { get; init; }
    public bool CanHashPending { get; init; }
    public required IReadOnlyList<TypeRowViewModel> Types { get; init; }
    public required IReadOnlyList<DiskSummaryRowViewModel> Disks { get; init; }
}

public sealed record DuplicateGroupViewModel(string Title, IReadOnlyList<string> Copies);

public sealed class SettingsViewModel
{
    public required string Provider { get; init; }
    public required string Connection { get; init; }
    public required string EnvFile { get; init; }
    public required string Dictionaries { get; init; }
    public required string DictionarySummary { get; init; }
    public required string LogFolder { get; init; }
    public required string Network { get; init; }
}
