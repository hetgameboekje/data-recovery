using System.Xml;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Xml;

/// <summary>
/// Reads config/dictionaries.xml with the XML DOM (<see cref="XmlDocument"/> + XPath).
/// DTDs and external entities are disabled, so the file cannot pull in anything from outside.
/// </summary>
public sealed class XmlDictionaryRepository(string path) : IDictionaryRepository
{
    public string Location { get; } = Path.GetFullPath(path);

    public RecoveryDictionaries Load()
    {
        var doc = LoadDocument(Location);
        var root = doc.DocumentElement ?? throw new ConfigurationException("Empty XML document");
        if (root.Name != "dictionaries") throw new ConfigurationException("Root element must be <dictionaries>");

        XmlElement Dict(string name) =>
            root.SelectSingleNode($"dictionary[@name='{name}']") as XmlElement
            ?? throw new ConfigurationException($"Missing <dictionary name=\"{name}\">");

        // categories: <category name="Photos"><extension>jpg</extension>...</category>
        var categories = Dict("categories");
        var byExt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XmlElement cat in categories.SelectNodes("category")!)
        {
            var name = Required(cat, "name");
            foreach (XmlElement e in cat.SelectNodes("extension")!)
            {
                var ext = e.InnerText.Trim().TrimStart('.').ToLowerInvariant();
                if (ext.Length == 0) continue;
                if (byExt.TryGetValue(ext, out var other) && other != name)
                    throw new ConfigurationException($"Extension '{ext}' is in both '{other}' and '{name}'");
                byExt[ext] = name;
            }
        }

        // bus types: <bus code="17" name="NVMe" readThreads="8" trimWarning="true">description</bus>
        var buses = new Dictionary<BusType, BusTypeInfo>();
        foreach (XmlElement b in Dict("busTypes").SelectNodes("bus")!)
        {
            if (!int.TryParse(Required(b, "code"), out var code) || !Enum.IsDefined(typeof(BusType), code))
                throw new ConfigurationException($"Unknown bus code '{b.GetAttribute("code")}'");
            var bus = (BusType)code;
            buses[bus] = new BusTypeInfo(bus, Required(b, "name"), Normalize(b.InnerText),
                int.TryParse(b.GetAttribute("readThreads"), out var t) ? t : 1,
                bool.TryParse(b.GetAttribute("trimWarning"), out var w) && w);
        }

        var junk = Dict("junk");
        return new RecoveryDictionaries
        {
            DefaultCategory = categories.GetAttribute("default") is { Length: > 0 } def ? def : "Other",
            CategoryByExtension = byExt,
            SkipFolders = Items("skipFolders", StringComparer.OrdinalIgnoreCase),
            SkipRootFiles = Items("skipRootFiles", StringComparer.OrdinalIgnoreCase),
            PartialFileExtensions = Items("partialFileExtensions", StringComparer.OrdinalIgnoreCase, lower: true),
            ProbeableImageExtensions = Items("probeableImages", StringComparer.OrdinalIgnoreCase, lower: true),
            BusTypes = buses,
            Junk = new JunkRules
            {
                PreviewMaxLongSide = int.TryParse(junk.GetAttribute("previewMaxLongSide"), out var p) ? p : 640,
                SkinSizes = Sizes(junk, "skinSize"),
                ThumbnailSizes = Sizes(junk, "thumbnailSize"),
                IconSizes = junk.SelectNodes("iconSize")!.Cast<XmlElement>()
                    .Select(e => int.TryParse(e.InnerText, out var n) ? n : 0).Where(n => n > 0).ToHashSet(),
                GameAssetPathKeywords = junk.SelectNodes("gameAssetPathKeyword")!.Cast<XmlElement>()
                    .Select(e => e.InnerText.Trim()).Where(s => s.Length > 0).ToList(),
            },
        };

        HashSet<string> Items(string dict, StringComparer cmp, bool lower = false) =>
            Dict(dict).SelectNodes("item")!.Cast<XmlElement>()
                .Select(e => lower ? e.InnerText.Trim().TrimStart('.').ToLowerInvariant() : e.InnerText.Trim())
                .Where(s => s.Length > 0).ToHashSet(cmp);
    }

    static XmlDocument LoadDocument(string file)
    {
        if (!File.Exists(file)) throw new ConfigurationException($"File not found: {file}");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(file, settings);
            doc.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ConfigurationException($"Invalid XML at line {ex.LineNumber}: {ex.Message}", ex);
        }
        return doc;
    }

    static string Required(XmlElement e, string attribute) =>
        e.GetAttribute(attribute) is { Length: > 0 } v ? v
            : throw new ConfigurationException($"<{e.Name}> needs a '{attribute}' attribute");

    static List<(int, int)> Sizes(XmlElement parent, string element) =>
        parent.SelectNodes(element)!.Cast<XmlElement>()
            .Select(e => (int.TryParse(e.GetAttribute("width"), out var w) ? w : 0, int.TryParse(e.GetAttribute("height"), out var h) ? h : 0))
            .Where(s => s.Item1 > 0 && s.Item2 > 0).ToList();

    static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
