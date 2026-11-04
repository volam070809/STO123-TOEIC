using Microsoft.AspNetCore.Mvc;
using STO123.Services;

namespace STO123.Controllers;

[ApiController]
[Route("api/content/revisions")]
public sealed class ContentRevisionController(ContentRevisionStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] long? since, [FromQuery] bool fresh, CancellationToken ct)
    {
        if (since is < 0) return BadRequest(new { message = "Phiên bản nội dung không hợp lệ." });
        Response.Headers.CacheControl = "no-store";
        var snapshot = await store.ReadAsync(since, fresh, ct);
        return Ok(new { version = snapshot.Version, changedDomains = snapshot.ChangedDomains });
    }
}
