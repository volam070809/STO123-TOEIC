using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Route("api/parts")]
public class PartsController : ControllerBase
{
    private readonly ToeicDbContext _context;

    public PartsController(ToeicDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var parts = await _context.PartTOEIC
            .OrderBy(p => p.MaPart)
            .Select(p => new
            {
                p.MaPart,
                p.SoPart,
                p.SoCauChuan
            })
            .ToListAsync();

        return Ok(parts);
    }
}