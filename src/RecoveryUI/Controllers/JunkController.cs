using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;
using RecoveryUI.Views;

namespace RecoveryUI.Controllers;

/// <summary>
/// Phase 3: shows what the junk and keep-together rules find in the catalog. "Check again" reloads dictionaries.xml first,
/// so edited rules show at once. Read-only; the Copy page uses the same rules.
/// </summary>
public sealed class JunkController(IJunkView view, IMainView main, JunkService junk, DictionaryService dictionaries, DiskService disks) : IPageController
{
    public Page Page => Page.Junk;
    public string Title => "Junk and apps";
    public IPageView View => view;

    bool stale = true;
    CancellationTokenSource? running;

    public void Attach()
    {
        view.RefreshRequested += (_, _) => Load(reloadRules: true);
        view.OpenRulesRequested += (_, _) => ExplorerLauncher.Select(main, dictionaries.Location);
        view.OpenInExplorerRequested += (_, t) => ExplorerLauncher.Show(main, disks, t.DiskId, t.Path, t.IsFile);
    }

    public void OnShown()
    {
        if (stale) Load(reloadRules: false);
    }

    /// <summary>The catalog changed (a scan or copy finished): check again next time the page is shown.</summary>
    public void MarkStale() => stale = true;

    void Load(bool reloadRules)
    {
        running?.Cancel();
        var cts = running = new CancellationTokenSource();
        stale = false;
        view.ShowLoading("Checking the catalog…");
        Task.Run(() =>
        {
            try
            {
                if (reloadRules) dictionaries.Reload();
                var r = junk.Analyze(cts.Token);
                var names = disks.GetCatalogued().ToDictionary(d => d.Id, d => d.DisplayName);
                var roots = disks.GetMountedRoots();
                if (!cts.IsCancellationRequested) view.ShowResult(ToViewModel(r, names, roots, disks));
            }
            catch (OperationCanceledException) { }
            catch (RecoveryServiceException ex) { stale = true; view.ShowError(ex.Message); }
        });
    }

    static JunkPageViewModel ToViewModel(FileTagResult r, IReadOnlyDictionary<long, string> names, IReadOnlyDictionary<long, string> mounted, DiskService disks)
    {
        var lastRoots = disks.GetCatalogued().ToDictionary(d => d.Id, d => d.LastRoot);
        string Full(long disk, string rel)
        {
            var root = mounted.TryGetValue(disk, out var m) ? m : lastRoots.GetValueOrDefault(disk) ?? "?:\\";
            return Path.Combine(root, rel);
        }
        static string GoesTo(TagKind kind, string reason) =>
            kind == TagKind.KeepTogether ? "Apps and data\\<disk>\\<original path>" : $"_Review\\{reason}\\<disk>\\<original path>";

        var junkGroups = r.Groups.Where(g => g.Kind == TagKind.Junk).ToList();
        var kept = r.Bundles.Where(b => b.Kind == TagKind.KeepTogether).ToList();
        var review = r.Bundles.Where(b => b.Kind == TagKind.ReviewTogether).ToList();

        var summary = r.Tags.Count == 0
            ? "Nothing found: no junk and no keep-together folders in the catalog."
            : $"{junkGroups.Sum(g => g.Files):N0} junk files, {kept.Count:N0} folders kept whole, {review.Count:N0} reinstallable programs.";
        if (r.UnmeasuredImages > 0)
            summary += $" {r.UnmeasuredImages:N0} pictures have no pixel size yet, so the size rules skipped them (Report: \"Fingerprint the rest\").";

        return new JunkPageViewModel
        {
            JunkSize = Format.Bytes(junkGroups.Sum(g => g.Bytes)),
            JunkCaption = $"{junkGroups.Sum(g => g.Files):N0} files, to _Review",
            KeptSize = Format.Bytes(kept.Sum(b => b.Bytes)),
            KeptCaption = $"{kept.Count:N0} folders, to Apps and data",
            ReviewSize = Format.Bytes(review.Sum(b => b.Bytes)),
            ReviewCaption = $"{review.Count:N0} folders, to _Review",
            Summary = summary,
            Groups = r.Groups.Select(g => new JunkGroupRowViewModel
            {
                Reason = g.Reason,
                What = g.Reason,
                Kind = g.Kind switch { TagKind.Junk => "Junk", TagKind.KeepTogether => "Kept whole", _ => "Reinstallable" },
                Files = g.Files.ToString("N0"),
                Size = Format.Bytes(g.Bytes),
                GoesTo = GoesTo(g.Kind, g.Reason),
            }).ToList(),
            Bundles = r.Bundles.Select(b => new BundleRowViewModel
            {
                DiskId = b.DiskId,
                Root = b.Root,
                Folder = Full(b.DiskId, b.Root),
                What = b.Name,
                Files = b.Files.ToString("N0"),
                Size = Format.Bytes(b.Bytes),
                GoesTo = GoesTo(b.Kind, b.Name),
                Disk = names.GetValueOrDefault(b.DiskId, "?"),
            }).ToList(),
            Examples = r.Examples.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<JunkExampleRowViewModel>)kv.Value.Select(f => new JunkExampleRowViewModel
            {
                DiskId = f.DiskId,
                RelativePath = f.Path,
                Path = Full(f.DiskId, f.Path),
                Size = Format.Bytes(f.Size),
            }).ToList()),
        };
    }
}
