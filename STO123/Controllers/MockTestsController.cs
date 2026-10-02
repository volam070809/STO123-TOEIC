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
        if (request.Source is not ("FIXED" or "RANDOM" or "PART"))
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
        var finalized = rows.Where(x => x.TrangThai is ExamCore.Submitted or ExamCore.Expired)
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua).ToList();
        var summaries = new List<MockAttemptSummaryDto>();
        foreach (var row in finalized)
        {
            var result = await attempts.ResultAsync(row.MaKetQua, LearnerId, ct);
            var tested = result.Parts.Where(p => p.Stats.Total > 0).ToArray();
            var part = row.MaDeThi is null && tested.Length == 1 ? tested[0].Part : (int?)null;
            summaries.Add(new(row.MaKetQua, result.ExamName ?? "Thi thử", row.TrangThai,
                result.StartedAt, null, result.Overall.Total,
                result.Overall.Total - result.Overall.Unanswered, part,
                part is null ? result.TotalScore : null,
                part is null ? null : tested[0].Stats.Correct,
                part is null ? null : tested[0].Stats.Percentage,
                row.MaDeThi.HasValue ? "FIXED" : part.HasValue ? "PART" : "RANDOM", row.MaDeThi));
        }
        return Ok(MockHistorySummary.Build(rows, exams) with { Attempts = summaries });
    }

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        await attempts.FinalizeExpiredOwnedAsync(LearnerId, ct);
        var row = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).FirstOrDefaultAsync(ct);
        if (row is null) return Ok((MockAttemptSummaryDto?)null);
        var current = await attempts.GetAsync(row.MaKetQua, LearnerId, ct);
        if (current.Status is not (ExamCore.Active or "BO_DO")) return Ok((MockAttemptSummaryDto?)null);
        var questions = current.Groups.SelectMany(g => g.Questions).Concat(current.IndependentQuestions).ToArray();
        var tested = questions.Select(q => q.Part).Distinct().ToArray();
        var part = current.Source == "PART" && tested.Length == 1 ? tested[0] : (int?)null;
        return Ok(new MockAttemptSummaryDto(row.MaKetQua, current.ExamName ?? "Thi thử", current.Status,
            current.StartedAt, current.ExpiresAt, current.TotalQuestions,
            questions.Count(q => q.SelectedOption is not null), part, null, null, null,
            current.Source, row.MaDeThi)
            { RemainingSeconds = current.RemainingSeconds, IsPaused = current.IsPaused });
    }
}
