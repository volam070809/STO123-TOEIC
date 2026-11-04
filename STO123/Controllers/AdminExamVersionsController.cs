using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG")]
[ApiController]
[Route("api/admin/content/exams/{examId:int}/versions")]
public sealed class AdminExamVersionsController(ToeicDbContext db, ExamVersionStore versions) : ControllerBase
{
    public sealed record PublishRequest(int? ExpectedCurrentVersionId, Guid RequestKey);
    public sealed record PublishSelectionRequest(int? ExpectedCurrentVersionId, Guid RequestKey,
        List<int> OrderedQuestionIds);
    public sealed record StatusRequest(string Status);
    public sealed record RestoreRequest(int ExpectedCurrentVersionId, Guid RequestKey);

    [HttpGet("/api/admin/content/exams")]
    public async Task<IActionResult> Exams(CancellationToken ct)
    {
        var rows = await db.DeThi.AsNoTracking().Where(e => e.LoaiDe == "DE_THI")
            .OrderBy(e => e.MaDeThi).Select(e => new {
                examId = e.MaDeThi, name = e.TenDe, status = e.TrangThai,
                duration = e.ThoiGianLamBai, currentVersionId = e.MaPhienBanHienHanh,
                questionCount = e.MaPhienBanHienHanh == null ? e.CauHoiDeThi.Count :
                    db.PhienBanDeThi.Where(v => v.MaPhienBanDeThi == e.MaPhienBanHienHanh)
                        .Select(v => v.SoCau).FirstOrDefault()
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPut("/api/admin/content/exams/{examId:int}/status")]
    public async Task<IActionResult> Status(int examId, [FromBody] StatusRequest request, CancellationToken ct)
    {
        if (request.Status is not ("OPEN" or "CLOSE"))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái phải là OPEN hoặc CLOSE." });
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var exam = await db.DeThi.FromSqlInterpolated(
            $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
            .SingleOrDefaultAsync(ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND", message = "Không tìm thấy đề thi." });
        if (request.Status == "OPEN" && exam.MaPhienBanHienHanh is null)
            return Conflict(new { code = "VERSION_REQUIRED",
                message = "Cần xuất bản phiên bản đề hợp lệ trước khi mở đề." });
        exam.TrangThai = request.Status;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { examId, status = exam.TrangThai });
    }

    [HttpGet]
    public async Task<IActionResult> List(int examId, CancellationToken ct)
    {
        var exam = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == examId)
            .Select(e => new { e.MaDeThi, e.TenDe, e.TrangThai, e.MaPhienBanHienHanh })
            .SingleOrDefaultAsync(ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND", message = "Không tìm thấy đề thi." });
        var rows = await db.PhienBanDeThi.AsNoTracking().Where(v => v.MaDeThi == examId)
            .OrderByDescending(v => v.SoPhienBan)
            .Select(v => new { v.MaPhienBanDeThi, v.SoPhienBan, v.NgayXuatBan,
                v.SoCau, v.ThoiGianLamBai, v.TenDe, v.MaNguoiXuatBan })
            .ToListAsync(ct);
        return Ok(new { exam, versions = rows });
    }

    [HttpGet("{versionId:int}/preview")]
    public async Task<IActionResult> Preview(int examId, int versionId, CancellationToken ct)
    {
        var row = await db.PhienBanDeThi.AsNoTracking().SingleOrDefaultAsync(
            v => v.MaDeThi == examId && v.MaPhienBanDeThi == versionId, ct);
        if (row is null) return NotFound(new { code = "VERSION_NOT_FOUND", message = "Không tìm thấy phiên bản đề." });
        try { return Ok(await versions.ReadAsync(row, ct)); }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("current-source-status")]
    public async Task<IActionResult> CurrentSourceStatus(int examId, CancellationToken ct)
    {
        var exam = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == examId)
            .Select(e => e.MaPhienBanHienHanh).SingleOrDefaultAsync(ct);
        if (exam is null) return NotFound(new { code = "VERSION_NOT_FOUND",
            message = "Đề chưa có phiên bản hiện hành." });
        var version = await db.PhienBanDeThi.AsNoTracking()
            .SingleAsync(v => v.MaDeThi == examId && v.MaPhienBanDeThi == exam, ct);
        try
        {
            var snapshot = await versions.ReadAsync(version, ct);
            var ids = snapshot.Questions.Select(q => q.Id).ToArray();
            var available = await db.CauHoi.AsNoTracking().Where(q => ids.Contains(q.MaCauHoi))
                .Select(q => q.MaCauHoi).ToArrayAsync(ct);
            return Ok(new { missingQuestionIds = ids.Except(available).ToArray() });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int examId, [FromBody] PublishRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publisherId))
            return Forbid();
        try
        {
            var published = await versions.PublishAsync(examId, request.ExpectedCurrentVersionId,
                request.RequestKey, publisherId, ct);
            return Ok(new { published.MaPhienBanDeThi, published.MaDeThi, published.SoPhienBan,
                published.NgayXuatBan, published.SoCau });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("publish-selection")]
    public async Task<IActionResult> PublishSelection(int examId,
        [FromBody] PublishSelectionRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publisherId))
            return Forbid();
        try
        {
            var published = await versions.PublishAsync(examId, request.ExpectedCurrentVersionId,
                request.RequestKey, publisherId, ct, request.OrderedQuestionIds);
            return Ok(new { published.MaPhienBanDeThi, published.MaDeThi,
                published.SoPhienBan, published.SoCau });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("{versionId:int}/restore")]
    public async Task<IActionResult> Restore(int examId, int versionId,
        [FromBody] RestoreRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publisherId))
            return Forbid();
        try
        {
            var restored = await versions.RestoreAsync(examId, versionId,
                request.ExpectedCurrentVersionId, request.RequestKey, publisherId, ct);
            return Ok(new { restored.MaPhienBanDeThi, restored.SoPhienBan,
                restored.SoCau, restored.NgayXuatBan });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }
}
