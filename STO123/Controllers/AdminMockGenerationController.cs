using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG")]
[ApiController]
[Route("api/admin/mock-exams")]
public sealed class AdminMockGenerationController(ToeicDbContext db, ExamGenerationService generation,
    ExamVersionStore versions) : ControllerBase
{
    public sealed record GenerateRequest(int CriteriaId);
    public sealed record PartShortage(int Part, int EasyMissing, int MediumMissing, int HardMissing);

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken ct)
    {
        // Keep the criteria and selected source questions stable until their exam links are saved.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
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
        PartShortage[] shortages = [];
        try
        {
            var candidates = await generation.BuildAdminCandidatesAsync(ct);
            shortages = targets.OrderBy(x => x.Key).Select(x => {
                var questions = candidates.Where(u => u.Part == x.Key)
                    .SelectMany(u => u.Questions).DistinctBy(q => q.MaCauHoi).ToArray();
                return new PartShortage(x.Key,
                    Math.Max(0, x.Value.SoCauDe - questions.Count(q => q.DoKho == 1)),
                    Math.Max(0, x.Value.SoCauTB - questions.Count(q => q.DoKho == 2)),
                    Math.Max(0, x.Value.SoCauKho - questions.Count(q => q.DoKho == 3)));
            }).ToArray();
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
            db.DeThi.Add(exam);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { examId = exam.MaDeThi, examCode = MockExamCode.FromId(exam.MaDeThi),
                examName = exam.TenDe, status = exam.TrangThai, questionCount = order });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code,
            message = e.Message, shortages = shortages.Where(x =>
                x.EasyMissing > 0 || x.MediumMissing > 0 || x.HardMissing > 0).ToArray() }); }
    }

    [HttpPost("{examId:int}/publish")]
    public async Task<IActionResult> Publish(int examId, CancellationToken ct)
    {
        if (!Guid.TryParse(Request.Headers["Idempotency-Key"], out var requestKey) ||
            requestKey == Guid.Empty)
            return BadRequest(new { code = "MISSING_REQUEST_KEY",
                message = "Thiếu mã yêu cầu xuất bản. Vui lòng mở lại đề và thử lại." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publisherId))
            return Forbid();
        try
        {
            var current = await db.DeThi.AsNoTracking().Where(x => x.MaDeThi == examId)
                .Select(x => x.MaPhienBanHienHanh).SingleOrDefaultAsync(ct);
            var version = await versions.PublishAsync(examId, current, requestKey, publisherId, ct);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var exam = await db.DeThi.FromSqlInterpolated(
                $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
                .SingleAsync(ct);
            if (exam.MaPhienBanHienHanh != version.MaPhienBanDeThi)
                return Conflict(new { code = "VERSION_CONFLICT",
                    message = "Đề đã có phiên bản mới. Vui lòng tải lại." });
            exam.TrangThai = "OPEN";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { examId, examCode = MockExamCode.FromId(examId), status = "OPEN",
                versionId = version.MaPhienBanDeThi, version = version.SoPhienBan });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }
}
