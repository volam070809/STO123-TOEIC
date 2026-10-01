using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class PlacementLifecycleTests
{
    private readonly DateTime now = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnePlacementUsesSameAttemptAndDeadlineAcrossResume()
    {
        var row = new KetQuaLamBai { MaKetQua = 7, TrangThai = "BO_DO", HetHanLuc = now.AddMinutes(30) };
        Assert.Equal(PlacementAction.Create, PlacementLifecycle.Decide(null, now));
        Assert.Equal(PlacementAction.Resume, PlacementLifecycle.Decide(row, now));
        Assert.Equal(7, row.MaKetQua);
        Assert.Equal(now.AddMinutes(30), row.HetHanLuc);
        row.HetHanLuc = now.AddSeconds(-1);
        Assert.Equal(PlacementAction.FinalizeExpired, PlacementLifecycle.Decide(row, now));
        row.TrangThai = ExamCore.Expired;
        Assert.Equal(PlacementAction.ResultOnly, PlacementLifecycle.Decide(row, now));
        row.TrangThai = ExamCore.Submitted;
        Assert.Equal(PlacementAction.ResultOnly, PlacementLifecycle.Decide(row, now));
    }
}
