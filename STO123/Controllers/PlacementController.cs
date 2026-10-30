using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.Knn;
using STO123.Services.Payment;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/placement")]
public sealed class PlacementController(ToeicDbContext db, ExamAttemptService attempts,
    KnnDiagnosticsStore diagnostics, TimeProvider clock) : ControllerBase
{
    public static decimal CourseProgressPercent(int completedUnits, int totalUnits) => totalUnits <= 0 ? 0 :
        Math.Round(100m * completedUnits / totalUnits, 2, MidpointRounding.AwayFromZero);
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new ExamProblem("ATTEMPT_FORBIDDEN", "Phiên đăng nhập không hợp lệ.", 403);

    private async Task<KetQuaLamBai?> Existing(CancellationToken ct) => await db.KetQuaLamBai.AsNoTracking()
        .Where(x => x.MaHocVien == LearnerId && x.LoaiBaiLam == ExamCore.Placement)
        .OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua).FirstOrDefaultAsync(ct);

    private Task<KetQuaLamBai?> LatestCompleted(CancellationToken ct) => db.KetQuaLamBai.AsNoTracking()
        .Where(x => x.MaHocVien == LearnerId && x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
        .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua).FirstOrDefaultAsync(ct);

    private static IActionResult Problem(ExamProblem e) => new ObjectResult(new { code = e.Code, message = e.Message })
        { StatusCode = e.Status };

    [HttpGet]
    public async Task<IActionResult> State(CancellationToken ct)
    {
        try
        {
            var row = await Existing(ct);
            if (row is null) return Ok(new { attemptId = (int?)null, status = "NOT_STARTED" });
            if (row.TrangThai is ExamCore.Active or "BO_DO")
            {
                var counts = await db.CauHoiLuotLam.AsNoTracking().Where(q => q.MaKetQua == row.MaKetQua)
                    .GroupBy(q => q.MaKetQua).Select(g => new { Total = g.Count(),
                        Answered = g.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null) })
                    .FirstOrDefaultAsync(ct);
                return Ok(new { attemptId = (int?)row.MaKetQua, status = row.TrangThai,
                    totalQuestions = counts?.Total ?? 0, answered = counts?.Answered ?? 0,
                    remainingSeconds = ExamTimer.Remaining(row, DateTime.UtcNow),
                    isPaused = row.BatDauPhienLuc is null || ExamTimer.IsStale(row, DateTime.UtcNow) });
            }
            return Ok(new { attemptId = (int?)row.MaKetQua, status = row.TrangThai });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        try
        {
            var started = await attempts.StartPlacementAsync(LearnerId, ct);
            return Ok(new { attemptId = started.AttemptId, status = started.Status });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpGet("result")]
    public async Task<IActionResult> Result(CancellationToken ct)
    {
        try
        {
            var row = await LatestCompleted(ct);
            if (row is null) throw new ExamProblem("PLACEMENT_NOT_FOUND", "Chưa có bài phân lớp.", 404);
            var result = await attempts.ResultAsync(row.MaKetQua, LearnerId, ct);
            var knn = await CurrentKnn(ct);
            var classification = PlacementClassification.From(knn?.MaKetQua == row.MaKetQua ? knn : null);
            var percentages = PlacementFeatures.FromFinalizedResult(result);
            var diagnostic = diagnostics.Get(row.MaKetQua);
            var analysis = PlacementAnalysis.From(result);
            return Ok(new { result, partPercentages = percentages,
                classification = classification.Status, stage = classification.Stage,
                modelVersion = classification.ModelVersion, targetScore = classification.TargetScore,
                summary = analysis.Summary, parts = analysis.Parts,
                knn = diagnostic is null ? null : new { modelVersion = diagnostic.ModelVersion,
                    k = diagnostic.K, stage1Votes = diagnostic.Stage1Votes,
                    stage2Votes = diagnostic.Stage2Votes, stage3Votes = diagnostic.Stage3Votes,
                    winnerVotes = diagnostic.WinnerVotes } });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpPut("target")]
    public async Task<IActionResult> Target([FromBody] TargetScoreRequest request, CancellationToken ct)
    {
        try
        {
            var result = await attempts.UpdatePlacementTargetAsync(LearnerId, request.TargetScore, ct);
            return Ok(new { targetScore = result.TargetScore, stage = result.Stage,
                modelVersion = result.ModelVersion });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct)
    {
        try
        {
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            var entitledCourseIds = CourseEntitlement.ActiveCourseIds(db, LearnerId, now);
            var rows = await db.KhoaHoc.AsNoTracking().Where(c => c.TrangThai == "DANG_MO")
                .OrderBy(c => c.GiaiDoan)
                .Select(c => new { Course = c, HasAccess = entitledCourseIds.Contains(c.MaKhoaHoc) })
                .ToListAsync(ct);
            var enrollmentRows = await db.DangKyGoiHoc.AsNoTracking()
                .Where(e => e.MaNguoiDung == LearnerId)
                .Include(e => e.MaGoiHocNavigation).ThenInclude(p => p.MaKhoaHoc)
                .Include(e => e.ChiTietDangKyKhoaHoc)
                .Include(e => e.GiaoDichThanhToan).ToListAsync(ct);
            var enrollments = enrollmentRows.SelectMany(e =>
                (e.MaGoiHoc.HasValue ? e.MaGoiHocNavigation.MaKhoaHoc.Select(c => c.MaKhoaHoc) :
                    e.ChiTietDangKyKhoaHoc.Select(c => c.MaKhoaHoc)).Select(courseId => new {
                CourseId = courseId, e.TrangThai, e.NgayBatDau, e.NgayKetThuc,
                Pending = e.GiaoDichThanhToan.Any(p => p.TrangThai == "CHO_XU_LY"),
                Paid = e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG"),
                Failed = e.GiaoDichThanhToan.Any(p => p.TrangThai == "THAT_BAI")
            })).ToList();
            var open = rows.Select(row => row.Course).ToList();
            var knn = await CurrentKnn(ct);
            var recommended = CourseRecommendation.RecommendedStages(open, knn?.GiaiDoanDeXuat,
                knn?.DiemMucTieu);
            return Ok(rows.Select(row => { var c = row.Course;
                var related = enrollments.Where(e => e.CourseId == c.MaKhoaHoc).ToList();
                var accessUntil = row.HasAccess ? related.Where(e => e.Paid &&
                    e.TrangThai == "DANG_SU_DUNG" && e.NgayBatDau <= now && e.NgayKetThuc > now)
                    .Max(e => e.NgayKetThuc) : related.Where(e => e.Paid).Max(e => e.NgayKetThuc);
                var accessStatus = row.HasAccess ? "ACTIVE" :
                    related.Any(e => e.Pending && e.TrangThai == "CHO_THANH_TOAN") ? "PAYMENT_PENDING" :
                    related.Any(e => e.Paid && e.NgayKetThuc <= now) ? "EXPIRED" :
                    related.Any(e => e.Failed) ? "PAYMENT_FAILED" : "NOT_PURCHASED";
                return new { courseId = c.MaKhoaHoc, name = c.TenKhoaHoc,
                stage = c.GiaiDoan, maxTargetScore = c.DiemMucTieuToiDa, description = c.MoTa,
                retailPrice = c.GiaBanLe, accessDays = PaymentCreationService.CourseValidDays,
                coverImageUrl = c.DuongDanAnhDaiDien,
                accessStatus, accessExpiresAt = accessUntil, progress = (object?)null,
                recommended = recommended.Contains(c.GiaiDoan),
                recommendationOrder = recommended.Contains(c.GiaiDoan) ?
                    Array.IndexOf(recommended.ToArray(), c.GiaiDoan) + 1 : (int?)null
            }; }).ToList());
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var query = db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired));
        var completedCount = await query.CountAsync(ct);
        var latestCompletedAt = await query.OrderByDescending(x => x.NgayLamBai)
            .ThenByDescending(x => x.MaKetQua).Select(x => x.NgayNopBai).FirstOrDefaultAsync(ct);
        var current = await CurrentKnn(ct);
        return Ok(new { completedCount,
            latestCompletedAt = latestCompletedAt.HasValue ? ExamCore.Utc(latestCompletedAt.Value) : (DateTime?)null,
            currentStage = current?.GiaiDoanDeXuat, targetScore = current?.DiemMucTieu });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 20) return BadRequest(new { code = "INVALID_PAGE" });
        var query = db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Placement &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { attemptId = x.MaKetQua, completedAt = x.NgayNopBai,
                status = x.TrangThai, stage = x.GiaiDoanLucNop }).ToListAsync(ct);
        return Ok(new { items = rows.Select((row, index) => new { row.attemptId,
            number = total - (page - 1) * pageSize - index,
            completedAt = row.completedAt.HasValue ? ExamCore.Utc(row.completedAt.Value) : (DateTime?)null,
            row.status, row.stage }),
            total, page, pageSize, hasMore = page * pageSize < total });
    }

    [HttpGet("courses/{courseId:int}")]
    public async Task<IActionResult> CourseDetail(int courseId, CancellationToken ct)
    {
        try
        {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var hasAccess = await CourseEntitlement.ActiveCourseIds(db, LearnerId, now)
            .AnyAsync(id => id == courseId, ct);
        if (!hasAccess) return StatusCode(403, new { code = "COURSE_LOCKED",
            message = "Khóa học đang khóa. Vui lòng mua khóa học để xem nội dung." });
        var course = await db.KhoaHoc.AsNoTracking()
            .Include(c => c.UnitKhoaHoc).ThenInclude(u => u.LessonKhoaHoc)
            .ThenInclude(l => l.BuocLoTrinh)
            .FirstOrDefaultAsync(c => c.MaKhoaHoc == courseId && c.TrangThai == "DANG_MO", ct);
        if (course is null) return NotFound(new { code = "COURSE_NOT_FOUND", message = "Không tìm thấy khóa học đang mở." });
        var knn = await CurrentKnn(ct);
        var recommended = CourseRecommendation.RecommendedStages([course], knn?.GiaiDoanDeXuat,
            knn?.DiemMucTieu);
        var progressRows = await db.TienDoBuocLoTrinh.AsNoTracking()
            .Where(p => p.MaDangKyNavigation.MaNguoiDung == LearnerId && p.MaBuocNavigation.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)
            .Select(p => new { p.MaBuoc, p.TrangThai }).ToListAsync(ct);
        var completed = progressRows.Where(p => p.TrangThai == "HOAN_THANH").Select(p => p.MaBuoc).ToHashSet();
        var started = progressRows.Where(p => p.TrangThai == "DANG_LAM").Select(p => p.MaBuoc).ToHashSet();
        var exerciseSteps = course.UnitKhoaHoc.Where(u => !u.IsArchived)
            .SelectMany(u => u.LessonKhoaHoc.Where(l => !l.IsArchived))
            .SelectMany(l => l.BuocLoTrinh.Where(s => !s.IsArchived)).Where(s => s.MaBaiTap != null).ToList();
        var stepIds = exerciseSteps.Select(s => s.MaBuoc).ToArray();
        var exerciseIds = exerciseSteps.Select(s => s.MaBaiTap!.Value).Distinct().ToArray();
        var enrollmentIds = await db.DangKyGoiHoc.AsNoTracking().Where(e => e.MaNguoiDung == LearnerId)
            .Select(e => e.MaDangKy).ToArrayAsync(ct);
        var attempts = stepIds.Length == 0 ? new List<LuotBaiTapKhoaHoc>() : await db.LuotBaiTapKhoaHoc.AsNoTracking()
            .Where(a => enrollmentIds.Contains(a.MaDangKy) && stepIds.Contains(a.MaBuoc)).ToListAsync(ct);
        var attemptIds = attempts.Select(a => a.MaLuot).ToArray();
        var checkedRows = attemptIds.Length == 0 ? new List<LanKiemTraBaiTapKhoaHoc>() : await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking()
            .Where(c => attemptIds.Contains(c.MaLuot)).ToListAsync(ct);
        var versions = exerciseIds.Length == 0 ? new List<PhienBanBaiTapKhoaHoc>() : await db.PhienBanBaiTapKhoaHoc.AsNoTracking()
            .Where(v => exerciseIds.Contains(v.MaBaiTap) && v.TrangThai == "XUAT_BAN")
            .ToListAsync(ct);
        var versionIds = versions.Select(v => v.MaPhienBan).Concat(attempts.Select(a => a.MaPhienBan)).Distinct().ToArray();
        var questionCounts = versionIds.Length == 0 ? new Dictionary<int, int>() : await db.CauHoiBaiTapKhoaHoc.AsNoTracking()
            .Where(q => versionIds.Contains(q.MaPhienBan)).GroupBy(q => q.MaPhienBan)
            .Select(g => new { VersionId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.VersionId, x => x.Count, ct);
        (int done, int total) Units(BuocLoTrinh step)
        {
            if (step.MaDeThi is not null) return (0, 0);
            if (step.MaBaiTap is not { } exerciseId) return (completed.Contains(step.MaBuoc) ? 1 : 0, 1);
            var published = versions.Where(v => v.MaBaiTap == exerciseId).OrderByDescending(v => v.SoPhienBan).FirstOrDefault();
            var versionId = published?.MaPhienBan ?? 0;
            var total = questionCounts.GetValueOrDefault(versionId);
            var currentAttemptIds = attempts.Where(a => a.MaBuoc == step.MaBuoc && a.MaPhienBan == versionId)
                .Select(a => a.MaLuot).ToHashSet();
            var done = checkedRows.Where(c => currentAttemptIds.Contains(c.MaLuot))
                .Select(c => c.MaCauHoi).Distinct().Count();
            return (Math.Min(done, total), total);
        }
        string Status(BuocLoTrinh step)
        {
            var units = Units(step);
            if (step.MaBaiTap is not null)
            {
                var currentVersion = versions.Where(v => v.MaBaiTap == step.MaBaiTap)
                    .OrderByDescending(v => v.SoPhienBan).FirstOrDefault()?.MaPhienBan;
                return units.total > 0 && units.done == units.total ? "HOAN_THANH" :
                    units.done > 0 || attempts.Any(a => a.MaBuoc == step.MaBuoc &&
                        a.MaPhienBan == currentVersion && a.TrangThai == "DANG_LAM") ? "DANG_LAM" : "CHUA_LAM";
            }
            return completed.Contains(step.MaBuoc) ? "HOAN_THANH" :
                started.Contains(step.MaBuoc) ? "DANG_LAM" : "CHUA_LAM";
        }
        static decimal Percent(int done, int total) => CourseProgressPercent(done, total);
        var units = course.UnitKhoaHoc.Where(u => !u.IsArchived).OrderBy(u => u.ThuTu).Select(u => new {
            id = u.MaUnit, name = u.TenUnit, description = u.MoTa,
            completedUnits = u.LessonKhoaHoc.Where(l => !l.IsArchived).Sum(l => l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).done)),
            totalUnits = u.LessonKhoaHoc.Where(l => !l.IsArchived).Sum(l => l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).total)),
            progress = Percent(u.LessonKhoaHoc.Where(l => !l.IsArchived).Sum(l => l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).done)),
                u.LessonKhoaHoc.Where(l => !l.IsArchived).Sum(l => l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).total))),
            lessons = u.LessonKhoaHoc.Where(l => !l.IsArchived).OrderBy(l => l.ThuTu).Select(l => new {
                id = l.MaLesson, name = l.TenLesson, description = l.MoTa,
                completedUnits = l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).done),
                totalUnits = l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).total),
                progress = Percent(l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).done), l.BuocLoTrinh.Where(b => !b.IsArchived).Sum(b => Units(b).total)),
                contents = l.BuocLoTrinh.Where(b => !b.IsArchived).OrderBy(b => b.ThuTu).Select(b => new {
                    id = b.MaBuoc, title = b.TieuDe,
                    completedUnits = Units(b).done, totalUnits = Units(b).total,
                    progress = Percent(Units(b).done, Units(b).total),
                    progressStatus = Status(b),
                    type = b.MaBaiGiang != null ? "LECTURE" : b.MaChuDe != null ? "VOCABULARY" : b.MaBaiTap != null ? "EXERCISE" : "EXAM",
                    locked = b.MaDeThi != null || b.MaBaiTap != null &&
                        !db.PhienBanBaiTapKhoaHoc.Any(v => v.MaBaiTap == b.MaBaiTap && v.TrangThai == "XUAT_BAN")
                }).ToList()
            }).ToList()
        }).ToList();
        return Ok(new { courseId = course.MaKhoaHoc, name = course.TenKhoaHoc,
            stage = course.GiaiDoan, maxTargetScore = course.DiemMucTieuToiDa, description = course.MoTa,
            coverImageUrl = course.DuongDanAnhDaiDien,
            recommended = recommended.Contains(course.GiaiDoan),
            accessStatus = hasAccess ? "ACTIVE" : "LOCKED",
            completedUnits = units.Sum(u => u.lessons.Sum(l => l.completedUnits)),
            totalUnits = units.Sum(u => u.lessons.Sum(l => l.totalUnits)),
            progress = Percent(units.Sum(u => u.lessons.Sum(l => l.completedUnits)),
                units.Sum(u => u.lessons.Sum(l => l.totalUnits))),
            contentCount = units.Sum(u => u.lessons.Sum(l => l.contents.Count)), units });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    private async Task<KetQuaPhanLopKNN?> CurrentKnn(CancellationToken ct)
    {
        var rows = await db.KetQuaPhanLopKNN.AsNoTracking()
            .Where(x => x.MaKetQuaNavigation.MaHocVien == LearnerId)
            .OrderBy(x => x.MaPhanLop).Take(2).ToListAsync(ct);
        if (rows.Count > 1)
            throw new ExamProblem("DUPLICATE_KNN_RESULT", "Kết quả phân lớp bị trùng; cần kiểm tra dữ liệu.", 409);
        return rows.FirstOrDefault();
    }
}

public sealed record TargetScoreRequest(int? TargetScore);
