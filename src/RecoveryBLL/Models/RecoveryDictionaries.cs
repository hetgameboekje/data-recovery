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
    /// <summary>Phase 3: folders that are copied whole (a Minecraft server, a database, a program...), never split by type.</summary>
    public IReadOnlyList<BundleRule> KeepTogether { get; init; } = [];
    public string DefaultCategory { get; init; } = "Other";
    /// <summary>Categories in file order, the default category last.</summary>
    public IReadOnlyList<string> CategoryOrder { get; init; } = [];
    /// <summary>Optional display colour per category ("#RRGGBB", the <c>color</c> attribute); used by the disk map.</summary>
    public IReadOnlyDictionary<string, string> CategoryColors { get; init; } = new Dictionary<string, string>();

    public string CategoryOf(string ext) => CategoryByExtension.TryGetValue(ext, out var c) ? c : DefaultCategory;
}

/// <summary>Rules for phase 3/3b (junk and preview detection). Loaded now so they can be edited ahead of time.</summary>
public sealed class JunkRules
{
    public IReadOnlyList<(int Width, int Height)> SkinSizes { get; init; } = [];
    public IReadOnlySet<int> IconSizes { get; init; } = new HashSet<int>();
    public IReadOnlyList<(int Width, int Height)> ThumbnailSizes { get; init; } = [];
    public int PreviewMaxLongSide { get; init; } = 640;
    /// <summary>A file of this category smaller than this can't be a real picture/video/song: a recovery scrap.</summary>
    public IReadOnlyDictionary<string, long> ScrapBelow { get; init; } = new Dictionary<string, long>();
    /// <summary>File names that are always junk (Thumbs.db, desktop.ini, .DS_Store...).</summary>
    public IReadOnlySet<string> JunkFiles { get; init; } = new HashSet<string>();
    /// <summary>Folder names whose files are junk (thumbnail and cache folders).</summary>
    public IReadOnlySet<string> JunkFolders { get; init; } = new HashSet<string>();
    /// <summary>A folder with at least this many small images of one size (no camera data) is an image set (game assets, thumbnails).</summary>
    public int AssetFolderMinImages { get; init; } = 200;
    public int AssetFolderMaxSide { get; init; } = 256;
}

/// <summary>What happens to a keep-together folder: copied with your files, or put aside for review.</summary>
public enum BundleAction { Keep, Review }

/// <summary>
/// A keep-together folder. <see cref="Marker"/>: a file or folder name inside it (the folder holding it is the bundle);
/// <see cref="Folder"/>: the folder's own name; <see cref="Program"/>: a folder with an .exe and at least 3 .dll files.
/// </summary>
public sealed record BundleRule(string Name, string? Marker, string? Folder, bool Program, BundleAction Action);
