using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Services.SmartLearning;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/smart-learning")]
public sealed class SmartLearningController(SmartLearningService smartLearning) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        return Ok(await smartLearning.ProfileAsync(learnerId, ct));
    }
}
