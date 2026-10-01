using STO123.Models;

namespace STO123.Services.Exam;

public static class FixedExamPlan
{
    public static IReadOnlyList<PlannedUnit> Select(IReadOnlyList<CauHoiDeThi> links,
        IEnumerable<PlannedUnit> availableUnits)
    {
        static ExamProblem Invalid() => new("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);
        if (links.Count != 200 || links.Select((link, index) => link.ThuTu == index + 1).Any(valid => !valid))
            throw Invalid();
        var lookup = availableUnits.SelectMany(unit => unit.Questions.Select(question => (question.MaCauHoi, unit)))
            .ToDictionary(item => item.MaCauHoi, item => item.unit);
        var result = new List<PlannedUnit>();
        var index = 0;
        while (index < links.Count)
        {
            if (!lookup.TryGetValue(links[index].MaCauHoi, out var unit)) throw Invalid();
            if (index + unit.Questions.Count > links.Count ||
                !unit.Questions.Select(q => q.MaCauHoi).SequenceEqual(
                    links.Skip(index).Take(unit.Questions.Count).Select(link => link.MaCauHoi)))
                throw Invalid();
            result.Add(unit);
            index += unit.Questions.Count;
        }
        if (!ExamGenerationService.ValidPlan(result)) throw Invalid();
        return result;
    }
}
