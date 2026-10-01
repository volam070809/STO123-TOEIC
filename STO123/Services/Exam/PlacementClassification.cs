using STO123.Models;

namespace STO123.Services.Exam;

public sealed record PlacementClassification(string Status, byte? Stage, string? ModelVersion,
    int? TargetScore)
{
    public static PlacementClassification From(KetQuaPhanLopKNN? row) => row is null ?
        new("UNAVAILABLE", null, null, null) :
        new("AVAILABLE", row.GiaiDoanDeXuat, row.PhienBanMoHinh, row.DiemMucTieu);
}

public static class PlacementTarget
{
    public static void Apply(KetQuaPhanLopKNN row, int? targetScore)
    {
        if (targetScore is < 10 or > 990)
            throw new ExamProblem("INVALID_TARGET", "Điểm mục tiêu phải từ 10 đến 990.");
        row.DiemMucTieu = targetScore;
    }
}
