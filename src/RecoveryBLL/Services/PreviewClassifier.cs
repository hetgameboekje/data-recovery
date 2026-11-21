using RecoveryBLL.Dtos;

namespace RecoveryBLL.Services;

public sealed record PreviewRules(int MaxLongSide, IReadOnlySet<(int Width, int Height)> KnownSizes);

public enum PreviewKind { None, Preview, Thumbnail }

public readonly record struct PreviewCandidate(long FileId, PreviewKind Kind);

/// <summary>Phase 3b (pure): small photos without camera data are previews or thumbnails. Camera data always protects a photo.</summary>
public static class PreviewClassifier
{
    public static IReadOnlyList<PreviewCandidate> Classify(IReadOnlyList<FileFactsDto> files, PreviewRules rules)
    {
        var result = new List<PreviewCandidate>();
        foreach (var f in files)
        {
            if (f.MediaType != "Photos" || f.HasCamera || f.Width is not int w || f.Height is not int h) continue;
            if (rules.KnownSizes.Contains((w, h)) || rules.KnownSizes.Contains((h, w)))
                result.Add(new(f.Id, PreviewKind.Thumbnail));
            else if (Math.Max(w, h) <= rules.MaxLongSide)
                result.Add(new(f.Id, PreviewKind.Preview));
        }
        return result;
    }
}
