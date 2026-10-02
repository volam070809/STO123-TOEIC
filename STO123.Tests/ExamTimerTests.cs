using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class ExamTimerTests
{
    [Fact]
    public void HeartbeatsThenPausePreserveTimeAcrossThreeHours()
    {
        var start = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
        var attempt = new KetQuaLamBai
        {
            TrangThai = ExamCore.Active,
            ThoiGianConLaiGiay = 120 * 60,
            BatDauPhienLuc = start
        };
        for (var i = 1; i <= 100; i++)
            ExamTimer.Charge(attempt, start.AddSeconds(i * 15), true);
        ExamTimer.Charge(attempt, start.AddMinutes(25), false);
        Assert.Equal(95 * 60, attempt.ThoiGianConLaiGiay);
        Assert.Equal(95 * 60, ExamTimer.Remaining(attempt, start.AddHours(3)));
        ExamTimer.Charge(attempt, start.AddHours(3), false);
        Assert.Equal(95 * 60, attempt.ThoiGianConLaiGiay);
        attempt.BatDauPhienLuc = start.AddHours(3);
        Assert.Equal(95 * 60 - 15, ExamTimer.Remaining(attempt, start.AddHours(3).AddSeconds(15)));
    }

    [Fact]
    public void StaleSessionChargesOnlyLeaseAndZeroNeverRestarts()
    {
        var start = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
        var attempt = new KetQuaLamBai { ThoiGianConLaiGiay = 30, BatDauPhienLuc = start };
        ExamTimer.Charge(attempt, start.AddHours(3), true);
        Assert.Null(attempt.BatDauPhienLuc);
        Assert.Equal(10, attempt.ThoiGianConLaiGiay);
        attempt.ThoiGianConLaiGiay = 0;
        ExamTimer.Charge(attempt, start.AddHours(4), false);
        Assert.Equal(0, attempt.ThoiGianConLaiGiay);
    }
}
