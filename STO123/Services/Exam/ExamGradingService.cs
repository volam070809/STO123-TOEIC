using STO123.DTOs.Exam;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed class ExamGradingService
{
    public ExamResultDto Grade(KetQuaLamBai attempt, IReadOnlyList<CauHoiLuotLam> questions,
        IReadOnlyDictionary<int, ChiTietKetQua> answers)
    {
        RawStatsDto Stats(IEnumerable<CauHoiLuotLam> source)
        {
            var list = source.ToList();
            var correct = 0;
            var incorrect = 0;
            foreach (var q in list)
            {
                if (!answers.TryGetValue(q.MaCauHoiLuotLam, out var answer) || string.IsNullOrWhiteSpace(answer.DapAnChon)) continue;
                if (answer.DapAnChon.Trim() == q.PhuongAnDung.Trim()) correct++;
                else incorrect++;
            }
            return new(list.Count, correct, incorrect, list.Count - correct - incorrect,
                list.Count == 0 ? 0 : Math.Round(100m * correct / list.Count, 2));
        }
        var parts = Enumerable.Range(1, 7).Select(part => new PartStatsDto(part,
            Stats(questions.Where(q => q.MaPartNavigation?.SoPart == part)))).ToList();
        var started = ExamCore.Utc(attempt.NgayLamBai);
        var finished = attempt.NgayNopBai.HasValue ? ExamCore.Utc(attempt.NgayNopBai.Value) : (DateTime?)null;
        return new(attempt.MaKetQua, attempt.TrangThai, started, finished,
            finished.HasValue ? Math.Max(0, (int)(finished.Value - started).TotalSeconds) : 0,
            attempt.DiemNghe, attempt.DiemDoc, attempt.DiemTong, attempt.DiemTong.HasValue,
            Stats(questions), Stats(questions.Where(q => q.MaPartNavigation?.SoPart <= 4)),
            Stats(questions.Where(q => q.MaPartNavigation?.SoPart >= 5)), parts);
    }
}
