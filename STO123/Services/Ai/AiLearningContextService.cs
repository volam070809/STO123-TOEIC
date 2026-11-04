using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Services.Ai;

public sealed record AiLearningContext(string? Type, int? WordId, int? CourseId, int? StepId,
    long? AttemptId, int? QuestionId);

public sealed record AiContextResolution(string Text, AiSource? Source = null,
    bool UncheckedPractice = false, int? Part = null);

public sealed class AiLearningContextService(ToeicDbContext db, TimeProvider clock)
{
    public async Task<AiContextResolution> ResolveAsync(int learnerId, AiLearningContext? context, CancellationToken ct)
    {
        if (context?.Type is null) return new("");
        if (context.Type == "VOCABULARY" && context.WordId is > 0)
        {
            var word = await db.TuVung.AsNoTracking().Where(w => w.MaTuVung == context.WordId &&
                w.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"))
                .Select(w => new { w.TuVung1, w.Nghia, w.PhienAm, w.LoaiTu, w.ViDu, w.DichViDu,
                    topic = w.TuVungChuDe.Where(link => link.MaChuDeNavigation.TrangThai == "DANG_MO")
                        .OrderBy(link => link.MaChuDe).Select(link => new {
                            link.MaChuDe, link.MaChuDeNavigation.TenChuDe }).FirstOrDefault() })
                .FirstOrDefaultAsync(ct);
            if (word?.topic is null) throw Unavailable();
            return new($"Từ đang học: {word.TuVung1}; nghĩa: {word.Nghia}; phiên âm: {word.PhienAm}; " +
                $"loại từ: {word.LoaiTu}; ví dụ: {word.ViDu}; dịch: {word.DichViDu}.",
                new AiSource($"Từ vựng · {word.topic.TenChuDe}", $"/vocabulary?topic={word.topic.MaChuDe}"));
        }
        if (context.Type == "LESSON" && context.CourseId is > 0 && context.StepId is > 0)
        {
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            var step = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == context.StepId &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == context.CourseId && !s.IsArchived &&
                CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                    .Contains(s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc) &&
                !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
                (s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO" ||
                 s.MaChuDe != null && s.MaChuDeNavigation.TrangThai == "DANG_MO" ||
                 s.MaBaiTap != null && db.PhienBanBaiTapKhoaHoc.Any(v =>
                     v.MaBaiTap == s.MaBaiTap && v.TrangThai == "XUAT_BAN")))
                .Select(s => new { s.TieuDe, lesson = s.MaLessonNavigation.TenLesson,
                    course = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TenKhoaHoc,
                    lecture = s.MaBaiGiangNavigation == null ? null : s.MaBaiGiangNavigation.NoiDung,
                    summary = s.MaBaiGiangNavigation == null ? null : s.MaBaiGiangNavigation.NoiDungTomTat })
                .FirstOrDefaultAsync(ct);
            if (step is null) throw Unavailable();
            var content = step.lecture ?? step.summary ?? "";
            return new($"Khóa: {step.course}; bài: {step.lesson}; hoạt động: {step.TieuDe}. " +
                $"Nội dung: {content[..Math.Min(1800, content.Length)]}",
                new AiSource($"{step.course} · {step.lesson}",
                    $"/courses/{context.CourseId}/steps/{context.StepId}"));
        }
        if (context.Type == "PRACTICE" && context.AttemptId is > 0 && context.QuestionId is > 0)
        {
            if (context.AttemptId > int.MaxValue) throw Unavailable();
            var practiceAttemptId = (int)context.AttemptId.Value;
            var q = await db.CauHoiLuotLam.AsNoTracking().Where(q => q.MaKetQua == practiceAttemptId &&
                q.MaCauHoiLuotLam == context.QuestionId && q.MaKetQuaNavigation.MaHocVien == learnerId &&
                q.MaKetQuaNavigation.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                q.MaKetQuaNavigation.TrangThai == ExamCore.Active)
                .Select(q => new { part = q.MaPartNavigation.SoPart, q.NoiDung, q.PhuongAnA, q.PhuongAnB,
                    q.PhuongAnC, q.PhuongAnD, q.PhuongAnDung, q.GiaiThich,
                    passage = q.MaNhomLuotLamNavigation == null ? null : q.MaNhomLuotLamNavigation.NoiDungNguLieu,
                    selected = q.ChiTietKetQua == null ? null : q.ChiTietKetQua.DapAnChon })
                .FirstOrDefaultAsync(ct);
            if (q is null) throw Unavailable();
            if (string.IsNullOrWhiteSpace(q.selected))
            {
                var passageHint = q.passage ?? "";
                var questionHint = q.NoiDung ?? "";
                return new($"Câu luyện tập Part {q.part} chưa kiểm tra. " +
                    $"Nội dung câu: {questionHint[..Math.Min(500, questionHint.Length)]}. " +
                    $"Ngữ liệu: {passageHint[..Math.Min(700, passageHint.Length)]}. " +
                    "Chỉ giải thích kiến thức và gợi ý cách suy luận; không nêu hoặc suy đoán phương án đúng.",
                    new AiSource($"Luyện tập Part {q.part}", "/practice/parts"), true, q.part);
            }
            var passage = q.passage ?? "";
            var content = $"Câu luyện tập Part {q.part} theo bản gốc của lượt đang làm: {q.NoiDung}. " +
                $"Ngữ liệu: {passage[..Math.Min(1200, passage.Length)]}. " +
                $"A: {q.PhuongAnA}; B: {q.PhuongAnB}; C: {q.PhuongAnC}; D: {q.PhuongAnD}. " +
                $"Học viên chọn {q.selected}; đáp án đúng {q.PhuongAnDung}; giải thích: {q.GiaiThich}.";
            return new(content, new AiSource($"Câu đã kiểm tra · Part {q.part}", "/practice/parts"), false, q.part);
        }
        if (context.Type == "COURSE_EXERCISE" && context.CourseId is > 0 && context.StepId is > 0 &&
            context.AttemptId is > 0 && context.QuestionId is > 0)
        {
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            var courseId = context.CourseId.Value;
            var stepId = context.StepId.Value;
            var attemptId = context.AttemptId.Value;
            var questionId = context.QuestionId.Value;
            var attempt = await db.LuotBaiTapKhoaHoc.AsNoTracking().Where(a => a.MaLuot == attemptId &&
                a.MaBuoc == stepId && db.DangKyGoiHoc.Any(e =>
                    e.MaDangKy == a.MaDangKy && e.MaNguoiDung == learnerId) &&
                CourseEntitlement.ActiveCourseIds(db, learnerId, now).Contains(courseId) &&
                db.BuocLoTrinh.Any(s => s.MaBuoc == stepId && s.MaBaiTap == a.MaBaiTap &&
                    s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
                    !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                    s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO"))
                .Select(a => new { a.MaPhienBan, a.MaBaiTap }).FirstOrDefaultAsync(ct);
            if (attempt is null) throw Unavailable();
            var check = await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking().Where(c =>
                c.MaLuot == attemptId && c.MaCauHoi == questionId && c.MaPhienBan == attempt.MaPhienBan)
                .OrderByDescending(c => c.LanKiemTra)
                .Select(c => new { c.PhuongAnChon, c.PhuongAnDungLucKiemTra,
                    c.GiaiThichLucKiemTra }).FirstOrDefaultAsync(ct);
            var source = new AiSource("Bài tập đang học", $"/courses/{courseId}/steps/{stepId}");
            var question = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().Where(q =>
                q.MaCauHoi == questionId && q.MaPhienBan == attempt.MaPhienBan &&
                q.MaBaiTap == attempt.MaBaiTap)
                .Select(q => new { q.NoiDung, q.NguLieu, q.PhuongAnA, q.PhuongAnB,
                    q.PhuongAnC, q.PhuongAnD }).FirstOrDefaultAsync(ct);
            if (question is null) throw Unavailable();
            if (check is null)
            {
                var passageHint = question.NguLieu ?? "";
                return new($"Câu bài tập trong khóa chưa kiểm tra: {question.NoiDung}. " +
                    $"Ngữ liệu: {passageHint[..Math.Min(700, passageHint.Length)]}. " +
                    "Chỉ gợi ý cách suy luận; không nêu hoặc suy đoán phương án đúng.", source, true);
            }
            return new($"Câu bài tập thuộc phiên bản học viên đã làm: {question.NoiDung}. " +
                $"Ngữ liệu: {question.NguLieu}. A: {question.PhuongAnA}; B: {question.PhuongAnB}; " +
                $"C: {question.PhuongAnC}; D: {question.PhuongAnD}. " +
                $"Học viên chọn {check.PhuongAnChon}; đáp án lúc kiểm tra " +
                $"{check.PhuongAnDungLucKiemTra}; giải thích lúc kiểm tra: {check.GiaiThichLucKiemTra}.", source);
        }
        throw Unavailable();
    }

    private static AiChatException Unavailable() => new("AI_CONTEXT_UNAVAILABLE",
        "Không thể dùng ngữ cảnh này. Hãy mở lại bài học hoặc hỏi AI không kèm ngữ cảnh.", 403);
}
