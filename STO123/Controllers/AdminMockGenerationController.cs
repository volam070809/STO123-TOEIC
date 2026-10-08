using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/mock-exams")]
public sealed class AdminMockGenerationController(ToeicDbContext db, ExamGenerationService generation) : ControllerBase
{
    public sealed record GenerateRequest(int CriteriaId);

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken ct)
    {
        var criteria = await db.ThongTinSinhDe.AsNoTracking()
            .Where(x => x.MaSinhDe == request.CriteriaId)
            .Select(x => new { x.MaSinhDe, x.TenDe }).FirstOrDefaultAsync(ct);
        if (criteria is null) return NotFound(new { code = "CRITERIA_NOT_FOUND" });
        var partIds = (await db.PartTOEIC.AsNoTracking().Select(x => new { x.MaPart, x.SoPart }).ToListAsync(ct))
            .ToDictionary(x => x.MaPart, x => x.SoPart);
        var details = await db.ChiTietCauHinhDeThi.AsNoTracking()
            .Where(x => x.MaSinhDe == request.CriteriaId).ToListAsync(ct);
        if (details.Count != 7 || details.Any(x => !partIds.ContainsKey(x.MaPart)))
            return Conflict(new { code = "INVALID_GENERATION_CRITERIA", message = "Cấu hình phải có đủ 7 Part." });
        var targets = details.ToDictionary(x => partIds[x.MaPart],
            x => (x.SoCauDe, x.SoCauTB, x.SoCauKho));
        try
        {
            var candidates = await generation.BuildAdminCandidatesAsync(ct);
            var plan = CriteriaExamPlanner.Select(candidates, targets, Random.Shared);
            var authorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
                throw new ExamProblem("ADMIN_FORBIDDEN", "Phiên đăng nhập không hợp lệ.", 403);
            var exam = new DeThi {
                TenDe = criteria.TenDe, LoaiDe = "DE_THI", ThoiGianLamBai = ExamCore.DurationMinutes,
                TrangThai = "CLOSE", MaSinhDe = criteria.MaSinhDe, MaNguoiSoan = authorId
            };
            var order = 0;
            foreach (var question in plan.SelectMany(x => x.Questions))
                exam.CauHoiDeThi.Add(new CauHoiDeThi { MaCauHoi = question.MaCauHoi, ThuTu = ++order });
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.DeThi.Add(exam);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { examId = exam.MaDeThi, examCode = MockExamCode.FromId(exam.MaDeThi),
                examName = exam.TenDe, status = exam.TrangThai, questionCount = order });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost("{examId:int}/publish")]
    public async Task<IActionResult> Publish(int examId, CancellationToken ct)
    {
        try
        {
            _ = await generation.ValidateForPublishAsync(examId, ct);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var exam = await db.DeThi.FirstOrDefaultAsync(x => x.MaDeThi == examId, ct);
            if (exam is null || exam.TrangThai != "CLOSE" ||
                await db.KetQuaLamBai.AnyAsync(x => x.MaDeThi == examId, ct))
                return Conflict(new { code = "EXAM_NOT_PUBLISHABLE" });
            exam.TrangThai = "OPEN";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { examId, examCode = MockExamCode.FromId(examId), status = "OPEN" });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }
}
