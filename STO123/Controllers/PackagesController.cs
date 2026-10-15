using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Controllers;

public sealed record PackageCourseDto(int CourseId, string Name, byte Stage);
public sealed record PackageDto(int PackageId, string Name, string? Description, decimal Price,
    int ValidDays, IReadOnlyList<PackageCourseDto> Courses, bool IsPurchased, bool IsActive);

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/packages")]
public sealed class PackagesController(ToeicDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!LearnerId(out var learnerId)) return Unauthorized();
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var packages = await AvailablePackages(learnerId, now)
            .ToListAsync(cancellationToken);
        return Ok(packages.Where(item => item.Price == decimal.Truncate(item.Price)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        if (!LearnerId(out var learnerId)) return Unauthorized();
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var package = await AvailablePackages(learnerId, now, id)
            .SingleOrDefaultAsync(cancellationToken);
        return package is null || package.Price != decimal.Truncate(package.Price) ?
            NotFound(new { code = "PACKAGE_UNAVAILABLE" }) : Ok(package);
    }

    private IQueryable<PackageDto> AvailablePackages(int learnerId, DateTime now, int? packageId = null) =>
        db.GoiHoc.AsNoTracking()
            .Where(item => item.TrangThai == "DANG_MO" && item.Gia > 0 &&
                item.SoNgaySuDung > 0 && item.MaKhoaHoc.Any() &&
                item.MaKhoaHoc.All(course => course.TrangThai == "DANG_MO") &&
                (packageId == null || item.MaGoiHoc == packageId))
            .OrderBy(item => item.MaGoiHoc)
            .Select(item => new PackageDto(
                item.MaGoiHoc, item.TenGoiHoc, item.MoTa, item.Gia, item.SoNgaySuDung,
                item.MaKhoaHoc.Where(course => course.TrangThai == "DANG_MO")
                    .OrderBy(course => course.GiaiDoan)
                    .Select(course => new PackageCourseDto(course.MaKhoaHoc,
                        course.TenKhoaHoc, course.GiaiDoan)).ToList(),
                item.DangKyGoiHoc.Any(enrollment => enrollment.MaNguoiDung == learnerId &&
                    (enrollment.TrangThai == "DANG_SU_DUNG" || enrollment.TrangThai == "HET_HAN") &&
                    enrollment.NgayBatDau != null && enrollment.NgayKetThuc > enrollment.NgayBatDau &&
                    enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG")),
                item.DangKyGoiHoc.Any(enrollment => enrollment.MaNguoiDung == learnerId &&
                    enrollment.TrangThai == "DANG_SU_DUNG" && enrollment.NgayBatDau <= now &&
                    enrollment.NgayKetThuc > now &&
                    enrollment.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"))));

    private bool LearnerId(out int learnerId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out learnerId) && learnerId > 0;
}
