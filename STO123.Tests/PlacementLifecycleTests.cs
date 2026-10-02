using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class PlacementLifecycleTests
{
    private readonly DateTime now = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PausedPlacementKeepsItsRemainingTimeAcrossLongAbsence()
    {
        var row = new KetQuaLamBai { MaKetQua = 7, TrangThai = "BO_DO", ThoiGianConLaiGiay = 95 * 60 };
        Assert.Equal(PlacementAction.Create, PlacementLifecycle.Decide(null, now));
        Assert.Equal(PlacementAction.Resume, PlacementLifecycle.Decide(row, now));
        Assert.Equal(PlacementAction.Resume, PlacementLifecycle.Decide(row, now.AddHours(3)));
        Assert.Equal(7, row.MaKetQua);
        Assert.Equal(95 * 60, ExamTimer.Remaining(row, now.AddHours(3)));
        row.BatDauPhienLuc = now.AddHours(3);
        Assert.Equal(95 * 60 - 10, ExamTimer.Remaining(row, now.AddHours(3).AddSeconds(10)));
        ExamTimer.Charge(row, now.AddHours(3).AddSeconds(10), false);
        Assert.Equal(95 * 60 - 10, ExamTimer.Remaining(row, now.AddHours(6)));
        ExamTimer.Charge(row, now.AddHours(6), false);
        Assert.Equal(95 * 60 - 10, row.ThoiGianConLaiGiay);
        row.ThoiGianConLaiGiay = 0;
        Assert.Equal(PlacementAction.FinalizeExpired, PlacementLifecycle.Decide(row, now));
        row.TrangThai = ExamCore.Expired;
        Assert.Equal(PlacementAction.Create, PlacementLifecycle.Decide(row, now));
        row.TrangThai = ExamCore.Submitted;
        Assert.Equal(PlacementAction.Create, PlacementLifecycle.Decide(row, now));
    }
}
