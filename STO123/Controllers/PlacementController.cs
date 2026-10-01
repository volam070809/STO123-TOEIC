using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/placement")]
public sealed class PlacementController(ToeicDbContext db, ExamAttemptService attempts) : ControllerBase
{
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new ExamProblem("ATTEMPT_FORBIDDEN", "Phiên đăng nhập không hợp lệ.", 403);

    private async Task<KetQuaLamBai?> Existing(CancellationToken ct) => await db.KetQuaLamBai.AsNoTracking()
        .Where(x => x.MaHocVien == LearnerId && x.LoaiBaiLam == ExamCore.Placement)
        .OrderBy(x => x.NgayLamBai).ThenBy(x => x.MaKetQua).FirstOrDefaultAsync(ct);

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
                var current = await attempts.GetAsync(row.MaKetQua, LearnerId, ct);
                return Ok(new { attemptId = (int?)row.MaKetQua, status = current.Status });
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
            var id = await attempts.StartPlacementAsync(LearnerId, ct);
            var row = await attempts.OwnedAsync(id, LearnerId, ct);
            return Ok(new { attemptId = id, status = row.TrangThai });
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    [HttpGet("result")]
    public async Task<IActionResult> Result(CancellationToken ct)
    {
        try
        {
            var row = await Existing(ct);
            if (row is null) throw new ExamProblem("PLACEMENT_NOT_FOUND", "Chưa có bài phân lớp.", 404);
            var result = await attempts.ResultAsync(row.MaKetQua, LearnerId, ct);
            var knn = await OneKnn(row.MaKetQua, ct);
            var classification = PlacementClassification.From(knn);
            return Ok(new { result, partPercentages = PlacementFeatures.FromFinalizedResult(result),
                classification = classification.Status, stage = classification.Stage,
                modelVersion = classification.ModelVersion, targetScore = classification.TargetScore });
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
            var row = await Existing(ct);
            var knn = row is not null && row.TrangThai is ExamCore.Submitted or ExamCore.Expired ?
                await OneKnn(row.MaKetQua, ct) : null;
            var recommended = CourseRecommendation.RecommendedStages(open, knn?.GiaiDoanDeXuat, knn?.DiemMucTieu);
            return Ok(open.Select(c => new { courseId = c.MaKhoaHoc, name = c.TenKhoaHoc,
                stage = c.GiaiDoan, maxTargetScore = c.DiemMucTieuToiDa, description = c.MoTa,
                recommended = recommended.Contains(c.GiaiDoan) }).ToList());
        }
        catch (ExamProblem e) { return Problem(e); }
    }

    private async Task<KetQuaPhanLopKNN?> OneKnn(int attemptId, CancellationToken ct)
    {
        var rows = await db.KetQuaPhanLopKNN.Where(x => x.MaKetQua == attemptId)
            .OrderBy(x => x.MaPhanLop).Take(2).ToListAsync(ct);
        if (rows.Count > 1)
            throw new ExamProblem("DUPLICATE_KNN_RESULT", "Kết quả phân lớp bị trùng; cần kiểm tra dữ liệu.", 409);
        return rows.FirstOrDefault();
    }
}

public sealed record TargetScoreRequest(int? TargetScore);
