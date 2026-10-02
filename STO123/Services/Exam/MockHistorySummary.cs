using STO123.DTOs.Exam;
using STO123.Models;

namespace STO123.Services.Exam;

public static class MockHistorySummary
{
    public static MockHistoryDto Build(IReadOnlyList<KetQuaLamBai> attempts, IReadOnlyList<DeThi> exams)
    {
        var finalized = attempts.Where(a => a.LoaiBaiLam == ExamCore.Mock &&
            a.TrangThai is ExamCore.Submitted or ExamCore.Expired).ToList();
        var scored = finalized.Where(a => a.DiemTong.HasValue).ToList();
        var fixedExams = exams.Where(e => e.LoaiDe == "DE_THI").Select(exam =>
        {
            var rows = attempts.Where(a => a.LoaiBaiLam == ExamCore.Mock && a.MaDeThi == exam.MaDeThi).ToList();
            var completed = rows.Where(a => a.TrangThai is ExamCore.Submitted or ExamCore.Expired).ToList();
            var scores = completed.Where(a => a.DiemTong.HasValue).ToList();
            var latest = completed.OrderByDescending(a => a.NgayNopBai).ThenByDescending(a => a.MaKetQua).FirstOrDefault();
            var active = rows.Where(a => a.TrangThai is ExamCore.Active or "BO_DO")
                .OrderByDescending(a => a.NgayLamBai).FirstOrDefault();
            return new FixedMockHistoryDto(exam.MaDeThi, exam.TenDe, exam.TrangThai,
                completed.Count, latest?.DiemTong,
                scores.Count == 0 ? null : scores.Average(a => (decimal)a.DiemTong!.Value),
                scores.Count == 0 ? null : scores.Max(a => a.DiemTong),
                latest?.NgayNopBai is { } date ? ExamCore.Utc(date) : null, active?.MaKetQua);
        }).OrderBy(e => e.ExamId).ToList();
        var randomAttempts = finalized.Where(a => !a.MaDeThi.HasValue)
            .OrderByDescending(a => a.NgayLamBai)
            .Select(a => new ExamHistoryDto(a.MaKetQua, a.TrangThai, ExamCore.Utc(a.NgayLamBai),
                a.HetHanLuc.HasValue ? ExamCore.Utc(a.HetHanLuc.Value) : null, a.DiemTong)).ToList();
        var latestMock = scored.OrderByDescending(a => a.NgayNopBai).ThenByDescending(a => a.MaKetQua).FirstOrDefault();
        var averages = fixedExams.Where(e => e.AverageScore.HasValue).Select(e => e.AverageScore!.Value).ToList();
        return new(fixedExams, randomAttempts, new(
            averages.Count == 0 ? null : averages.Average(),
            scored.Count == 0 ? null : scored.Max(a => a.DiemTong),
            latestMock?.DiemTong));
    }
}
