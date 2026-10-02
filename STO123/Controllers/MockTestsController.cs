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

    private async Task<MockAttemptSummaryDto?> ActiveSummary(CancellationToken ct)
    {
        var candidates = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock && (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua).Take(20).ToListAsync(ct);
        var row = candidates.FirstOrDefault(x => ExamTimer.Remaining(x, DateTime.UtcNow) > 0);
        if (row is null) return null;
        var counts = await db.CauHoiLuotLam.AsNoTracking().Where(q => q.MaKetQua == row.MaKetQua)
            .GroupBy(q => q.MaPartNavigation.SoPart)
            .Select(g => new { Part = g.Key, Total = g.Count(),
                Answered = g.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null) })
            .ToListAsync(ct);
        var part = row.MaDeThi is null && counts.Count == 1 ? counts[0].Part : (int?)null;
        var name = row.MaDeThi.HasValue ? await db.DeThi.AsNoTracking()
            .Where(x => x.MaDeThi == row.MaDeThi).Select(x => x.TenDe).FirstOrDefaultAsync(ct) :
            part.HasValue ? $"Thi thử Part {part}" : "Đề ngẫu nhiên toàn bài";
        return new MockAttemptSummaryDto(row.MaKetQua, name ?? "Thi thử", row.TrangThai,
            ExamCore.Utc(row.NgayLamBai), null, counts.Sum(x => x.Total), counts.Sum(x => x.Answered),
            part, null, null, null, row.MaDeThi.HasValue ? "FIXED" : part.HasValue ? "PART" : "RANDOM",
            row.MaDeThi)
        { RemainingSeconds = ExamTimer.Remaining(row, DateTime.UtcNow), IsPaused = row.BatDauPhienLuc is null };
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var completedCount = await db.KetQuaLamBai.AsNoTracking().CountAsync(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired), ct);
        return Ok(new { completedCount, active = await ActiveSummary(ct) });
    }

    [HttpGet("fixed-summary")]
    public async Task<IActionResult> FixedSummary(CancellationToken ct)
    {
        var exams = await db.DeThi.AsNoTracking().Where(x => x.LoaiDe == "DE_THI")
            .Select(x => new FixedMockDto(x.MaDeThi, x.TenDe, x.ThoiGianLamBai, x.TrangThai)).ToListAsync(ct);
        var counts = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock && x.MaDeThi != null &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
            .GroupBy(x => x.MaDeThi).Select(g => new { ExamId = g.Key, Count = g.Count(),
                BestScore = g.Max(x => x.DiemTong) }).ToListAsync(ct);
        var active = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock && x.MaDeThi != null &&
            (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .Select(x => new { x.MaDeThi, x.MaKetQua, x.NgayLamBai, x.ThoiGianConLaiGiay, x.BatDauPhienLuc })
            .ToListAsync(ct);
        return Ok(exams.Where(exam => (exam.Status == "OPEN" && exam.Duration == ExamCore.DurationMinutes) ||
            counts.Any(x => x.ExamId == exam.ExamId)).Select(exam => new { exam.ExamId, exam.ExamName,
            examStatus = exam.Status, exam.Duration,
            completedAttempts = counts.FirstOrDefault(x => x.ExamId == exam.ExamId)?.Count ?? 0,
            bestScore = counts.FirstOrDefault(x => x.ExamId == exam.ExamId)?.BestScore,
            activeAttemptId = active.Where(x => x.MaDeThi == exam.ExamId)
                .OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua)
                .Select(x => (int?)x.MaKetQua).FirstOrDefault() }).ToList());
    }

    [HttpGet("parts")]
    public async Task<IActionResult> Parts(CancellationToken ct)
    {
        var active = await db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock && x.MaDeThi == null &&
            (x.TrangThai == ExamCore.Active || x.TrangThai == "BO_DO"))
            .OrderByDescending(x => x.NgayLamBai).ToListAsync(ct);
        var ids = active.Select(x => x.MaKetQua).ToArray();
        var groups = await db.CauHoiLuotLam.AsNoTracking().Where(q => ids.Contains(q.MaKetQua))
            .GroupBy(q => new { q.MaKetQua, q.MaPartNavigation.SoPart })
            .Select(g => new { g.Key.MaKetQua, Part = g.Key.SoPart, Total = g.Count(),
                Answered = g.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null) })
            .ToListAsync(ct);
        var onePart = groups.GroupBy(x => x.MaKetQua).Where(g => g.Count() == 1)
            .Select(g => g.Single()).ToDictionary(x => x.MaKetQua);
        return Ok(ExamCore.PartCounts.Select(p => {
            var row = active.FirstOrDefault(x => onePart.TryGetValue(x.MaKetQua, out var g) && g.Part == p.Key &&
                ExamTimer.Remaining(x, DateTime.UtcNow) > 0);
            var group = row is null ? null : onePart[row.MaKetQua];
            return new { part = p.Key, totalQuestions = p.Value,
                activeAttemptId = row?.MaKetQua, answered = group?.Answered,
                remainingSeconds = row is null ? null : (int?)ExamTimer.Remaining(row, DateTime.UtcNow) };
        }).ToList());
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string mode = "ALL", [FromQuery] int? part = null,
        [FromQuery] int? examId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        mode = mode.ToUpperInvariant();
        if (mode is not ("ALL" or "FIXED" or "RANDOM" or "PART") ||
            page < 1 || pageSize is < 1 or > 20 ||
            (mode == "PART" && part is < 1 or > 7) ||
            (mode != "PART" && part is not null) || (mode != "FIXED" && examId is not null))
            return BadRequest(new { code = "INVALID_HISTORY_FILTER" });
        var query = db.KetQuaLamBai.AsNoTracking().Where(x => x.MaHocVien == LearnerId &&
            x.LoaiBaiLam == ExamCore.Mock &&
            (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired));
        query = mode switch
        {
            "FIXED" => query.Where(x => x.MaDeThi != null && (examId == null || x.MaDeThi == examId)),
            "RANDOM" => query.Where(x => x.MaDeThi == null &&
                x.CauHoiLuotLam.Select(q => q.MaPart).Distinct().Count() > 1),
            "PART" => query.Where(x => x.MaDeThi == null && x.CauHoiLuotLam.Any() &&
                (part == null || x.CauHoiLuotLam.All(q => q.MaPartNavigation.SoPart == part)) &&
                x.CauHoiLuotLam.Select(q => q.MaPart).Distinct().Count() == 1),
            _ => query
        };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.NgayLamBai).ThenByDescending(x => x.MaKetQua)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new {
                x.MaKetQua, x.TrangThai, x.NgayLamBai, x.MaDeThi, x.DiemTong,
                ExamName = x.MaDeThiNavigation != null ? x.MaDeThiNavigation.TenDe : null,
                Total = x.CauHoiLuotLam.Count,
                Answered = x.CauHoiLuotLam.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon != null),
                Correct = x.CauHoiLuotLam.Count(q => q.ChiTietKetQua != null && q.ChiTietKetQua.DapAnChon == q.PhuongAnDung),
                Part = x.MaDeThi == null && x.CauHoiLuotLam.Select(q => q.MaPart).Distinct().Count() == 1 ?
                    x.CauHoiLuotLam.Select(q => (int?)q.MaPartNavigation.SoPart).FirstOrDefault() : null
            }).ToListAsync(ct);
        var items = rows.Select(x => new MockAttemptSummaryDto(x.MaKetQua,
            x.ExamName ?? (x.Part.HasValue ? $"Thi thử Part {x.Part}" : "Đề ngẫu nhiên toàn bài"),
            x.TrangThai, ExamCore.Utc(x.NgayLamBai), null, x.Total, x.Answered, x.Part,
            x.Part.HasValue ? null : x.DiemTong, x.Part.HasValue ? x.Correct : null,
            x.Part.HasValue && x.Total > 0 ? Math.Round(100m * x.Correct / x.Total, 1) : null,
            x.MaDeThi.HasValue ? "FIXED" : x.Part.HasValue ? "PART" : "RANDOM", x.MaDeThi)).ToList();
        return Ok(new { items, total, page, pageSize, hasMore = page * pageSize < total });
    }

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct) => Ok(await ActiveSummary(ct));
}
