namespace RecoveryBLL.Models;

/// <summary>
/// All lookup lists the services use (file categories, folders to skip, bus types, junk rules).
/// They live in config/dictionaries.xml so they can be changed without rebuilding.
/// </summary>
public sealed class RecoveryDictionaries
{
    public IReadOnlyDictionary<string, string> CategoryByExtension { get; init; } = new Dictionary<string, string>();
    public IReadOnlySet<string> SkipFolders { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> SkipRootFiles { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> PartialFileExtensions { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> ProbeableImageExtensions { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<BusType, BusTypeInfo> BusTypes { get; init; } = new Dictionary<BusType, BusTypeInfo>();
    public JunkRules Junk { get; init; } = new();
    public string DefaultCategory { get; init; } = "Other";

    public string CategoryOf(string ext) => CategoryByExtension.TryGetValue(ext, out var c) ? c : DefaultCategory;
}

/// <summary>Rules for phase 3/3b (junk and preview detection). Loaded now so they can be edited ahead of time.</summary>
public sealed class JunkRules
{
    public IReadOnlyList<(int Width, int Height)> SkinSizes { get; init; } = [];
    public IReadOnlySet<int> IconSizes { get; init; } = new HashSet<int>();
    public IReadOnlyList<(int Width, int Height)> ThumbnailSizes { get; init; } = [];
    public IReadOnlyList<string> GameAssetPathKeywords { get; init; } = [];
    public int PreviewMaxLongSide { get; init; } = 640;
}
