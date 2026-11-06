using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Services.Exam;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/practice")]
public sealed class PracticeController(PracticePartService practice) : ControllerBase
{
    public sealed record StartRequest(int Part, int Difficulty, string? Preset, bool FromRecommendation = false);
    public sealed record CheckRequest(string? SelectedOption);

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct) =>
        await Run(() => practice.AvailabilityAsync(ct));

    [HttpPost("start")]
    public async Task<IActionResult> Start(StartRequest request, CancellationToken ct) =>
        await WithLearner(id => practice.StartAsync(id, request.Part, request.Difficulty, request.Preset,
            ct, request.FromRecommendation));

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct) =>
        await WithLearner(async id => await practice.ActiveAsync(id, ct) ?? new { attemptId = (int?)null });

    [HttpGet("active-list")]
    public async Task<IActionResult> ActiveList(CancellationToken ct) =>
        await WithLearner(id => practice.ActiveListAsync(id, ct));

    [HttpGet("{attemptId:int}")]
    public async Task<IActionResult> Continue(int attemptId, CancellationToken ct) =>
        await WithLearner(id => practice.ContinueAsync(id, attemptId, ct));

    [HttpPost("{attemptId:int}/questions/{questionOccurrenceId:int}/check")]
    public async Task<IActionResult> Check(int attemptId, int questionOccurrenceId, CheckRequest request, CancellationToken ct) =>
        await WithLearner(id => practice.CheckAsync(id, attemptId, questionOccurrenceId, request.SelectedOption, ct));

    [HttpGet("{attemptId:int}/questions/{questionOccurrenceId:int}/answer")]
    public async Task<IActionResult> SeeAnswer(int attemptId, int questionOccurrenceId, CancellationToken ct) =>
        await WithLearner(id => practice.SeeAnswerAsync(id, attemptId, questionOccurrenceId, ct));

    [HttpPost("{attemptId:int}/questions/{questionOccurrenceId:int}/redo")]
    public async Task<IActionResult> Redo(int attemptId, int questionOccurrenceId, CancellationToken ct) =>
        await WithLearner(id => practice.RedoAsync(id, attemptId, questionOccurrenceId, ct));

    [HttpPost("{attemptId:int}/finish")]
    public async Task<IActionResult> Finish(int attemptId, CancellationToken ct) =>
        await WithLearner(id => practice.FinishAsync(id, attemptId, ct));

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics(CancellationToken ct) =>
        await WithLearner(id => practice.StatisticsAsync(id, ct));

    [HttpGet("history/{attemptId:int}")]
    public IActionResult Review(int attemptId) => StatusCode(410, new {
        code = "PRACTICE_HISTORY_REMOVED", message = "Luyện tập theo Part chỉ lưu thống kê tổng hợp."
    });

    private async Task<IActionResult> WithLearner(Func<int, Task<object>> action)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0) return Unauthorized();
        return await Run(() => action(id));
    }

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ExamProblem problem) { return StatusCode(problem.Status, new { code = problem.Code, message = problem.Message }); }
    }
}
