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
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var learnerId = LearnerId;
        var rows = await db.DeThi.AsNoTracking()
            .Where(x => x.LoaiDe == "DE_THI" && x.TrangThai == "OPEN" &&
                x.ThoiGianLamBai == ExamCore.DurationMinutes)
            .Select(x => new {
                ExamId = x.MaDeThi, ExamName = x.TenDe, Duration = x.ThoiGianLamBai,
                Completed = x.KetQuaLamBai.Any(a => a.MaHocVien == learnerId &&
                    a.LoaiBaiLam == ExamCore.Mock &&
                    (a.TrangThai == ExamCore.Submitted || a.TrangThai == ExamCore.Expired)),
                Attempt = x.KetQuaLamBai.Where(a => a.MaHocVien == learnerId &&
                    a.LoaiBaiLam == ExamCore.Mock)
                    .OrderByDescending(a => a.TrangThai == ExamCore.Submitted || a.TrangThai == ExamCore.Expired)
                    .ThenByDescending(a => a.MaKetQua)
                    .Select(a => new { a.MaKetQua, a.TrangThai, a.DiemTong }).FirstOrDefault()
            }).OrderBy(x => x.ExamId).ToListAsync(ct);
        return Ok(rows.Select(x => new {
            x.ExamId, ExamCode = MockExamCode.FromId(x.ExamId), x.ExamName, x.Duration,
            State = x.Completed ? "COMPLETED" : x.Attempt == null ? "NOT_STARTED" : "IN_PROGRESS",
            AttemptId = x.Attempt?.MaKetQua, TotalScore = x.Completed ? x.Attempt?.DiemTong : null
        }));
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartMockRequest request, CancellationToken ct)
    {
        if (request.ExamId <= 0)
            return BadRequest(new { code = "INVALID_SOURCE", message = "Chỉ có thể bắt đầu đề thi thử đã xuất bản." });
        try { return Ok(new { attemptId = await attempts.StartAsync(LearnerId,
            new StartExamRequest("FIXED", request.ExamId), ct) }); }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 20)
            return BadRequest(new { code = "INVALID_HISTORY_FILTER" });
        var learnerId = LearnerId;
        var query = db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == learnerId &&
            x.LoaiBaiLam == ExamCore.Mock &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired));
        var rows = await query.OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(x => new { AttemptId = x.MaKetQua, x.TrangThai, x.NgayLamBai,
                ExamId = x.MaDeThi, ExamName = x.MaDeThiNavigation != null ? x.MaDeThiNavigation.TenDe : null,
                TotalScore = x.DiemTong }).ToListAsync(ct);
        var hasMore = rows.Count > pageSize;
        return Ok(new { items = rows.Take(pageSize).Select(x => new {
            x.AttemptId, Status = x.TrangThai, StartedAt = ExamCore.Utc(x.NgayLamBai),
            x.ExamId, ExamCode = MockExamCode.FromId(x.ExamId),
            Name = x.ExamName ?? "Bài thi thử cũ", x.TotalScore
        }).ToList(), page, pageSize, hasMore });
    }
}
