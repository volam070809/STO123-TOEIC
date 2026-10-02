using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Authorize]
[Route("api/tu-vung/practice-history")]
public class VocabularyPracticeHistoryController(ToeicDbContext db) : ControllerBase
{
    public sealed record AnswerRequest(int MaTuVung, string Type, string Prompt,
        List<string> Options, int CorrectIndex, int SelectedIndex);
    public sealed record SaveRequest(int MaChuDe, List<AnswerRequest> Answers);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request.Answers is not { Count: > 0 and <= 1000 } || request.MaChuDe <= 0 ||
            request.Answers.Any(x => x is null || x.MaTuVung <= 0) ||
            request.Answers.Select(x => x.MaTuVung).Distinct().Count() != request.Answers.Count)
            return BadRequest(new { message = "Invalid practice answers." });

        if (!await db.ChuDe.AnyAsync(x => x.MaChuDe == request.MaChuDe && x.TrangThai == "DANG_MO", ct))
            return BadRequest(new { message = "Topic is unavailable." });

        var wordIds = request.Answers.Select(x => x.MaTuVung).ToArray();
        var words = await db.TuVungChuDe.AsNoTracking()
            .Where(x => x.MaChuDe == request.MaChuDe && wordIds.Contains(x.MaTuVung))
            .Select(x => new { x.MaTuVung, Word = x.MaTuVungNavigation.TuVung1,
                Meaning = x.MaTuVungNavigation.Nghia }).ToDictionaryAsync(x => x.MaTuVung, ct);
        if (words.Count != request.Answers.Count)
            return BadRequest(new { message = "A vocabulary word does not belong to this topic." });

        var now = DateTime.UtcNow;
        var details = new List<ChiTietLuyenTuVung>();
        for (var i = 0; i < request.Answers.Count; i++)
        {
            var answer = request.Answers[i];
            var word = words[answer.MaTuVung];
            var wordToMeaning = answer.Type == "word-to-meaning";
            if (!wordToMeaning && answer.Type != "meaning-to-word")
                return BadRequest(new { message = "Invalid question type." });
            var expectedPrompt = wordToMeaning ? word.Word : word.Meaning;
            var expectedAnswer = wordToMeaning ? word.Meaning : word.Word;
            if (answer.Prompt?.Trim() != expectedPrompt.Trim() || answer.Prompt.Length > 512 ||
                answer.Options is not { Count: >= 2 and <= 4 } ||
                answer.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 256) ||
                answer.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != answer.Options.Count ||
                answer.CorrectIndex < 0 || answer.CorrectIndex >= answer.Options.Count ||
                answer.SelectedIndex < 0 || answer.SelectedIndex >= answer.Options.Count ||
                answer.Options[answer.CorrectIndex] != expectedAnswer.Trim())
                return BadRequest(new { message = "Invalid question or answer." });

            details.Add(new ChiTietLuyenTuVung
            {
                MaChuDe = request.MaChuDe,
                MaTuVung = answer.MaTuVung,
                ThuTu = i + 1,
                NoiDungCauHoi = answer.Prompt,
                PhuongAnA = answer.Options[0],
                PhuongAnB = answer.Options[1],
                // Empty strings represent absent choices in a two or three option quiz.
                PhuongAnC = answer.Options.Count > 2 ? answer.Options[2] : "",
                PhuongAnD = answer.Options.Count > 3 ? answer.Options[3] : "",
                PhuongAnDung = Letter(answer.CorrectIndex),
                DapAnChon = Letter(answer.SelectedIndex),
                LaDung = answer.CorrectIndex == answer.SelectedIndex
            });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var attempt = new LanLuyenTuVung
        {
            MaNguoiDung = userId,
            MaChuDe = request.MaChuDe,
            TrangThai = "DA_NOP",
            NgayBatDau = now,
            NgayNopBai = now
        };
        db.LanLuyenTuVung.Add(attempt);
        await db.SaveChangesAsync(ct);
        foreach (var detail in details) detail.MaLanLuyen = attempt.MaLanLuyen;
        db.ChiTietLuyenTuVung.AddRange(details);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(GetOne), new { id = attempt.MaLanLuyen }, new
        {
            maLanLuyen = attempt.MaLanLuyen,
            total = details.Count,
            correct = details.Count(x => x.LaDung == true),
            incorrect = details.Count(x => x.LaDung == false),
            percent = Percent(details.Count(x => x.LaDung == true), details.Count)
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (page < 1 || pageSize is < 1 or > 20) return BadRequest(new { message = "Invalid page." });
        var query = db.LanLuyenTuVung.AsNoTracking()
            .Where(x => x.MaNguoiDung == userId && x.NgayNopBai != null);
        var total = await query.CountAsync(ct);
        var attempts = await query
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaLanLuyen)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.MaLanLuyen, x.MaChuDe, x.MaChuDeNavigation.TenChuDe,
                x.NgayBatDau, x.NgayNopBai, x.TrangThai,
                Total = x.ChiTietLuyenTuVung.Count(),
                Correct = x.ChiTietLuyenTuVung.Count(d => d.LaDung == true),
                Incorrect = x.ChiTietLuyenTuVung.Count(d => d.LaDung == false) })
            .ToListAsync(ct);
        return Ok(new { items = attempts.Select(x => new { x.MaLanLuyen, x.MaChuDe, x.TenChuDe,
            x.NgayBatDau, x.NgayNopBai, x.TrangThai, x.Total, x.Correct,
            x.Incorrect, Percent = x.Correct + x.Incorrect == x.Total
                ? Percent(x.Correct, x.Total) : (int?)null }), total, page, pageSize,
            hasMore = page * pageSize < total });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var attempt = await db.LanLuyenTuVung.AsNoTracking()
            .Where(x => x.MaLanLuyen == id && x.MaNguoiDung == userId && x.NgayNopBai != null)
            .Select(x => new { x.MaLanLuyen, x.MaChuDe, x.MaChuDeNavigation.TenChuDe,
                x.NgayBatDau, x.NgayNopBai, x.TrangThai }).FirstOrDefaultAsync(ct);
        if (attempt is null) return NotFound();
        var details = await db.ChiTietLuyenTuVung.AsNoTracking()
            .Where(x => x.MaLanLuyen == id && x.MaChuDe == attempt.MaChuDe)
            .OrderBy(x => x.ThuTu)
            .Select(x => new { x.MaTuVung, Word = x.TuVungChuDe.MaTuVungNavigation.TuVung1,
                x.ThuTu, x.NoiDungCauHoi, x.PhuongAnA, x.PhuongAnB, x.PhuongAnC,
                x.PhuongAnD, x.PhuongAnDung, x.DapAnChon, x.LaDung })
            .ToListAsync(ct);
        var correct = details.Count(x => x.LaDung == true);
        var incorrect = details.Count(x => x.LaDung == false);
        return Ok(new { attempt.MaLanLuyen, attempt.MaChuDe, attempt.TenChuDe,
            attempt.NgayBatDau, attempt.NgayNopBai, attempt.TrangThai,
            Total = details.Count, Correct = correct, Incorrect = incorrect,
            Percent = correct + incorrect == details.Count ? Percent(correct, details.Count) : (int?)null,
            Details = details });
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private static string Letter(int index) => ((char)('A' + index)).ToString();
    private static int Percent(int correct, int total) => total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total);
}
