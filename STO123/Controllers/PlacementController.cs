using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.Knn;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/placement")]
public sealed class PlacementController(ToeicDbContext db, ExamAttemptService attempts,
    KnnDiagnosticsStore diagnostics) : ControllerBase
{
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
            var open = await db.KhoaHoc.AsNoTracking().Where(c => c.TrangThai == "DANG_MO")
                .OrderBy(c => c.GiaiDoan).ToListAsync(ct);
            var knn = await CurrentKnn(ct);
            var recommended = CourseRecommendation.RecommendedStages(open, knn?.GiaiDoanDeXuat,
                knn?.DiemMucTieu);
            return Ok(open.Select(c => new { courseId = c.MaKhoaHoc, name = c.TenKhoaHoc,
                stage = c.GiaiDoan, maxTargetScore = c.DiemMucTieuToiDa, description = c.MoTa,
                coverImageUrl = c.DuongDanAnhDaiDien,
                accessStatus = "LOCKED", progress = (object?)null,
                recommended = recommended.Contains(c.GiaiDoan),
                recommendationOrder = recommended.Contains(c.GiaiDoan) ?
                    Array.IndexOf(recommended.ToArray(), c.GiaiDoan) + 1 : (int?)null
            }).ToList());
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
        var course = await db.KhoaHoc.AsNoTracking()
            .Include(c => c.UnitKhoaHoc).ThenInclude(u => u.LessonKhoaHoc)
            .ThenInclude(l => l.BuocLoTrinh)
            .FirstOrDefaultAsync(c => c.MaKhoaHoc == courseId && c.TrangThai == "DANG_MO", ct);
        if (course is null) return NotFound(new { code = "COURSE_NOT_FOUND", message = "Không tìm thấy khóa học đang mở." });
        var knn = await CurrentKnn(ct);
        var recommended = CourseRecommendation.RecommendedStages([course], knn?.GiaiDoanDeXuat,
            knn?.DiemMucTieu);
        var units = course.UnitKhoaHoc.OrderBy(u => u.ThuTu).Select(u => new {
            name = u.TenUnit, description = u.MoTa,
            lessons = u.LessonKhoaHoc.OrderBy(l => l.ThuTu).Select(l => new {
                name = l.TenLesson, description = l.MoTa,
                contents = l.BuocLoTrinh.OrderBy(b => b.ThuTu).Select(b => new {
                    title = b.TieuDe, locked = true
                }).ToList()
            }).ToList()
        }).ToList();
        return Ok(new { courseId = course.MaKhoaHoc, name = course.TenKhoaHoc,
            stage = course.GiaiDoan, description = course.MoTa,
            coverImageUrl = course.DuongDanAnhDaiDien,
            recommended = recommended.Contains(course.GiaiDoan),
            accessStatus = "LOCKED", progress = (object?)null,
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
