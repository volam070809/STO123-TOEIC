using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG")]
[ApiController]
[Route("api/admin/content/exams/manual")]
public sealed class AdminManualExamController(ToeicDbContext db, ExamGenerationService generation,
    ExamVersionStore versions) : ControllerBase
{
    public sealed record ManualExamRequest(string Name, List<int> OrderedQuestionIds);
    public sealed record DraftRequest(string Name);

    [HttpPost("drafts/{examId:int}/publish")]
    public async Task<IActionResult> PublishDraft(int examId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publisherId)) return Forbid();
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var exam = await db.DeThi.FromSqlInterpolated(
                $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
                .SingleOrDefaultAsync(ct);
            if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND" });
            if (exam.MaPhienBanHienHanh is not null) return Conflict(new { code = "EXAM_ALREADY_PUBLISHED" });
            var count = await db.CauHoiDeThi.CountAsync(x => x.MaDeThi == examId, ct);
            if (count != 200) return Conflict(new { code = "INCOMPLETE_EXAM", message = "Đề phải có đủ 200 câu." });
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.CauHoiDeThi SET ThuTu = ThuTu + 1000 WHERE MaDeThi = {examId}", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH ordered AS (
                    SELECT l.ThuTu,
                        ROW_NUMBER() OVER (ORDER BY p.SoPart, l.ThuTu) AS NewOrder
                    FROM dbo.CauHoiDeThi l
                    JOIN dbo.CauHoi q ON q.MaCauHoi = l.MaCauHoi
                    JOIN dbo.PartTOEIC p ON p.MaPart = q.MaPart
                    WHERE l.MaDeThi = {examId}
                )
                UPDATE ordered SET ThuTu = NewOrder
                """, ct);
            await tx.CommitAsync(ct);
        }
        try
        {
            var published = await versions.PublishAsync(examId, null, Guid.NewGuid(), publisherId, ct);
            return Ok(new { examId, versionId = published.MaPhienBanDeThi, status = "CLOSE" });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft([FromBody] DraftRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 128)
            return BadRequest(new { code = "INVALID_NAME", message = "Tên đề phải có từ 1 đến 128 ký tự." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var authorId)) return Forbid();
        var exam = new DeThi { TenDe = request.Name.Trim(), LoaiDe = "DE_THI",
            ThoiGianLamBai = ExamCore.DurationMinutes, TrangThai = "CLOSE", MaNguoiSoan = authorId };
        db.DeThi.Add(exam);
        await db.SaveChangesAsync(ct);
        return Ok(new { examId = exam.MaDeThi, examCode = MockExamCode.FromId(exam.MaDeThi),
            name = exam.TenDe, status = "CHUA_XUAT_BAN", questionCount = 0 });
    }

    [HttpGet("drafts/{examId:int}")]
    public async Task<IActionResult> GetDraft(int examId, CancellationToken ct)
    {
        var exam = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == examId && e.LoaiDe == "DE_THI")
            .Select(e => new { e.MaDeThi, e.TenDe, e.MaPhienBanHienHanh, e.TrangThai })
            .SingleOrDefaultAsync(ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND" });
        if (exam.MaPhienBanHienHanh is not null) return Conflict(new { code = "EXAM_ALREADY_PUBLISHED" });
        var links = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == examId)
            .OrderBy(x => x.ThuTu).Select(x => new { x.MaCauHoi, x.ThuTu,
                part = x.MaCauHoiNavigation.MaPartNavigation.SoPart,
                groupId = x.MaCauHoiNavigation.NhomCauHoi == null ? (int?)null :
                    x.MaCauHoiNavigation.NhomCauHoi.MaNguLieu }).ToListAsync(ct);
        return Ok(new { examId, name = exam.TenDe, status = "CHUA_XUAT_BAN",
            questionCount = links.Count, links });
    }

    [HttpPut("drafts/{examId:int}")]
    public async Task<IActionResult> SaveDraft(int examId, [FromBody] DraftRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 128)
            return BadRequest(new { code = "INVALID_NAME" });
        var exam = await db.DeThi.SingleOrDefaultAsync(x => x.MaDeThi == examId && x.LoaiDe == "DE_THI", ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND" });
        if (exam.MaPhienBanHienHanh is not null) return Conflict(new { code = "EXAM_ALREADY_PUBLISHED" });
        exam.TenDe = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new { examId, name = exam.TenDe, status = "CHUA_XUAT_BAN" });
    }

    [HttpDelete("drafts/{examId:int}/units/{questionId:int}")]
    public async Task<IActionResult> RemoveUnit(int examId, int questionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var exam = await db.DeThi.FromSqlInterpolated(
            $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
            .SingleOrDefaultAsync(ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND" });
        if (exam.MaPhienBanHienHanh is not null) return Conflict(new { code = "EXAM_ALREADY_PUBLISHED" });
        var groupId = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaCauHoi == questionId)
            .Select(x => (int?)x.MaNguLieu).SingleOrDefaultAsync(ct);
        var ids = groupId is null ? new[] { questionId } : await db.NhomCauHoi.AsNoTracking()
            .Where(x => x.MaNguLieu == groupId).Select(x => x.MaCauHoi).ToArrayAsync(ct);
        var links = await db.CauHoiDeThi.Where(x => x.MaDeThi == examId && ids.Contains(x.MaCauHoi))
            .ToListAsync(ct);
        if (links.Count != ids.Length) return Conflict(new { code = "INCOMPLETE_UNIT" });
        db.CauHoiDeThi.RemoveRange(links);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { examId, removedQuestionIds = ids });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ManualExamRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 128 ||
            request.OrderedQuestionIds is null || request.OrderedQuestionIds.Count != 200 ||
            request.OrderedQuestionIds.Distinct().Count() != 200)
            return BadRequest(new { code = "INVALID_MANUAL_EXAM",
                message = "Tên đề hoặc danh sách 200 câu không hợp lệ." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var authorId))
            return Forbid();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var candidates = await generation.BuildAdminCandidatesAsync(ct);
            var links = request.OrderedQuestionIds.Select((id, index) => new CauHoiDeThi {
                MaCauHoi = id, ThuTu = index + 1
            }).ToArray();
            var plan = FixedExamPlan.Select(links, candidates);
            var exam = new DeThi { TenDe = request.Name.Trim(), LoaiDe = "DE_THI",
                ThoiGianLamBai = ExamCore.DurationMinutes, TrangThai = "CLOSE",
                MaNguoiSoan = authorId };
            foreach (var link in links) exam.CauHoiDeThi.Add(link);
            db.DeThi.Add(exam);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { examId = exam.MaDeThi, examCode = MockExamCode.FromId(exam.MaDeThi),
                name = exam.TenDe, questionCount = 200, status = exam.TrangThai,
                parts = plan.GroupBy(u => u.Part).OrderBy(g => g.Key)
                    .Select(g => new { part = g.Key, count = g.Sum(u => u.Questions.Count) }) });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code,
            message = "Đề chưa đủ cấu trúc hoặc media hợp lệ. Kiểm tra 200 câu và từng nhóm Part trước khi lưu.",
            detail = e.Message }); }
    }
}
