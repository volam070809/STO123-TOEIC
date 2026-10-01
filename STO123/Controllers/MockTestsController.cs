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
        try { return Ok(new { attemptId = await attempts.StartAsync(LearnerId, request, ct) }); }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var rows = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaHocVien == LearnerId && x.LoaiBaiLam == ExamCore.Mock)
            .OrderByDescending(x => x.NgayLamBai)
            .Select(x => new { x.MaKetQua, x.TrangThai, x.NgayLamBai, x.HetHanLuc, x.DiemTong })
            .ToListAsync(ct);
        return Ok(rows.Select(x => new ExamHistoryDto(x.MaKetQua, x.TrangThai,
            ExamCore.Utc(x.NgayLamBai), x.HetHanLuc.HasValue ? ExamCore.Utc(x.HetHanLuc.Value) : null,
            x.DiemTong)));
    }
}
