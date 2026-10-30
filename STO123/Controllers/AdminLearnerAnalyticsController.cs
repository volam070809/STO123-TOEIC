using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Admin;
using STO123.Services.Auth;
using STO123.Services.Exam;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/learners/{learnerId:int}/analytics")]
public sealed class AdminLearnerAnalyticsController(ToeicDbContext db, TimeProvider clock) : ControllerBase
{
    private const string PracticeType = "PRACTICE";
    private const string MockType = "MOCK";
    private const string PlacementType = "PLACEMENT";

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(int learnerId, CancellationToken ct)
    {
        var learner = await db.NguoiDung.AsNoTracking()
            .Where(user => user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN")
            .Select(user => new
            {
                user.MaNguoiDung, user.HoTen, user.Email, user.SoDienThoai,
                user.TrangThai, user.NgayTao, user.LanDangNhapCuoi
            }).SingleOrDefaultAsync(ct);
        if (learner is null) return LearnerNotFound();

        var topicsStarted = await db.TuVungChuDe.AsNoTracking()
            .Where(link => link.MaTuVungNavigation.TienDoTuVung.Any(progress =>
                progress.MaNguoiDung == learnerId))
            .Select(link => link.MaChuDe).Distinct().CountAsync(ct);
        var remembered = await db.TienDoTuVung.AsNoTracking()
            .CountAsync(progress => progress.MaNguoiDung == learnerId && progress.TrangThai == "DA_THUOC", ct);
        var practiceAttempts = await db.KetQuaLamBai.AsNoTracking()
            .CountAsync(attempt => attempt.MaHocVien == learnerId && attempt.LoaiBaiLam == PracticeType, ct);
        var completedMocks = await db.KetQuaLamBai.AsNoTracking()
            .CountAsync(attempt => attempt.MaHocVien == learnerId && attempt.LoaiBaiLam == MockType &&
                (attempt.TrangThai == "DA_NOP" || attempt.TrangThai == "HET_GIO"), ct);

        var examActivity = await db.KetQuaLamBai.AsNoTracking()
            .Where(attempt => attempt.MaHocVien == learnerId)
            .OrderByDescending(attempt => attempt.NgayNopBai ?? attempt.NgayLamBai)
            .Take(8).Select(attempt => new AdminLearnerActivityDto(
                attempt.LoaiBaiLam,
                attempt.LoaiBaiLam == PracticeType ? "Luyện tập theo Part" :
                    attempt.LoaiBaiLam == MockType ? "Thi thử TOEIC" : "Kiểm tra đầu vào",
                attempt.TrangThai, attempt.NgayNopBai ?? attempt.NgayLamBai, attempt.MaKetQua))
            .ToListAsync(ct);
        var vocabularyActivity = await db.LanLuyenTuVung.AsNoTracking()
            .Where(attempt => attempt.MaNguoiDung == learnerId)
            .OrderByDescending(attempt => attempt.NgayNopBai ?? attempt.NgayBatDau)
            .Take(8).Select(attempt => new AdminLearnerActivityDto(
                "VOCABULARY", "Luyện từ vựng", attempt.TrangThai,
                attempt.NgayNopBai ?? attempt.NgayBatDau, attempt.MaLanLuyen))
            .ToListAsync(ct);
        var courseActivity = await db.TienDoBuocLoTrinh.AsNoTracking()
            .Where(progress => progress.MaDangKyNavigation.MaNguoiDung == learnerId)
            .OrderByDescending(progress => progress.CapNhatLuc).Take(8)
            .Select(progress => new AdminLearnerActivityDto(
                "COURSE", "Tiến độ khóa học", progress.TrangThai,
                progress.CapNhatLuc, progress.MaTienDoBLT)).ToListAsync(ct);
        var recent = examActivity.Concat(vocabularyActivity).Concat(courseActivity)
            .OrderByDescending(activity => activity.OccurredAt).Take(8).ToList();
        var latestProgress = await db.TienDoTuVung.AsNoTracking()
            .Where(progress => progress.MaNguoiDung == learnerId)
            .MaxAsync(progress => (DateTime?)progress.CapNhatLuc, ct);
        var lastLearning = Latest(recent.Select(activity => (DateTime?)activity.OccurredAt)
            .Append(latestProgress));

        return Ok(new AdminLearnerOverviewDto(
            learner.MaNguoiDung, learner.HoTen, learner.Email, learner.SoDienThoai,
            learner.TrangThai, learner.NgayTao, learner.LanDangNhapCuoi,
            topicsStarted, remembered, practiceAttempts, completedMocks,
            lastLearning, recent));
    }

    [HttpGet("vocabulary")]
    public async Task<IActionResult> Vocabulary(int learnerId, CancellationToken ct)
    {
        if (!await LearnerExists(learnerId, ct)) return LearnerNotFound();

        var topics = await db.ChuDe.AsNoTracking().OrderBy(topic => topic.MaChuDe)
            .Select(topic => new
            {
                TopicId = topic.MaChuDe, Name = topic.TenChuDe, TopicStatus = topic.TrangThai,
                Total = topic.TuVungChuDe.Count,
                Remembered = topic.TuVungChuDe.Count(link => link.MaTuVungNavigation.TienDoTuVung
                    .Any(progress => progress.MaNguoiDung == learnerId && progress.TrangThai == "DA_THUOC")),
                Learning = topic.TuVungChuDe.Count(link => link.MaTuVungNavigation.TienDoTuVung
                    .Any(progress => progress.MaNguoiDung == learnerId && progress.TrangThai == "DANG_HOC")),
                LastProgressAt = topic.TuVungChuDe.SelectMany(link => link.MaTuVungNavigation.TienDoTuVung)
                    .Where(progress => progress.MaNguoiDung == learnerId)
                    .Max(progress => (DateTime?)progress.CapNhatLuc)
            }).ToListAsync(ct);
        var practice = await db.LanLuyenTuVung.AsNoTracking()
            .Where(attempt => attempt.MaNguoiDung == learnerId)
            .GroupBy(attempt => attempt.MaChuDe)
            .Select(group => new
            {
                TopicId = group.Key,
                Attempts = group.Count(),
                Completed = group.Count(attempt => attempt.NgayNopBai != null),
                Correct = group.SelectMany(attempt => attempt.ChiTietLuyenTuVung)
                    .Count(detail => detail.LaDung == true),
                Incorrect = group.SelectMany(attempt => attempt.ChiTietLuyenTuVung)
                    .Count(detail => detail.LaDung == false),
                LastAt = group.Max(attempt => (DateTime?)(attempt.NgayNopBai ?? attempt.NgayBatDau))
            }).ToDictionaryAsync(item => item.TopicId, ct);
        var unique = await db.TienDoTuVung.AsNoTracking()
            .Where(progress => progress.MaNguoiDung == learnerId)
            .GroupBy(_ => 1).Select(group => new
            {
                Started = group.Count(),
                Remembered = group.Count(progress => progress.TrangThai == "DA_THUOC")
            }).SingleOrDefaultAsync(ct);

        var rows = topics.Select(topic =>
        {
            practice.TryGetValue(topic.TopicId, out var result);
            var answered = (result?.Correct ?? 0) + (result?.Incorrect ?? 0);
            var last = Latest([topic.LastProgressAt, result?.LastAt]);
            return new AdminLearnerVocabularyTopicDto(
                topic.TopicId, topic.Name, topic.TopicStatus, topic.Total, topic.Remembered,
                topic.Learning, AdminLearnerAnalyticsMath.Accuracy(topic.Remembered, topic.Total),
                AdminLearnerAnalyticsMath.VocabularyStatus(topic.Total, topic.Remembered, topic.Learning),
                result?.Attempts ?? 0, result?.Completed ?? 0, result?.Correct ?? 0,
                result?.Incorrect ?? 0, AdminLearnerAnalyticsMath.Accuracy(result?.Correct ?? 0, answered), last);
        }).ToList();
        return Ok(new AdminLearnerVocabularyAnalyticsDto(
            unique?.Started ?? 0, unique?.Remembered ?? 0,
            rows.Count(topic => topic.LearningStatus != "NOT_STARTED"), rows));
    }

    [HttpGet("practice")]
    public async Task<IActionResult> Practice(int learnerId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery] int? part = null, CancellationToken ct = default)
    {
        if (!ValidPage(page, pageSize) || part is < 1 or > 7)
            return BadRequest(new { code = "INVALID_PRACTICE_FILTER", message = "Bộ lọc lịch sử luyện tập không hợp lệ." });
        if (!await LearnerExists(learnerId, ct)) return LearnerNotFound();

        var query = db.KetQuaLamBai.AsNoTracking().Where(attempt =>
            attempt.MaHocVien == learnerId && attempt.LoaiBaiLam == PracticeType);
        var metrics = await PracticeMetrics(query).ToListAsync(ct);
        var partRows = Enumerable.Range(1, 7).Select(number =>
        {
            var rows = metrics.Where(metric => metric.Part == number).ToList();
            var total = rows.Sum(metric => metric.Total);
            var answered = rows.Sum(metric => metric.Answered);
            var correct = rows.Sum(metric => metric.Correct);
            return new AdminLearnerPracticePartDto(number, rows.Count,
                rows.Count(metric => metric.CompletedAt.HasValue), correct, answered - correct,
                total - answered, AdminLearnerAnalyticsMath.Accuracy(correct, total),
                rows.Count == 0 ? null : rows.Max(metric => (DateTime?)metric.StartedAt));
        }).ToList();

        if (part.HasValue) query = query.Where(attempt => attempt.CauHoiLuotLam
            .Any(question => question.MaPartNavigation.SoPart == part.Value));
        var totalItems = await query.CountAsync(ct);
        var orderedHistory = query.OrderByDescending(attempt => attempt.NgayLamBai)
            .ThenByDescending(attempt => attempt.MaKetQua);
        var history = await PracticeMetrics(orderedHistory)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = history.Select(metric => new AdminLearnerPracticeAttemptDto(
            metric.AttemptId, metric.Part == 0 ? null : metric.Part, metric.StartedAt,
            metric.CompletedAt, metric.Status, null, null, metric.Total, metric.Correct,
            metric.Answered - metric.Correct, metric.Total - metric.Answered,
            AdminLearnerAnalyticsMath.Accuracy(metric.Correct, metric.Total))).ToList();
        return Ok(new AdminLearnerPracticeAnalyticsDto(partRows, items, totalItems, page,
            pageSize, Pages(totalItems, pageSize)));
    }

