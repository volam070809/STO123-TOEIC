using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Services.Ai;

public sealed record AiLearningContext(string? Type, int? WordId, int? CourseId, int? StepId,
    int? AttemptId, int? QuestionId);

public sealed class AiLearningContextService(ToeicDbContext db, TimeProvider clock)
{
    public async Task<string> ResolveAsync(int learnerId, AiLearningContext? context, CancellationToken ct)
    {
        var stage = await db.KetQuaPhanLopKNN.AsNoTracking()
            .Where(x => x.MaKetQuaNavigation.MaHocVien == learnerId)
            .OrderByDescending(x => x.NgayPhanLop).Select(x => (byte?)x.GiaiDoanDeXuat)
            .FirstOrDefaultAsync(ct);
        var label = stage switch { 1 => "Beginner", 2 => "Intermediate", 3 => "Advanced", _ => "chưa phân lớp" };
        var prefix = $"Giai đoạn học hiện tại: {label}.";
        if (context?.Type is null) return prefix;
        if (context.Type == "VOCABULARY" && context.WordId is > 0)
        {
            var word = await db.TuVung.AsNoTracking().Where(w => w.MaTuVung == context.WordId &&
                w.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"))
                .Select(w => new { w.TuVung1, w.Nghia, w.PhienAm, w.LoaiTu, w.ViDu, w.DichViDu })
                .FirstOrDefaultAsync(ct);
            if (word is null) throw Unavailable();
            return $"{prefix}\nTừ đang học: {word.TuVung1}; nghĩa: {word.Nghia}; phiên âm: {word.PhienAm}; loại từ: {word.LoaiTu}; ví dụ: {word.ViDu}; dịch: {word.DichViDu}.";
        }
        if (context.Type == "LESSON" && context.CourseId is > 0 && context.StepId is > 0)
        {
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            if (!await CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                .AnyAsync(id => id == context.CourseId, ct)) throw Unavailable();
            var step = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == context.StepId &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == context.CourseId && !s.IsArchived &&
                !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived)
                .Select(s => new { s.TieuDe, lesson = s.MaLessonNavigation.TenLesson,
                    lecture = s.MaBaiGiangNavigation == null ? null : s.MaBaiGiangNavigation.NoiDung,
                    summary = s.MaBaiGiangNavigation == null ? null : s.MaBaiGiangNavigation.NoiDungTomTat })
                .FirstOrDefaultAsync(ct);
            if (step is null) throw Unavailable();
            var content = step.lecture ?? step.summary ?? "";
            return $"{prefix}\nBài đang học: {step.lesson} / {step.TieuDe}. Nội dung: {content[..Math.Min(1800, content.Length)]}";
        }
        if (context.Type == "PRACTICE" && context.AttemptId is > 0 && context.QuestionId is > 0)
        {
            var q = await db.CauHoiLuotLam.AsNoTracking().Where(q => q.MaKetQua == context.AttemptId &&
                q.MaCauHoiLuotLam == context.QuestionId && q.MaKetQuaNavigation.MaHocVien == learnerId &&
                q.MaKetQuaNavigation.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                q.MaKetQuaNavigation.TrangThai == ExamCore.Active)
                .Select(q => new { q.MaPart, q.NoiDung, q.PhuongAnA, q.PhuongAnB,
                    q.PhuongAnC, q.PhuongAnD, q.PhuongAnDung, q.GiaiThich,
                    passage = q.MaNhomLuotLamNavigation == null ? null : q.MaNhomLuotLamNavigation.NoiDungNguLieu,
                    selected = q.ChiTietKetQua == null ? null : q.ChiTietKetQua.DapAnChon })
                .FirstOrDefaultAsync(ct);
            if (q is null) throw Unavailable();
            if (q.selected is null)
                return $"{prefix}\nHọc viên đang làm câu luyện tập Part {q.MaPart} nhưng chưa kiểm tra đáp án. Chỉ hướng dẫn phương pháp chung của Part này; không đoán hoặc tiết lộ đáp án câu đang làm.";
            var passage = q.passage ?? "";
            var content = $"{prefix}\nCâu luyện tập Part {q.MaPart}: {q.NoiDung}. Ngữ liệu: {passage[..Math.Min(1600, passage.Length)]}. " +
                $"A: {q.PhuongAnA}; B: {q.PhuongAnB}; C: {q.PhuongAnC}; D: {q.PhuongAnD}.";
            return content + $" Đã kiểm tra: học viên chọn {q.selected}; đáp án đúng {q.PhuongAnDung}; giải thích: {q.GiaiThich}.";
        }
        throw Unavailable();
    }

    private static AiChatException Unavailable() => new("AI_CONTEXT_UNAVAILABLE",
        "Không thể dùng ngữ cảnh này. Hãy mở lại bài học hoặc hỏi AI không kèm ngữ cảnh.", 403);
}
