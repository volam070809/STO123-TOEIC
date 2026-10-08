using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record ExamPartMap(IReadOnlyDictionary<int, int> NumberById,
    IReadOnlyDictionary<int, int> IdByNumber);

public sealed class ExamPartLookup(IMemoryCache cache, ToeicDbContext db)
{
    public async Task<ExamPartMap> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<ExamPartMap>(nameof(ExamPartMap), out var cached) && cached is not null)
            return cached;
        var rows = await db.PartTOEIC.AsNoTracking()
            .Select(p => new { p.MaPart, p.SoPart }).ToListAsync(ct);
        var map = new ExamPartMap(rows.ToDictionary(p => p.MaPart, p => p.SoPart),
            rows.ToDictionary(p => p.SoPart, p => p.MaPart));
        cache.Set(nameof(ExamPartMap), map, TimeSpan.FromHours(1));
        return map;
    }
}
