using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class PlacementClassificationTests
{
    [Fact]
    public void MissingModelDoesNotFabricateAStage()
    {
        var result = PlacementClassification.From(null);
        Assert.Equal("UNAVAILABLE", result.Status);
        Assert.Null(result.Stage);
        Assert.Null(result.ModelVersion);
    }

    [Fact]
    public void ChangingTargetLeavesClassificationUntouched()
    {
        var row = new KetQuaPhanLopKNN { MaKetQua = 42, GiaiDoanDeXuat = 2,
            PhienBanMoHinh = "trained-v1", DiemMucTieu = 550 };
        PlacementTarget.Apply(row, 800);
        Assert.Equal(800, row.DiemMucTieu);
        Assert.Equal(2, row.GiaiDoanDeXuat);
        Assert.Equal("trained-v1", row.PhienBanMoHinh);
        Assert.Equal(42, row.MaKetQua);
        Assert.Equal("INVALID_TARGET", Assert.Throws<ExamProblem>(() => PlacementTarget.Apply(row, 1000)).Code);
    }
}
