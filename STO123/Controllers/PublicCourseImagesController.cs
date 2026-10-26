using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Services.Admin;

namespace STO123.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/course-images")]
public sealed class PublicCourseImagesController(CourseImageStorage images) : ControllerBase
{
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Get(string fileName, CancellationToken ct)
    {
        try
        {
            var image = await images.OpenAsync(fileName, ct);
            if (image is null) return NotFound();
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(image.Value.Stream, image.Value.ContentType);
        }
        catch (Exception error) when (error is RequestFailedException or AuthenticationFailedException)
        {
            return StatusCode(503, new { message = "Không thể tải ảnh khóa học." });
        }
    }
}
