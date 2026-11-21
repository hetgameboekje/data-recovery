using RecoveryBLL.Dtos;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

public class PreviewClassifierTests
{
    static readonly PreviewRules Rules = new(640, new HashSet<(int, int)> { (160, 120) });

    static FileFactsDto Photo(int? w, int? h, bool camera = false, string type = "Photos") =>
        new(1, 1, "x/a.jpg", "jpg", 1000, type, w, h, camera);

    static PreviewKind Kind(FileFactsDto f) => PreviewClassifier.Classify([f], Rules).FirstOrDefault().Kind;

    [Fact] public void Camera_data_protects() => Assert.Equal(PreviewKind.None, Kind(Photo(100, 100, camera: true)));
    [Fact] public void Known_size_either_orientation()
    {
        Assert.Equal(PreviewKind.Thumbnail, Kind(Photo(160, 120)));
        Assert.Equal(PreviewKind.Thumbnail, Kind(Photo(120, 160)));
    }
    [Fact] public void Threshold_edge()
    {
        Assert.Equal(PreviewKind.Preview, Kind(Photo(640, 480)));
        Assert.Equal(PreviewKind.None, Kind(Photo(641, 480)));
    }
    [Fact] public void Unknown_dimensions_ignored() => Assert.Equal(PreviewKind.None, Kind(Photo(null, null)));
    [Fact] public void Non_photo_ignored() => Assert.Equal(PreviewKind.None, Kind(Photo(100, 100, type: "Videos")));
}
