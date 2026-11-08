using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Admin;

public sealed record AdminQuestionPartCount(int Part, int Questions, int Groups);
public sealed record AdminQuestionBankCount(string Bank, int TotalQuestions, int TotalGroups,
    IReadOnlyList<AdminQuestionPartCount> Parts);
public sealed record AdminExamQuestionCount(int ExamId, string Name, string Status, int Questions);
public sealed record AdminQuestionStatistics(IReadOnlyList<AdminQuestionBankCount> Banks,
    IReadOnlyList<AdminExamQuestionCount> Exams);

public static class AdminQuestionStatisticsQueries
{
    private static readonly string[] Banks = ["DE_THI", "LUYEN_TAP"];

    public static async Task<AdminQuestionStatistics> Load(ToeicDbContext db, CancellationToken ct)
    {
        var questionRows = await db.CauHoi.AsNoTracking()
            .Where(q => q.LoaiCauHoi == "DE_THI" || q.LoaiCauHoi == "LUYEN_TAP")
            .GroupBy(q => new { q.LoaiCauHoi, q.MaPartNavigation.SoPart })
            .Select(g => new { Bank = g.Key.LoaiCauHoi, Part = g.Key.SoPart,
                Questions = g.Count() }).ToListAsync(ct);

        // A group is a shared passage with multiple questions. Single-question media is not a group.
        var groupRows = await db.NhomCauHoi.AsNoTracking()
            .Where(m => (m.MaCauHoiNavigation.LoaiCauHoi == "DE_THI" ||
                m.MaCauHoiNavigation.LoaiCauHoi == "LUYEN_TAP") &&
                (m.MaCauHoiNavigation.MaPartNavigation.SoPart == 3 ||
                m.MaCauHoiNavigation.MaPartNavigation.SoPart == 4 ||
                m.MaCauHoiNavigation.MaPartNavigation.SoPart == 6 ||
                m.MaCauHoiNavigation.MaPartNavigation.SoPart == 7))
            .GroupBy(m => new { m.MaNguLieu, m.MaCauHoiNavigation.LoaiCauHoi,
                m.MaCauHoiNavigation.MaPartNavigation.SoPart })
            .Where(g => g.Count() > 1)
            .Select(g => new { Bank = g.Key.LoaiCauHoi, Part = g.Key.SoPart })
            .ToListAsync(ct);

        var banks = Banks.Select(bank => {
            var parts = Enumerable.Range(1, 7).Select(part => new AdminQuestionPartCount(part,
                questionRows.Where(row => row.Bank == bank && row.Part == part).Sum(row => row.Questions),
                groupRows.Count(row => row.Bank == bank && row.Part == part))).ToArray();
            return new AdminQuestionBankCount(bank, parts.Sum(part => part.Questions),
                parts.Sum(part => part.Groups), parts);
        }).ToArray();

        var exams = await db.DeThi.AsNoTracking().Where(e => e.LoaiDe == "DE_THI")
            .OrderBy(e => e.MaDeThi)
            .Select(e => new AdminExamQuestionCount(e.MaDeThi, e.TenDe, e.TrangThai,
                e.MaPhienBanHienHanh == null ? e.CauHoiDeThi.Count :
                    db.PhienBanDeThi.Where(v => v.MaPhienBanDeThi == e.MaPhienBanHienHanh)
                        .Select(v => v.SoCau).FirstOrDefault()))
            .ToListAsync(ct);
        return new AdminQuestionStatistics(banks, exams);
    }
}
