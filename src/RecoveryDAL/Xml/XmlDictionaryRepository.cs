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
        var order = new List<string>();
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XmlElement cat in categories.SelectNodes("category")!)
        {
            var name = Required(cat, "name");
            order.Add(name);
            if (cat.GetAttribute("color") is { Length: > 0 } color)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                    throw new ConfigurationException($"Category '{name}': color must look like #1F77B4, not '{color}'");
                colors[name] = color;
            }
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
            CategoryOrder = DefaultLast(order, categories.GetAttribute("default") is { Length: > 0 } d2 ? d2 : "Other"),
            CategoryColors = AddDefaultColor(colors, categories),
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
                ScrapBelow = junk.SelectNodes("scrap")!.Cast<XmlElement>()
                    .ToDictionary(e => Required(e, "category"),
                                  e => long.TryParse(Required(e, "below"), out var b) && b > 0 ? b
                                       : throw new ConfigurationException($"<scrap category=\"{e.GetAttribute("category")}\">: below must be a positive number of bytes"),
                                  StringComparer.OrdinalIgnoreCase),
                JunkFiles = Texts(junk, "junkFile"),
                JunkFolders = Texts(junk, "junkFolder"),
                AssetFolderMinImages = junk.SelectSingleNode("assetFolder") is XmlElement af && int.TryParse(af.GetAttribute("minImages"), out var mi) ? mi : 200,
                AssetFolderMaxSide = junk.SelectSingleNode("assetFolder") is XmlElement af2 && int.TryParse(af2.GetAttribute("maxSide"), out var ms) ? ms : 256,
            },
            KeepTogether = root.SelectSingleNode("dictionary[@name='keepTogether']") is XmlElement kt ? Bundles(kt) : [],
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

    static IReadOnlyList<string> DefaultLast(List<string> order, string defaultCategory)
    {
        order.RemoveAll(n => string.Equals(n, defaultCategory, StringComparison.OrdinalIgnoreCase));
        order.Add(defaultCategory);
        return order;
    }

    /// <summary>The default category ("Other") has no &lt;category&gt; element, so its colour is an attribute on the dictionary.</summary>
    static IReadOnlyDictionary<string, string> AddDefaultColor(Dictionary<string, string> colors, XmlElement categories)
    {
        var name = categories.GetAttribute("default") is { Length: > 0 } d ? d : "Other";
        if (categories.GetAttribute("defaultColor") is { Length: > 0 } c)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$"))
                throw new ConfigurationException($"defaultColor must look like #1F77B4, not '{c}'");
            colors[name] = c;
        }
        return colors;
    }

    static string Required(XmlElement e, string attribute) =>
        e.GetAttribute(attribute) is { Length: > 0 } v ? v
            : throw new ConfigurationException($"<{e.Name}> needs a '{attribute}' attribute");

    static HashSet<string> Texts(XmlElement parent, string element) =>
        parent.SelectNodes(element)!.Cast<XmlElement>().Select(e => e.InnerText.Trim()).Where(s => s.Length > 0)
              .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>&lt;bundle name="Minecraft" marker="server.properties" action="keep"/&gt; (or folder="..." / program="true").</summary>
    static List<BundleRule> Bundles(XmlElement dict)
    {
        var list = new List<BundleRule>();
        foreach (XmlElement b in dict.SelectNodes("bundle")!)
        {
            var name = Required(b, "name");
            string? marker = b.GetAttribute("marker") is { Length: > 0 } m ? m : null;
            string? folder = b.GetAttribute("folder") is { Length: > 0 } f ? f : null;
            bool program = bool.TryParse(b.GetAttribute("program"), out var p) && p;
            if ((marker != null ? 1 : 0) + (folder != null ? 1 : 0) + (program ? 1 : 0) != 1)
                throw new ConfigurationException($"<bundle name=\"{name}\">: give exactly one of marker, folder or program=\"true\"");
            var action = b.GetAttribute("action") switch
            {
                "" or "keep" => BundleAction.Keep,
                "review" => BundleAction.Review,
                var other => throw new ConfigurationException($"<bundle name=\"{name}\">: action must be keep or review, not '{other}'"),
            };
            list.Add(new BundleRule(name, marker, folder, program, action));
        }
        return list;
    }

    static List<(int, int)> Sizes(XmlElement parent, string element) =>
        parent.SelectNodes(element)!.Cast<XmlElement>()
            .Select(e => (int.TryParse(e.GetAttribute("width"), out var w) ? w : 0, int.TryParse(e.GetAttribute("height"), out var h) ? h : 0))
            .Where(s => s.Item1 > 0 && s.Item2 > 0).ToList();

    static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
