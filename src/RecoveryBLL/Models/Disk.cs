using RecoveryBLL.Dtos;

namespace RecoveryBLL.Models;

/// <summary>A catalogued volume. Identified by volume serial + size, never by drive letter.</summary>
public sealed class Disk
{
    public long Id { get; }
    public string Serial { get; }
    public long TotalBytes { get; }
    public string Label { get; }
    public string FileSystem { get; }
    public string? LastRoot { get; }
    public string FirstSeen { get; }
    public string? LastScan { get; }
    public long FileCount { get; }

    public Disk(DiskDto dto)
    {
        Id = dto.Id;
        Serial = dto.Serial;
        TotalBytes = dto.TotalBytes;
        Label = dto.Label ?? "";
        FileSystem = dto.FileSystem ?? "";
        LastRoot = dto.LastRoot;
        FirstSeen = dto.FirstSeen;
        LastScan = dto.LastScan;
        FileCount = dto.FileCount;
    }

    public string DisplayName => $"{(Label.Length == 0 ? "(no label)" : Label)} [{Serial}]";
}

/// <summary>What Windows reports about a mounted volume.</summary>
public sealed record VolumeIdentity(string Serial, long TotalBytes, string Label, string FileSystem, string Root);
