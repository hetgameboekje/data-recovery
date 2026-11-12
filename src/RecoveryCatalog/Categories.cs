namespace RecoveryCatalog;

/// <summary>Same extension lists as RecoveryToolkit.ps1, so both tools sort files the same way.</summary>
static class Categories
{
    static readonly Dictionary<string, string> ByExt = Build(new()
    {
        ["Photos"] = "jpg jpeg jpe jfif png gif bmp tif tiff heic heif webp avif svg ico psd raw cr2 cr3 crw nef nrw arw srf sr2 orf raf rw2 dng pef srw x3f",
        ["Videos"] = "mp4 m4v mov 3gp 3g2 avi mkv mpg mpeg mts m2ts ts wmv asf flv webm vob ogv divx",
        ["Audio"] = "mp3 wav flac ogg oga opus m4a aac wma aiff aif amr mid midi ape",
        ["Documents"] = "pdf doc docx docm xls xlsx xlsm ppt pptx odt ods odp rtf txt csv md epub pages numbers key",
        ["Archives"] = "zip rar 7z tar gz tgz bz2 xz iso",
    });

    /// <summary>Image formats whose dimensions <see cref="MediaProbe"/> can read from the header.</summary>
    public static readonly string[] ProbeableImages = ["jpg", "jpeg", "jpe", "jfif", "png", "gif", "bmp", "webp"];

    static Dictionary<string, string> Build(Dictionary<string, string> src)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cat, exts) in src)
            foreach (var e in exts.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                d[e] = cat;
        return d;
    }

    public static string Ext(string fileName)
    {
        var e = Path.GetExtension(fileName);
        return e.Length > 1 ? e[1..].ToLowerInvariant() : "";
    }

    public static string Of(string ext) => ByExt.TryGetValue(ext, out var c) ? c : "Other";
}
