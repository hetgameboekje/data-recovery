using System.Diagnostics;
using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryUI.ViewModels;
using RecoveryUI.Views;

namespace RecoveryUI.Controllers;

/// <summary>
/// Phase 2b: the disk map. Built from the catalog on a worker thread; a newer request cancels an older one.
/// Read-only: the only outside action is "Show in Explorer".
/// </summary>
public sealed class DiskMapController(IDiskMapView view, IMainView main, DiskMapService maps, DiskService disks) : IPageController
{
    public Page Page => Page.DiskMap;
    public string Title => "Disk map";
    public IPageView View => view;

    static readonly Color[] Fallback =
    [
        Color.FromArgb(59, 130, 196), Color.FromArgb(217, 83, 79), Color.FromArgb(63, 163, 77), Color.FromArgb(224, 168, 0),
        Color.FromArgb(142, 92, 196), Color.FromArgb(23, 162, 184), Color.FromArgb(214, 120, 40), Color.FromArgb(140, 140, 140),
    ];

    long? scope;
    bool stale = true;
    CancellationTokenSource? running;

    public void Attach()
    {
        view.ScopeChanged += (_, id) => { scope = id; Load(); };
        view.RefreshRequested += (_, _) => { LoadScopes(); Load(); };
        view.OpenInExplorerRequested += (_, n) => Open(n);
    }

    public void OnShown()
    {
        LoadScopes();
        if (stale) Load();
    }

    /// <summary>The catalog changed (a scan or copy finished): rebuild next time the page is shown.</summary>
    public void MarkStale() => stale = true;

    void LoadScopes() => Task.Run(() =>
    {
        try
        {
            var mounted = disks.GetMountedRoots();
            var list = new List<MapScopeViewModel> { new(null, "All catalogued disks together") };
            list.AddRange(disks.GetCatalogued().Select(d => new MapScopeViewModel(d.Id,
                (mounted.TryGetValue(d.Id, out var r) ? r.TrimEnd('\\') + "  " : "") + d.DisplayName
                + (mounted.ContainsKey(d.Id) ? "" : "  (not plugged in)"))));
            view.ShowScopes(list, scope);
        }
        catch (RecoveryServiceException ex) { view.ShowError(ex.Message); }
    });

    void Load()
    {
        running?.Cancel();
        var cts = running = new CancellationTokenSource();
        stale = false;
        view.ShowLoading("Building the map from the catalog…");
        Task.Run(() =>
        {
            try
            {
                var map = maps.Build(scope, cts.Token);
                var catalogued = disks.GetCatalogued().ToDictionary(d => d.Id);
                var mounted = disks.GetMountedRoots();
                if (!cts.IsCancellationRequested) view.ShowMap(ToViewModel(map, catalogued, mounted));
            }
            catch (OperationCanceledException) { }
            catch (RecoveryServiceException ex) { stale = true; view.ShowError(ex.Message); }
        });
    }

    static DiskMapViewModel ToViewModel(DiskMap map, IReadOnlyDictionary<long, Disk> catalogued, IReadOnlyDictionary<long, string> mounted)
    {
        var colors = map.Categories.Select((c, i) => c.Color != null ? ColorTranslator.FromHtml(c.Color) : Fallback[i % Fallback.Length]).ToArray();
        var r = map.Root;

        string summary;
        if (r.Files == 0)
            summary = "Nothing catalogued here yet. Catalog a disk first (\"Catalog disks\").";
        else
        {
            summary = $"{Format.Bytes(r.Bytes)} in {r.Files:N0} files.";
            if (r.ExtraCopyBytes > 0) summary += $" {Format.Bytes(r.ExtraCopyBytes)} of that are extra copies (striped).";
            var largest = map.Categories.OrderByDescending(c => c.Bytes).First();
            summary += $" Largest: {largest.Name} {Format.Bytes(largest.Bytes)}"
                       + (largest.ExtraCopyBytes > 0 ? $", of which {Format.Bytes(largest.ExtraCopyBytes)} extra copies." : ".");
            if (r.PendingBytes > 0) summary += $" {Format.Bytes(r.PendingBytes)} is not fingerprinted yet (dotted).";
        }

        string PathOf(MapNode n)
        {
            if (n.Kind == MapNodeKind.Root) return n.Name;
            var root = mounted.TryGetValue(n.DiskId, out var m) ? m
                : catalogued.TryGetValue(n.DiskId, out var d) && d.LastRoot != null ? d.LastRoot : "?:\\";
            var rel = n.RelativePath;
            return rel.Length == 0 ? root : Path.Combine(root, rel);
        }

        return new DiskMapViewModel
        {
            Root = r,
            Colors = colors,
            CategoryNames = map.Categories.Select(c => c.Name).ToList(),
            Summary = summary,
            Legend = map.Categories.Select((c, i) => (c, i)).Where(x => x.c.Bytes > 0)
                .Select(x => new MapLegendViewModel { Name = x.c.Name, Color = colors[x.i], Size = Format.Bytes(x.c.Bytes) }).ToList(),
            Extensions = map.Extensions.Select(e => new ExtensionRowViewModel
            {
                Ext = e.Ext,
                Extension = e.Ext.Length == 0 ? "(none)" : "." + e.Ext,
                Type = map.Categories[e.Category].Name,
                Size = Format.Bytes(e.Bytes),
                Share = r.Bytes > 0 ? $"{100.0 * e.Bytes / r.Bytes:0.0}%" : "-",
                Files = e.Files.ToString("N0"),
                Extra = e.ExtraCopyBytes > 0 ? Format.Bytes(e.ExtraCopyBytes) : "-",
            }).ToList(),
            PathOf = PathOf,
        };
    }

    /// <summary>Opens Explorer at the block (read-only). Unplugged or moved files get a short explanation instead.</summary>
    void Open(MapNode n)
    {
        var mounted = disks.GetMountedRoots();
        var disk = disks.GetCatalogued().FirstOrDefault(d => d.Id == n.DiskId);
        if (n.Kind == MapNodeKind.Root || !mounted.TryGetValue(n.DiskId, out var root))
        {
            main.ShowInfo("Not plugged in", $"Plug in {disk?.DisplayName ?? "this disk"} to open it in Explorer. The map itself works without the disk.");
            return;
        }
        var rel = n.RelativePath;
        var path = rel.Length == 0 ? root : Path.Combine(root, rel);
        bool isFile = n.Kind == MapNodeKind.File;
        if (isFile ? !File.Exists(path) : !Directory.Exists(path))
        {
            main.ShowInfo("Not found", $"{path}\n\nis no longer there: it was moved or deleted after the disk was catalogued. Catalog the disk again to update the map.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", isFile ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            main.ShowError("Show in Explorer", ex.Message);
        }
    }
}
