using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "HOC_VIEN")]
[ApiController]
[Route("api/mock-tests")]
public sealed class MockTestsController(ToeicDbContext db, ExamAttemptService attempts) : ControllerBase
{
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new ExamProblem("ATTEMPT_FORBIDDEN", "Phiên đăng nhập không hợp lệ.", 403);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.DeThi.AsNoTracking()
        .Where(x => x.LoaiDe == "DE_THI" && x.TrangThai == "OPEN" && x.ThoiGianLamBai == ExamCore.DurationMinutes)
        .Select(x => new FixedMockDto(x.MaDeThi, x.TenDe, x.ThoiGianLamBai, x.TrangThai))
        .ToListAsync(ct));

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartExamRequest request, CancellationToken ct)
    {
        if (request.Source is not ("FIXED" or "RANDOM"))
            return BadRequest(new { code = "INVALID_SOURCE", message = "Nguồn đề thi không hợp lệ." });
        try { return Ok(new { attemptId = await attempts.StartAsync(LearnerId, request, ct) }); }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        await attempts.FinalizeExpiredOwnedAsync(LearnerId, ct);
        var rows = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaHocVien == LearnerId && x.LoaiBaiLam == ExamCore.Mock)
            .ToListAsync(ct);
        var exams = await db.DeThi.AsNoTracking().Where(x => x.LoaiDe == "DE_THI").ToListAsync(ct);
        return Ok(MockHistorySummary.Build(rows, exams));
    }
}
