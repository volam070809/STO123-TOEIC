using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Exam;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/public/mock-tests")]
public sealed class PublicMockTestsController(ToeicDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicMockTestDto>>> List(CancellationToken ct)
    {
        var exams = await db.DeThi.AsNoTracking()
            .Where(exam => exam.LoaiDe == "DE_THI" && exam.TrangThai == "OPEN" &&
                exam.ThoiGianLamBai == ExamCore.DurationMinutes)
            .OrderBy(exam => exam.MaDeThi)
            .Select(exam => new
            {
                exam.MaDeThi,
                exam.TenDe,
                exam.ThoiGianLamBai,
                QuestionCount = exam.CauHoiDeThi.Count
            })
            .ToListAsync(ct);

        return Ok(exams.Select(exam => new PublicMockTestDto(
            exam.MaDeThi,
            MockExamCode.FromId(exam.MaDeThi)!,
            exam.TenDe,
            exam.ThoiGianLamBai,
            exam.QuestionCount)).ToList());
    }
}
