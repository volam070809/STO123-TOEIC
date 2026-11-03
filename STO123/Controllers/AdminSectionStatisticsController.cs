using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[Route("api/admin/statistics")]
public sealed class AdminSectionStatisticsController(ToeicDbContext db, TimeProvider clock) : ControllerBase
{
    private sealed record Enrollment(int CourseId, int EnrollmentId, int LearnerId, string LearnerName,
        string Status, DateTime? Starts, DateTime? Ends);
    private sealed record LearnerProgress(int CourseId, int EnrollmentId, int LearnerId, string LearnerName,
        int Completed, int Incomplete, int Percent, bool Started, bool Finished, bool Active);
    private static object Metric(string label, object value, string? note = null) => new { label, value, note };

    private async Task<(List<LearnerProgress> Learners, Dictionary<int, int> Steps, int CompletedRecords,
        int IncompleteRecords)> CourseData(int? courseId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var direct = await db.ChiTietDangKyKhoaHoc.AsNoTracking()
            .Where(x => (courseId == null || x.MaKhoaHoc == courseId) &&
                x.MaDangKyNavigation.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG"))
            .Select(x => new Enrollment(x.MaKhoaHoc, x.MaDangKy,
                x.MaDangKyNavigation.MaNguoiDung, x.MaDangKyNavigation.MaNguoiDungNavigation.HoTen,
                x.MaDangKyNavigation.TrangThai, x.MaDangKyNavigation.NgayBatDau,
                x.MaDangKyNavigation.NgayKetThuc)).ToListAsync(ct);
        var package = await db.DangKyGoiHoc.AsNoTracking()
            .Where(e => e.MaGoiHoc != null && e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG"))
            .SelectMany(e => e.MaGoiHocNavigation.MaKhoaHoc
                .Where(c => courseId == null || c.MaKhoaHoc == courseId)
                .Select(c => new Enrollment(c.MaKhoaHoc, e.MaDangKy, e.MaNguoiDung,
                    e.MaNguoiDungNavigation.HoTen, e.TrangThai, e.NgayBatDau, e.NgayKetThuc)))
            .ToListAsync(ct);
        var latest = direct.Concat(package).GroupBy(x => (x.CourseId, x.LearnerId))
            .Select(g => g.OrderByDescending(x => x.EnrollmentId).First()).ToList();
        var steps = await db.BuocLoTrinh.AsNoTracking().Where(s =>
            (courseId == null || s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId) &&
            !s.IsArchived && !s.MaLessonNavigation.IsArchived &&
            !s.MaLessonNavigation.MaUnitNavigation.IsArchived)
            .Select(s => new { CourseId = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc, s.MaBuoc })
            .ToListAsync(ct);
        var stepCounts = steps.GroupBy(x => x.CourseId).ToDictionary(g => g.Key, g => g.Count());
        var progress = await db.TienDoBuocLoTrinh.AsNoTracking().Where(p =>
            (courseId == null || p.MaBuocNavigation.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId) &&
            !p.MaBuocNavigation.IsArchived && !p.MaBuocNavigation.MaLessonNavigation.IsArchived &&
            !p.MaBuocNavigation.MaLessonNavigation.MaUnitNavigation.IsArchived)
            .GroupBy(p => new { CourseId = p.MaBuocNavigation.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                p.MaDangKy })
            .Select(g => new { g.Key.CourseId, g.Key.MaDangKy, Rows = g.Count(),
                Completed = g.Count(p => p.TrangThai == "HOAN_THANH"),
                PercentUnits = g.Sum(p => p.TrangThai == "HOAN_THANH" ? 100 :
                    p.TyLeHoanThanh >= 100 ? 99 : (int)(p.TyLeHoanThanh ?? 0)) }).ToListAsync(ct);
        var progressByEnrollment = progress.ToDictionary(x => (x.CourseId, x.MaDangKy));
        var learners = latest.Select(x => {
            var count = stepCounts.GetValueOrDefault(x.CourseId);
            var row = progressByEnrollment.GetValueOrDefault((x.CourseId, x.EnrollmentId));
            var completed = row?.Completed ?? 0;
            var percent = count == 0 ? 0 : (int)Math.Round((row?.PercentUnits ?? 0) / (double)count);
            return new LearnerProgress(x.CourseId, x.EnrollmentId, x.LearnerId, x.LearnerName,
                completed, Math.Max(0, count - completed), percent, row?.Rows > 0,
                count > 0 && completed == count,
                x.Status == "DANG_SU_DUNG" && x.Starts <= now && x.Ends > now);
        }).ToList();
        return (learners, stepCounts, learners.Sum(x => x.Completed), learners.Sum(x => x.Incomplete));
    }

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct)
    {
        var data = await CourseData(null, ct);
        var total = await db.KhoaHoc.AsNoTracking().CountAsync(ct);
        return Ok(new { metrics = new[] {
            Metric("Tổng khóa học", total),
            Metric("Học viên đã đăng ký", data.Learners.Select(x => x.LearnerId).Distinct().Count(), "Không đếm trùng học viên"),
            Metric("Đang học", data.Learners.Where(x => x.Active && x.Started && !x.Finished).Select(x => x.LearnerId).Distinct().Count()),
            Metric("Hoàn thành", data.Learners.Where(x => x.Finished).Select(x => x.LearnerId).Distinct().Count()),
            Metric("Tiến độ trung bình", data.Learners.Count == 0 ? "—" : $"{data.Learners.Average(x => x.Percent):0.#}%")
        } });
    }

    [HttpGet("courses/{courseId:int}")]
    public async Task<IActionResult> Course(int courseId, CancellationToken ct)
    {
        if (!await db.KhoaHoc.AsNoTracking().AnyAsync(x => x.MaKhoaHoc == courseId, ct)) return NotFound();
        var data = await CourseData(courseId, ct);
        var rows = data.Learners.OrderBy(x => x.LearnerName).ToArray();
        var attempts = await (from a in db.LuotBaiTapKhoaHoc.AsNoTracking()
            join step in db.BuocLoTrinh.AsNoTracking() on a.MaBuoc equals step.MaBuoc
            where step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId
            orderby a.BatDauLuc descending
            select new { a.MaLuot, a.MaDangKy, a.MaPhienBan, a.LanLam, a.TrangThai, a.BatDauLuc })
            .Take(20).ToListAsync(ct);
        var attemptIds = attempts.Select(x => x.MaLuot).ToArray();
        var checks = await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking()
            .Where(x => attemptIds.Contains(x.MaLuot))
            .Select(x => new { x.MaLuot, x.MaCauHoi, x.LanKiemTra, x.Dung }).ToListAsync(ct);
        var versions = attempts.Select(x => x.MaPhienBan).Distinct().ToArray();
        var totals = await db.CauHoiBaiTapKhoaHoc.AsNoTracking()
            .Where(x => versions.Contains(x.MaPhienBan))
            .GroupBy(x => x.MaPhienBan).Select(g => new { Version = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Version, x => x.Total, ct);
        var names = rows.GroupBy(x => x.EnrollmentId).ToDictionary(g => g.Key, g => g.First().LearnerName);
        var quiz = attempts.Select(a => {
            var latestChecks = checks.Where(c => c.MaLuot == a.MaLuot).GroupBy(c => c.MaCauHoi)
                .Select(g => g.OrderByDescending(c => c.LanKiemTra).First()).ToArray();
            return new { learner = names.GetValueOrDefault(a.MaDangKy, $"Đăng ký #{a.MaDangKy}"),
                attempt = a.LanLam, status = a.TrangThai, answered = latestChecks.Length,
                total = totals.GetValueOrDefault(a.MaPhienBan), correct = latestChecks.Count(c => c.Dung),
                startedAt = a.BatDauLuc };
        }).ToArray();
        return Ok(new { metrics = new[] {
            Metric("Học viên đăng ký", rows.Length),
            Metric("Đang học", rows.Count(x => x.Active && x.Started && !x.Finished)),
            Metric("Hoàn thành", rows.Count(x => x.Finished)),
            Metric("Tiến độ trung bình", rows.Length == 0 ? "—" : $"{rows.Average(x => x.Percent):0.#}%"),
            Metric("Hoạt động hoàn thành", data.CompletedRecords),
            Metric("Hoạt động chưa xong", data.IncompleteRecords),
            Metric("Bài tập hoàn thành gần đây", attempts.Count(x => x.TrangThai == "HOAN_THANH"), "Trong 20 lượt gần nhất"),
            Metric("Bài tập đang làm gần đây", attempts.Count(x => x.TrangThai != "HOAN_THANH"), "Trong 20 lượt gần nhất")
        }, learners = rows.Select(x => new { x.LearnerId, x.LearnerName, x.Completed,
            x.Incomplete, x.Percent, x.Started, x.Finished, x.Active }),
            quizResults = quiz, totalActivities = data.Steps.GetValueOrDefault(courseId) });
    }

    [HttpGet("questions")]
    public async Task<IActionResult> Questions([FromQuery] string bank, CancellationToken ct)
    {
        if (bank is not ("DE_THI" or "LUYEN_TAP")) return BadRequest(new { message = "Ngân hàng không hợp lệ." });
        var rows = await db.CauHoi.AsNoTracking().Where(q => q.LoaiCauHoi == bank)
            .Select(q => new { id = q.MaCauHoi, part = q.MaPartNavigation.SoPart,
                groupId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
                order = q.NhomCauHoi == null ? 0 : q.NhomCauHoi.ThuTu,
                q.NoiDung, q.GiaiThich, q.PhuongAnA, q.PhuongAnB, q.PhuongAnC,
                q.PhuongAnD, q.PhuongAnDung, q.TrangThai, q.DoKho,
                resourceText = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.NoiDungNguLieu,
                resourceTranslation = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.NoiDungDich,
                audio = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.DuongDanAudio,
                image = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.DuongDanAnh,
                usedInExam = q.CauHoiDeThi.Any() }).ToListAsync(ct);
        var docs = await db.NguLieuTaiLieu.AsNoTracking()
            .Where(d => d.MaNguLieuNavigation.NhomCauHoi.Any(m => m.MaCauHoiNavigation.LoaiCauHoi == bank))
            .ToListAsync(ct);
        var docsByGroup = docs.GroupBy(x => x.MaNguLieu).ToDictionary(g => g.Key, g => g.ToList());
        var groups = rows.Where(x => x.groupId != null)
            .GroupBy(x => x.groupId!.Value).ToArray();
        var invalidGroups = groups.Where(g => !ExamSourceValidator.ValidGroup(g.First().part,
            g.Select(x => new NhomCauHoi { MaNguLieu = g.Key, MaCauHoi = x.id, ThuTu = x.order }).ToList()))
            .Select(g => g.Key).ToHashSet();
        var invalid = rows.Where(q => {
            var translation = q.part == 6 ? QuestionContentRules.Part6Translation(q.resourceTranslation, q.order) : null;
            if (QuestionContentRules.InvalidField(q.part, q.NoiDung, q.GiaiThich, translation) is not null) return true;
            if (!ExamSourceValidator.ValidQuestion(new CauHoi { TrangThai = q.TrangThai,
                PhuongAnA = q.PhuongAnA, PhuongAnB = q.PhuongAnB, PhuongAnC = q.PhuongAnC,
                PhuongAnD = q.PhuongAnD, PhuongAnDung = q.PhuongAnDung }, q.part)) return true;
            if (q.part == 5) return q.groupId != null;
            if (q.groupId is not { } groupId || invalidGroups.Contains(groupId)) return true;
            if (!ExamSourceValidator.ValidMedia(new NguLieu { NoiDungNguLieu = q.resourceText,
                DuongDanAudio = q.audio, DuongDanAnh = q.image }, q.part)) return true;
            return q.part == 7 && !ExamSourceValidator.ValidDocuments(7,
                docsByGroup.GetValueOrDefault(groupId) ?? []);
        }).Select(q => q.id).ToArray();
        var answerRows = bank == "DE_THI" ? await db.ChiTietKetQua.AsNoTracking()
            .Where(a => a.DapAnChon != null && a.MaCauHoiLuotLamNavigation.MaCauHoiGoc != null &&
                a.MaCauHoiLuotLamNavigation.MaKetQuaNavigation.NgayNopBai != null &&
                a.MaCauHoiLuotLamNavigation.MaKetQuaNavigation.DiemTong != null &&
                a.MaCauHoiLuotLamNavigation.MaCauHoiGocNavigation.LoaiCauHoi == bank)
            .GroupBy(a => a.MaCauHoiLuotLamNavigation.MaCauHoiGoc!.Value)
            .Select(g => new { id = g.Key, attempts = g.Count(),
                correct = g.Count(a => a.DapAnChon == a.MaCauHoiLuotLamNavigation.PhuongAnDung) })
            .ToListAsync(ct) : [];
        var answerById = answerRows.ToDictionary(x => x.id);
        var groupedIds = groups.SelectMany(g => g.Select(q => q.id)).ToHashSet();
        var byPart = Enumerable.Range(1, 7).Select(part => {
            var subset = rows.Where(q => q.part == part).ToArray();
            var answers = subset.Sum(q => answerById.GetValueOrDefault(q.id)?.attempts ?? 0);
            var correct = subset.Sum(q => answerById.GetValueOrDefault(q.id)?.correct ?? 0);
            return new { part, questions = subset.Length,
                groups = subset.Where(q => q.groupId != null && groupedIds.Contains(q.id))
                    .Select(q => q.groupId).Distinct().Count(),
                attempts = answers, accuracy = answers == 0 ? (double?)null : 100d * correct / answers };
        }).ToArray();
        return Ok(new { metrics = new[] {
            Metric("Tổng câu hỏi", rows.Count), Metric("Nhóm câu", groups.Length),
            Metric("Câu riêng", rows.Count - groupedIds.Count),
            Metric("Câu trong nhóm", groupedIds.Count),
            Metric("Đang thuộc đề", rows.Count(x => x.usedInExam)),
            Metric("Thiếu nội dung", invalid.Length)
        }, byPart, byDifficulty = rows.GroupBy(x => x.DoKho).OrderBy(g => g.Key)
            .Select(g => new { level = g.Key, count = g.Count() }),
            missingQuestionIds = invalid,
            answerByQuestion = answerRows.Select(x => new { x.id, x.attempts,
                accuracy = x.attempts == 0 ? (double?)null : 100d * x.correct / x.attempts }) });
    }

    [HttpGet("exams")]
    public async Task<IActionResult> Exams(CancellationToken ct)
    {
        var exams = await db.DeThi.AsNoTracking().Where(e => e.LoaiDe == "DE_THI")
            .GroupBy(_ => 1).Select(g => new { total = g.Count(), open = g.Count(e => e.TrangThai == "OPEN"),
                closed = g.Count(e => e.TrangThai == "CLOSE") }).SingleOrDefaultAsync(ct);
        var completed = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaDeThi != null &&
            a.MaDeThiNavigation.LoaiDe == "DE_THI" && a.LoaiBaiLam == ExamCore.Mock &&
            a.NgayNopBai != null && a.DiemTong != null)
            .GroupBy(_ => 1).Select(g => new { total = g.Count(), average = g.Average(a => a.DiemTong) })
            .SingleOrDefaultAsync(ct);
        return Ok(new { metrics = new[] { Metric("Tổng đề thi", exams?.total ?? 0),
            Metric("Đang mở", exams?.open ?? 0), Metric("Đã đóng", exams?.closed ?? 0),
            Metric("Kết quả đã nộp", completed?.total ?? 0, "Theo kết quả còn lưu, không phải mọi lần làm lại"),
            Metric("Điểm trung bình", completed is null ? "—" : $"{completed.average:0.#}") } });
    }

    [HttpGet("exams/{examId:int}")]
    public async Task<IActionResult> Exam(int examId, CancellationToken ct)
    {
        if (!await db.DeThi.AsNoTracking().AnyAsync(e => e.MaDeThi == examId && e.LoaiDe == "DE_THI", ct)) return NotFound();
        var completed = db.KetQuaLamBai.AsNoTracking().Where(a => a.MaDeThi == examId &&
            a.LoaiBaiLam == ExamCore.Mock && a.NgayNopBai != null && a.DiemTong != null);
        var totals = await completed.GroupBy(_ => 1).Select(g => new { count = g.Count(),
            learners = g.Select(a => a.MaHocVien).Distinct().Count(), average = g.Average(a => a.DiemTong),
            highest = g.Max(a => a.DiemTong) }).SingleOrDefaultAsync(ct);
        var attempts = await completed.OrderByDescending(a => a.NgayNopBai)
            .Select(a => new { a.MaHocVien, learner = a.MaHocVienNavigation.HoTen,
                a.DiemTong, a.DiemNghe, a.DiemDoc, a.NgayNopBai }).Take(20).ToListAsync(ct);
        return Ok(new { metrics = new[] {
            Metric("Kết quả đã nộp", totals?.count ?? 0, "Theo kết quả còn lưu"),
            Metric("Học viên đã thi", totals?.learners ?? 0),
            Metric("Điểm trung bình", totals is null ? "—" : $"{totals.average:0.#}"),
            Metric("Điểm cao nhất", totals?.highest?.ToString() ?? "—")
        }, recentAttempts = attempts });
    }

    [HttpGet("vocabulary")]
    public async Task<IActionResult> Vocabulary(CancellationToken ct)
    {
        var word = await db.TuVung.AsNoTracking().GroupBy(_ => 1).Select(g => new {
            total = g.Count(), wordAudio = g.Count(x => x.DuongDanAudio != null),
            exampleAudio = g.Count(x => x.DuongDanAudioViDu != null)
        }).SingleOrDefaultAsync(ct);
        var topics = await db.ChuDe.AsNoTracking().Select(t => new { id = t.MaChuDe,
            name = t.TenChuDe, words = t.TuVungChuDe.Count, hasCover = t.DuongDanAnhBia != null })
            .OrderByDescending(t => t.words).ToListAsync(ct);
        var partsOfSpeech = await db.TuVung.AsNoTracking().GroupBy(w => w.LoaiTu)
            .Select(g => new { name = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToListAsync(ct);
        var progress = await db.TienDoTuVung.AsNoTracking().GroupBy(_ => 1)
            .Select(g => new { records = g.Count(), learners = g.Select(x => x.MaNguoiDung).Distinct().Count(),
                remembered = g.Count(x => x.TrangThai == "DA_NHO") }).SingleOrDefaultAsync(ct);
        return Ok(new { metrics = new[] {
            Metric("Tổng từ vựng", word?.total ?? 0), Metric("Chủ đề", topics.Count),
            Metric("Chủ đề có ảnh", topics.Count(x => x.hasCover), "Ảnh bìa chủ đề; bảng từ chưa có ảnh riêng"),
            Metric("Chủ đề thiếu ảnh", topics.Count(x => !x.hasCover)),
            Metric("Có audio từ", word?.wordAudio ?? 0),
            Metric("Thiếu audio từ", (word?.total ?? 0) - (word?.wordAudio ?? 0)),
            Metric("Có audio ví dụ", word?.exampleAudio ?? 0),
            Metric("Thiếu audio ví dụ", (word?.total ?? 0) - (word?.exampleAudio ?? 0)),
            Metric("Học viên đã học từ", progress?.learners ?? 0),
            Metric("Lượt tiến độ từ", progress?.records ?? 0)
        }, topics, partsOfSpeech });
    }

    [HttpGet("vocabulary/topics/{topicId:int}")]
    public async Task<IActionResult> VocabularyTopic(int topicId, CancellationToken ct)
    {
        if (!await db.ChuDe.AsNoTracking().AnyAsync(x => x.MaChuDe == topicId, ct)) return NotFound();
        var links = await db.TuVungChuDe.AsNoTracking().Where(x => x.MaChuDe == topicId)
            .Select(x => x.MaTuVung).ToArrayAsync(ct);
        var progress = await db.TienDoTuVung.AsNoTracking().Where(x => links.Contains(x.MaTuVung))
            .Select(x => new { x.MaNguoiDung, x.TrangThai }).ToListAsync(ct);
        var practice = await db.LanLuyenTuVung.AsNoTracking().Where(x => x.MaChuDe == topicId)
            .GroupBy(_ => 1).Select(g => new { attempts = g.Count(),
                learners = g.Select(x => x.MaNguoiDung).Distinct().Count() }).SingleOrDefaultAsync(ct);
        return Ok(new { metrics = new[] { Metric("Từ trong chủ đề", links.Length),
            Metric("Học viên có tiến độ", progress.Select(x => x.MaNguoiDung).Distinct().Count()),
            Metric("Từ đã nhớ", progress.Count(x => x.TrangThai == "DA_NHO"), "Tổng bản ghi tiến độ"),
            Metric("Lượt luyện chủ đề", practice?.attempts ?? 0),
            Metric("Học viên đã luyện", practice?.learners ?? 0) } });
    }

    [HttpGet("lectures")]
    public async Task<IActionResult> Lectures([FromQuery] int? courseId, CancellationToken ct)
    {
        if (courseId is <= 0) return BadRequest(new { message = "Mã khóa học không hợp lệ." });
        var lectureIds = courseId is null ? null : await db.BuocLoTrinh.AsNoTracking()
            .Where(s => s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && s.MaBaiGiang != null)
            .Select(s => s.MaBaiGiang!.Value).Distinct().ToArrayAsync(ct);
        var lectures = await db.BaiGiang.AsNoTracking().CountAsync(x => lectureIds == null || lectureIds.Contains(x.MaBaiGiang), ct);
        var resources = await db.TaiNguyenBaiGiang.AsNoTracking()
            .Where(x => !x.IsArchived && (lectureIds == null || lectureIds.Contains(x.MaBaiGiang)))
            .GroupBy(x => x.LoaiTaiNguyen).Select(g => new { kind = g.Key, count = g.Count() }).ToListAsync(ct);
        var completed = await db.TienDoBuocLoTrinh.AsNoTracking().CountAsync(p =>
            p.TrangThai == "HOAN_THANH" && p.MaBuocNavigation.MaBaiGiang != null &&
            (courseId == null || p.MaBuocNavigation.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId), ct);
        var video = resources.Where(x => x.kind == "VIDEO").Sum(x => x.count);
        var pdf = resources.Where(x => x.kind == "PDF").Sum(x => x.count);
        return Ok(new { metrics = new[] { Metric("Bài giảng", lectures), Metric("Video", video),
            Metric("PDF", pdf), Metric("Tài nguyên khác", resources.Sum(x => x.count) - video - pdf),
            Metric("Hoạt động hoàn thành", completed, "Theo bước học, không theo từng tệp") },
            resourceCompletionAvailable = false });
    }
}