    [HttpGet("exams")]
    public async Task<IActionResult> Exams(int learnerId, [FromQuery] int mockPage = 1,
        [FromQuery] int placementPage = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (!ValidPage(mockPage, pageSize) || !ValidPage(placementPage, pageSize))
            return BadRequest(new { code = "INVALID_EXAM_FILTER", message = "Trang lịch sử bài thi không hợp lệ." });
        if (!await LearnerExists(learnerId, ct)) return LearnerNotFound();

        var mock = await ExamHistory(learnerId, MockType, mockPage, pageSize, ct);
        var placement = await ExamHistory(learnerId, PlacementType, placementPage, pageSize, ct);
        return Ok(new AdminLearnerExamAnalyticsDto(mock, placement));
    }

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(int learnerId, CancellationToken ct)
    {
        if (!await LearnerExists(learnerId, ct)) return LearnerNotFound();
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var activeCourseIds = await CourseEntitlement.ActiveCourseIds(db, learnerId, now).ToListAsync(ct);
        var enrollmentRows = await db.DangKyGoiHoc.AsNoTracking()
            .Where(enrollment => enrollment.MaNguoiDung == learnerId)
            .OrderByDescending(enrollment => enrollment.NgayDangKy)
            .Include(enrollment => enrollment.MaGoiHocNavigation).ThenInclude(package => package.MaKhoaHoc)
            .Include(enrollment => enrollment.ChiTietDangKyKhoaHoc).ThenInclude(detail => detail.MaKhoaHocNavigation)
            .Include(enrollment => enrollment.GiaoDichThanhToan)
            .AsSplitQuery().ToListAsync(ct);

        var rows = enrollmentRows.Select(enrollment =>
        {
            var successful = enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG");
            var pending = enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "CHO_XU_LY");
            var failed = enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THAT_BAI");
            var paymentStatus = successful ? "SUCCESS" : pending ? "PENDING" : failed ? "FAILED" : "NO_PAYMENT";
            var packageCourses = enrollment.MaGoiHocNavigation?.MaKhoaHoc.Select(course =>
                new { Course = course, Price = (decimal?)null }) ?? [];
            var directCourses = enrollment.ChiTietDangKyKhoaHoc.Select(detail =>
                new { Course = detail.MaKhoaHocNavigation, Price = (decimal?)detail.DonGia });
            var courses = packageCourses.Concat(directCourses).DistinctBy(item => item.Course.MaKhoaHoc)
                .OrderBy(item => item.Course.GiaiDoan).Select(item =>
                {
                    var hasAccess = activeCourseIds.Contains(item.Course.MaKhoaHoc);
                    var accessStatus = hasAccess ? "ACTIVE" :
                        paymentStatus == "PENDING" ? "PAYMENT_PENDING" :
                        successful && enrollment.NgayKetThuc <= now ? "EXPIRED" : "INACTIVE";
                    return new AdminLearnerAccessCourseDto(item.Course.MaKhoaHoc, item.Course.TenKhoaHoc,
                        item.Course.GiaiDoan, item.Course.TrangThai, item.Price, hasAccess, accessStatus);
                }).ToList();
            return new AdminLearnerAccessEnrollmentDto(enrollment.MaDangKy,
                enrollment.MaGoiHoc.HasValue ? "PACKAGE" : "DIRECT_COURSE",
                enrollment.MaGoiHoc, enrollment.MaGoiHocNavigation?.TenGoiHoc,
                enrollment.GiaDangKy, enrollment.SoNgaySuDung, enrollment.NgayDangKy,
                enrollment.NgayBatDau, enrollment.NgayKetThuc, enrollment.TrangThai,
                paymentStatus, courses);
        }).ToList();
        return Ok(new AdminLearnerCourseAnalyticsDto(rows.Count,
            rows.SelectMany(row => row.Courses).Where(course => course.HasCurrentAccess)
                .Select(course => course.CourseId).Distinct().Count(), rows));
    }

    private async Task<AdminLearnerExamHistoryDto> ExamHistory(int learnerId, string type,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.KetQuaLamBai.AsNoTracking().Where(attempt =>
            attempt.MaHocVien == learnerId && attempt.LoaiBaiLam == type);
        var all = await ExamMetrics(query).ToListAsync(ct);
        var totalItems = all.Count;
        var completed = all.Where(metric => AdminLearnerAnalyticsMath.IsCompletedExam(metric.Status)).ToList();
        var completedTotal = completed.Sum(metric => metric.Total);
        var completedAnswered = completed.Sum(metric => metric.Answered);
        var completedCorrect = completed.Sum(metric => metric.Correct);
        var latestScore = completed.Where(metric => metric.TotalScore.HasValue)
            .OrderByDescending(metric => metric.CompletedAt ?? metric.StartedAt)
            .Select(metric => metric.TotalScore).FirstOrDefault();
        var summary = new AdminLearnerExamSummaryDto(totalItems, completed.Count,
            completedCorrect, completedAnswered - completedCorrect,
            completedTotal - completedAnswered,
            AdminLearnerAnalyticsMath.Accuracy(completedCorrect, completedTotal), latestScore);

        var orderedPage = query.OrderByDescending(attempt => attempt.NgayLamBai)
            .ThenByDescending(attempt => attempt.MaKetQua);
        var pageRows = await ExamMetrics(orderedPage).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var ids = pageRows.Select(metric => metric.AttemptId).ToArray();
        var partRows = ids.Length == 0 ? [] : await db.CauHoiLuotLam.AsNoTracking()
            .Where(question => ids.Contains(question.MaKetQua))
            .GroupBy(question => new { question.MaKetQua, Part = question.MaPartNavigation.SoPart })
            .Select(group => new ExamPartMetric(group.Key.MaKetQua, group.Key.Part, group.Count(),
                group.Count(question => question.ChiTietKetQua != null && question.ChiTietKetQua.DapAnChon != null),
                group.Count(question => question.ChiTietKetQua != null &&
                    question.ChiTietKetQua.DapAnChon == question.PhuongAnDung)))
            .ToListAsync(ct);
        var items = pageRows.Select(metric =>
        {
            var parts = partRows.Where(part => part.AttemptId == metric.AttemptId).OrderBy(part => part.Part)
                .Select(part => new AdminLearnerExamPartDto(part.Part, part.Total, part.Correct,
                    part.Answered - part.Correct, part.Total - part.Answered,
                    AdminLearnerAnalyticsMath.Accuracy(part.Correct, part.Total))).ToList();
            return new AdminLearnerExamAttemptDto(metric.AttemptId, type, metric.Status,
                metric.ExamName, metric.StartedAt, metric.CompletedAt, metric.Total,
                metric.Correct, metric.Answered - metric.Correct, metric.Total - metric.Answered,
                AdminLearnerAnalyticsMath.Accuracy(metric.Correct, metric.Total),
                metric.ListeningScore, metric.ReadingScore, metric.TotalScore,
                metric.RecommendedStage, metric.TargetScore, parts);
        }).ToList();
        return new AdminLearnerExamHistoryDto(summary, items, totalItems, page, pageSize,
            Pages(totalItems, pageSize));
    }

    private IQueryable<PracticeMetric> PracticeMetrics(IQueryable<KetQuaLamBai> query) =>
        query.Select(attempt => new PracticeMetric(attempt.MaKetQua, attempt.TrangThai,
            attempt.NgayLamBai, attempt.NgayNopBai,
            attempt.CauHoiLuotLam.OrderBy(question => question.ThuTu)
                .Select(question => question.MaPartNavigation.SoPart).FirstOrDefault(),
            attempt.CauHoiLuotLam.Count,
            attempt.CauHoiLuotLam.Count(question => question.ChiTietKetQua != null &&
                question.ChiTietKetQua.DapAnChon != null),
            attempt.CauHoiLuotLam.Count(question => question.ChiTietKetQua != null &&
                question.ChiTietKetQua.DapAnChon == question.PhuongAnDung)));

    private IQueryable<ExamMetric> ExamMetrics(IQueryable<KetQuaLamBai> query) =>
        query.Select(attempt => new ExamMetric(attempt.MaKetQua, attempt.TrangThai,
            attempt.MaDeThiNavigation == null ? null : attempt.MaDeThiNavigation.TenDe,
            attempt.NgayLamBai, attempt.NgayNopBai, attempt.CauHoiLuotLam.Count,
            attempt.CauHoiLuotLam.Count(question => question.ChiTietKetQua != null &&
                question.ChiTietKetQua.DapAnChon != null),
            attempt.CauHoiLuotLam.Count(question => question.ChiTietKetQua != null &&
                question.ChiTietKetQua.DapAnChon == question.PhuongAnDung),
            attempt.DiemNghe, attempt.DiemDoc, attempt.DiemTong,
            attempt.KetQuaPhanLopKNN.OrderByDescending(result => result.NgayPhanLop)
                .Select(result => (byte?)result.GiaiDoanDeXuat).FirstOrDefault(),
            attempt.KetQuaPhanLopKNN.OrderByDescending(result => result.NgayPhanLop)
                .Select(result => result.DiemMucTieu).FirstOrDefault()));

    private Task<bool> LearnerExists(int learnerId, CancellationToken ct) =>
        db.NguoiDung.AsNoTracking().AnyAsync(user => user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN", ct);

    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 5 and <= 50;
    private static int Pages(int total, int pageSize) => total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
    private static DateTime? Latest(IEnumerable<DateTime?> values)
    {
        var dates = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return dates.Count == 0 ? null : dates.Max();
    }
    private NotFoundObjectResult LearnerNotFound() => NotFound(new
        { code = "LEARNER_NOT_FOUND", message = "Không tìm thấy học viên." });

    private sealed record PracticeMetric(int AttemptId, string Status, DateTime StartedAt,
        DateTime? CompletedAt, int Part, int Total, int Answered, int Correct);
    private sealed record ExamMetric(int AttemptId, string Status, string? ExamName,
        DateTime StartedAt, DateTime? CompletedAt, int Total, int Answered, int Correct,
        int? ListeningScore, int? ReadingScore, int? TotalScore,
        byte? RecommendedStage, int? TargetScore);
    private sealed record ExamPartMetric(int AttemptId, int Part, int Total, int Answered, int Correct);
}
