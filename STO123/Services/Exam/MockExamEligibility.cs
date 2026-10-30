using STO123.Models;

namespace STO123.Services.Exam;

public static class MockExamEligibility
{
    public static IQueryable<DeThi> Visible(IQueryable<DeThi> exams, int placementExamId) =>
        exams.Where(exam => exam.LoaiDe == "DE_THI" && exam.TrangThai == "OPEN" &&
            exam.ThoiGianLamBai == ExamCore.DurationMinutes && exam.MaDeThi != placementExamId);
}
