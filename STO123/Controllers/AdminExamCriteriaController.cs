using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/content/exam-criteria")]
public sealed class AdminExamCriteriaController(ToeicDbContext db) : ControllerBase
{
    public sealed record PartInput(int Part, int Easy, int Medium, int Hard);
    public sealed record CriteriaInput(string Name, List<PartInput> Parts);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.ThongTinSinhDe.AsNoTracking().OrderByDescending(x => x.MaSinhDe)
            .Select(x => new { id = x.MaSinhDe, name = x.TenDe, createdAt = x.NgayTao,
                examCount = x.DeThi.Count,
                parts = x.ChiTietCauHinhDeThi.OrderBy(p => p.MaPart).Select(p => new {
                    part = p.MaPartNavigation.SoPart, easy = p.SoCauDe,
                    medium = p.SoCauTB, hard = p.SoCauKho
                }).ToArray() }).ToArrayAsync(ct);
        return Ok(new { items = rows });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriteriaInput input, CancellationToken ct)
    {
        var invalid = await Validate(input, ct);
        if (invalid is not null) return invalid;
        var partIds = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(p => p.SoPart, p => p.MaPart, ct);
        var row = new ThongTinSinhDe { TenDe = input.Name.Trim(), NgayTao = DateTime.UtcNow };
        foreach (var part in input.Parts)
            row.ChiTietCauHinhDeThi.Add(new ChiTietCauHinhDeThi {
                MaPart = partIds[part.Part], SoCauDe = part.Easy,
                SoCauTB = part.Medium, SoCauKho = part.Hard
            });
        db.ThongTinSinhDe.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { id = row.MaSinhDe, name = row.TenDe });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CriteriaInput input, CancellationToken ct)
    {
        var invalid = await Validate(input, ct);
        if (invalid is not null) return invalid;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var row = await db.ThongTinSinhDe.FromSqlInterpolated(
            $"SELECT * FROM dbo.ThongTinSinhDe WITH (UPDLOCK, HOLDLOCK) WHERE MaSinhDe = {id}")
            .SingleOrDefaultAsync(ct);
        if (row is null) return NotFound(new { code = "CRITERIA_NOT_FOUND", message = "Không tìm thấy cấu hình." });
        if (await db.DeThi.AsNoTracking().AnyAsync(e => e.MaSinhDe == id, ct))
            return Conflict(new { code = "CRITERIA_IN_USE",
                message = "Cấu hình đã được dùng để sinh đề. Hãy nhân bản rồi chỉnh cấu hình mới." });
        var partIds = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(p => p.SoPart, p => p.MaPart, ct);
        await db.ChiTietCauHinhDeThi.Where(p => p.MaSinhDe == id).ExecuteDeleteAsync(ct);
        row.TenDe = input.Name.Trim();
        foreach (var part in input.Parts)
            db.ChiTietCauHinhDeThi.Add(new ChiTietCauHinhDeThi {
                MaSinhDe = id, MaPart = partIds[part.Part], SoCauDe = part.Easy,
                SoCauTB = part.Medium, SoCauKho = part.Hard
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { id, name = row.TenDe });
    }

    [HttpPost("{id:int}/clone")]
    public async Task<IActionResult> Clone(int id, CancellationToken ct)
    {
        var old = await db.ThongTinSinhDe.AsNoTracking().Include(x => x.ChiTietCauHinhDeThi)
            .ThenInclude(x => x.MaPartNavigation)
            .SingleOrDefaultAsync(x => x.MaSinhDe == id, ct);
        if (old is null) return NotFound(new { code = "CRITERIA_NOT_FOUND", message = "Không tìm thấy cấu hình." });
        var cloneInput = new CriteriaInput(old.TenDe, old.ChiTietCauHinhDeThi.Select(p => new PartInput(
            p.MaPartNavigation.SoPart, p.SoCauDe, p.SoCauTB, p.SoCauKho)).ToList());
        if (await Validate(cloneInput, ct) is not null)
            return Conflict(new { code = "INVALID_CRITERIA",
                message = "Cấu hình nguồn chưa đủ 200 câu đúng 7 Part; cần sửa trước khi nhân bản." });
        var row = new ThongTinSinhDe { TenDe = (old.TenDe.Length > 53 ? old.TenDe[..53] : old.TenDe) + " (bản sao)",
            NgayTao = DateTime.UtcNow };
        foreach (var part in old.ChiTietCauHinhDeThi)
            row.ChiTietCauHinhDeThi.Add(new ChiTietCauHinhDeThi { MaPart = part.MaPart,
                SoCauDe = part.SoCauDe, SoCauTB = part.SoCauTB, SoCauKho = part.SoCauKho });
        db.ThongTinSinhDe.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { id = row.MaSinhDe, name = row.TenDe });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var row = await db.ThongTinSinhDe.FromSqlInterpolated(
            $"SELECT * FROM dbo.ThongTinSinhDe WITH (UPDLOCK, HOLDLOCK) WHERE MaSinhDe = {id}")
            .SingleOrDefaultAsync(ct);
        if (row is null) return NotFound(new { code = "CRITERIA_NOT_FOUND", message = "Không tìm thấy cấu hình." });
        if (await db.DeThi.AsNoTracking().AnyAsync(e => e.MaSinhDe == id, ct))
            return Conflict(new { code = "CRITERIA_IN_USE", message = "Cấu hình đang được đề thi sử dụng." });
        var details = await db.ChiTietCauHinhDeThi.Where(p => p.MaSinhDe == id).ToListAsync(ct);
        db.ChiTietCauHinhDeThi.RemoveRange(details); db.ThongTinSinhDe.Remove(row);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { id, deleted = true });
    }

    private async Task<IActionResult?> Validate(CriteriaInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 64 ||
            input.Parts is null || input.Parts.Count != 7 ||
            input.Parts.Select(p => p.Part).Distinct().Count() != 7 ||
            input.Parts.Any(p => p.Part is < 1 or > 7 || p.Easy < 0 || p.Medium < 0 || p.Hard < 0 ||
                p.Easy + p.Medium + p.Hard != ExamCore.PartCounts[p.Part]) ||
            input.Parts.Sum(p => p.Easy + p.Medium + p.Hard) != 200)
            return BadRequest(new { code = "INVALID_CRITERIA",
                message = "Cấu hình cần đủ 7 Part và đúng 200 câu theo cấu trúc TOEIC." });
        var count = await db.PartTOEIC.AsNoTracking().CountAsync(p => p.SoPart >= 1 && p.SoPart <= 7, ct);
        return count == 7 ? null : Conflict(new { code = "PART_LOOKUP_MISSING",
            message = "Dữ liệu Part trong Azure SQL chưa đầy đủ." });
    }
}
