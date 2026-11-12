namespace RecoveryBLL.Models;

/// <summary>Windows STORAGE_BUS_TYPE values. M.2 is a slot, not a bus: an M.2 drive reports Sata or Nvme.</summary>
public enum BusType
{
    Unknown = 0, Scsi = 1, Atapi = 2, Ata = 3, Ieee1394 = 4, Ssa = 5, Fibre = 6, Usb = 7, Raid = 8,
    IScsi = 9, Sas = 10, Sata = 11, Sd = 12, Mmc = 13, Virtual = 14, FileBackedVirtual = 15,
    Spaces = 16, Nvme = 17, Scm = 18, Ufs = 19,
}

/// <summary>A physical disk (\\.\PhysicalDriveN) and how it is connected.</summary>
public sealed record PhysicalDevice(
    int Number,
    string Model,
    string? SerialNumber,
    BusType Bus,
    bool? IncursSeekPenalty,   // true = spinning HDD, false = SSD/flash, null = unknown (many USB bridges)
    bool? SupportsTrim,
    bool Removable,
    long SizeBytes)
{
    public string Kind => IncursSeekPenalty switch { true => "HDD", false => "SSD", _ => "?" };
}

/// <summary>Description and tuning per bus type, loaded from dictionaries.xml.</summary>
public sealed record BusTypeInfo(BusType Bus, string Name, string Description, int ReadThreads, bool TrimWarning);

/// <summary>A drive that is plugged in right now, joined with what the catalog knows about it.</summary>
public sealed record DriveStatus(
    VolumeIdentity Volume,
    long FreeBytes,
    PhysicalDevice? Device,
    BusTypeInfo? BusInfo,
    Disk? CatalogDisk,
    int RecommendedThreads);
