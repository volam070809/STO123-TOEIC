using System.Security.Claims;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Services.Arena;
using STO123.Services.Exam;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/arena")]
public sealed class ArenaController(ArenaService arena, BlobServiceClient blobs,
    AvatarStorage avatars) : ControllerBase
{
    public sealed record PartRequest(int Part);
    public sealed record JoinRequest(string? Code);
    public sealed record AnswerRequest(int QuestionIndex, string? Choice);

    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new ArenaProblem("INVALID_SESSION", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpPost("quick")]
    public Task<IActionResult> Quick(PartRequest request, CancellationToken ct) =>
        Run(() => arena.QuickAsync(LearnerId, request.Part, ct));

    [HttpPost("private")]
    public Task<IActionResult> CreatePrivate(PartRequest request, CancellationToken ct) =>
        Run(() => arena.CreatePrivateAsync(LearnerId, request.Part, ct));

    [HttpPost("join")]
    public Task<IActionResult> Join(JoinRequest request, CancellationToken ct) =>
        Run(() => arena.JoinPrivateAsync(LearnerId, request.Code, ct));

    [HttpGet("matches/{id:guid}")]
    public Task<IActionResult> State(Guid id, CancellationToken ct) =>
        Run(() => arena.StateAsync(id, LearnerId, ct));

    [HttpPost("matches/{id:guid}/leave")]
    public Task<IActionResult> Leave(Guid id, CancellationToken ct) =>
        Run(() => arena.LeaveAsync(id, LearnerId, ct));

    [HttpPost("matches/{id:guid}/surrender")]
    public Task<IActionResult> Surrender(Guid id, CancellationToken ct) =>
        Run(() => arena.SurrenderAsync(id, LearnerId, ct));

    [HttpPost("matches/{id:guid}/ready")]
    public Task<IActionResult> Ready(Guid id, CancellationToken ct) =>
        Run(() => arena.ReadyAsync(id, LearnerId, ct));

    [HttpPost("matches/{id:guid}/answer")]
    public Task<IActionResult> Answer(Guid id, AnswerRequest request, CancellationToken ct) =>
        Run(() => arena.AnswerAsync(id, LearnerId, request.QuestionIndex, request.Choice, ct));

    [HttpPost("matches/{id:guid}/invite")]
    public Task<IActionResult> Invite(Guid id, CancellationToken ct) =>
        Run(() => arena.InviteAsync(id, LearnerId, ct));

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        try { return Ok(await arena.ActiveAsync(LearnerId, ct)); }
        catch (ArenaProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
    }

    [HttpGet("matches/{id:guid}/questions/{index:int}/{kind:regex(audio|image)}")]
    public Task<IActionResult> Media(Guid id, int index, string kind, CancellationToken ct) =>
        ReadMedia(id, index, kind, null, ct);

    [HttpGet("matches/{id:guid}/questions/{index:int}/documents/{order:int}/image")]
    public Task<IActionResult> DocumentImage(Guid id, int index, int order, CancellationToken ct) =>
        ReadMedia(id, index, "document", order, ct);

    [HttpGet("matches/{id:guid}/groups/{groupId:int}/audio")]
    public async Task<IActionResult> GroupAudio(Guid id, int groupId, CancellationToken ct)
    {
        try { return await StreamMedia(await arena.GroupAudioPathAsync(id, LearnerId, groupId, ct), ct); }
        catch (ArenaProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
        catch (RequestFailedException error) when (error.Status == 404)
        { return NotFound(new { code = "MEDIA_NOT_FOUND", message = "Không tìm thấy tệp media." }); }
    }

    [HttpGet("matches/{id:guid}/players/{playerId:int}/avatar")]
    public async Task<IActionResult> PlayerAvatar(Guid id, int playerId, CancellationToken ct)
    {
        try
        {
            var reference = await arena.AvatarReferenceAsync(id, LearnerId, playerId, ct);
            if (!AvatarStorage.IsCustomReference(playerId, reference)) return NotFound();
            var image = await avatars.OpenAsync(reference!, ct);
            if (image is null) return NotFound();
            Response.Headers.CacheControl = "private, no-store";
            return File(image.Value.Stream, image.Value.ContentType);
        }
        catch (ArenaProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
        catch (RequestFailedException) { return StatusCode(503,
            new { code = "AVATAR_UNAVAILABLE", message = "Không thể tải ảnh đại diện." }); }
    }

    private async Task<IActionResult> ReadMedia(Guid id, int index, string kind, int? order, CancellationToken ct)
    {
        try
        {
            var (container, path) = await arena.MediaPathAsync(id, LearnerId, index, kind, order, ct);
            return await StreamMedia((container, path), ct);
        }
        catch (ArenaProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
        catch (RequestFailedException error) when (error.Status == 404)
        { return NotFound(new { code = "MEDIA_NOT_FOUND", message = "Không tìm thấy tệp media." }); }
    }

    private async Task<IActionResult> StreamMedia((string Container, string Path) source, CancellationToken ct)
    {
            var (container, path) = source;
            var blob = blobs.GetBlobContainerClient(container).GetBlobClient(path);
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            var mediaType = properties.Value.ContentType is { Length: > 0 } stored && stored != "application/octet-stream" ?
                stored : Path.GetExtension(path).ToLowerInvariant() switch {
                    ".mp3" => "audio/mpeg", ".png" => "image/png", ".webp" => "image/webp",
                    ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream"
                };
            return File(await blob.OpenReadAsync(cancellationToken: ct), mediaType, enableRangeProcessing: true);
    }

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (ArenaProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
        catch (ExamProblem problem) { return StatusCode(problem.Status,
            new { code = problem.Code, message = problem.Message }); }
    }
}
