using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/packages")]
public sealed class AdminPackagesController(ToeicDbContext db) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses = ["DANG_MO", "DA_DONG"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminPackageListQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", message = "Trang hoặc kích thước trang không hợp lệ." });
        var search = request.Search?.Trim();
        if (search?.Length > 100)
            return BadRequest(new { code = "INVALID_SEARCH", message = "Từ khóa tìm kiếm tối đa 100 ký tự." });
        var status = request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(status) && !SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái gói học không hợp lệ." });
        if (request.CourseId is <= 0)
            return BadRequest(new { code = "INVALID_COURSE", message = "Khóa học lọc không hợp lệ." });

        var query = db.GoiHoc.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(search)) query = query.Where(item => item.TenGoiHoc.Contains(search) ||
            (item.MoTa != null && item.MoTa.Contains(search)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(item => item.TrangThai == status);
        if (request.CourseId.HasValue)
            query = query.Where(item => item.MaKhoaHoc.Any(course => course.MaKhoaHoc == request.CourseId));

        var totalItems = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.NgayTao).ThenBy(item => item.MaGoiHoc)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new AdminPackageListItemDto(
                item.MaGoiHoc, item.TenGoiHoc, item.Gia, item.SoNgaySuDung, item.TrangThai, item.NgayTao,
                item.MaKhoaHoc.Count, item.DangKyGoiHoc.Count,
                item.DangKyGoiHoc.Count(enrollment => enrollment.TrangThai == "DANG_SU_DUNG"),
                item.DangKyGoiHoc.SelectMany(enrollment => enrollment.GiaoDichThanhToan)
                    .Count(payment => payment.TrangThai == "THANH_CONG"),
                item.MaKhoaHoc.OrderBy(course => course.GiaiDoan).Select(course => course.TenKhoaHoc).ToList()))
            .ToListAsync(ct);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)request.PageSize);
        return Ok(new AdminPagedPackagesDto(items, request.Page, request.PageSize, totalItems, totalPages));
    }

    [HttpGet("{packageId:int}")]
    public async Task<IActionResult> Detail(int packageId, CancellationToken ct)
    {
        var package = await db.GoiHoc.AsNoTracking().Where(item => item.MaGoiHoc == packageId)
            .Select(item => new { item.MaGoiHoc, item.TenGoiHoc, item.Gia, item.SoNgaySuDung,
                item.MoTa, item.TrangThai, item.NgayTao }).SingleOrDefaultAsync(ct);
        if (package is null) return PackageNotFound();

        var courses = await db.KhoaHoc.AsNoTracking()
            .Where(course => course.MaGoiHoc.Any(item => item.MaGoiHoc == packageId))
            .OrderBy(course => course.GiaiDoan)
            .Select(course => new AdminPackageCourseDto(course.MaKhoaHoc, course.TenKhoaHoc,
                course.GiaiDoan, course.TrangThai, course.GiaBanLe)).ToListAsync(ct);
        var stats = await db.DangKyGoiHoc.AsNoTracking().Where(item => item.MaGoiHoc == packageId)
            .GroupBy(_ => 1).Select(group => new AdminPackageEnrollmentStatsDto(
                group.Count(), group.Count(item => item.TrangThai == "CHO_THANH_TOAN"),
                group.Count(item => item.TrangThai == "DANG_SU_DUNG"),
                group.Count(item => item.TrangThai == "HET_HAN"),
                group.SelectMany(item => item.GiaoDichThanhToan).Count(payment => payment.TrangThai == "THANH_CONG"),
                group.SelectMany(item => item.GiaoDichThanhToan)
                    .Where(payment => payment.TrangThai == "THANH_CONG").Sum(payment => payment.SoTien)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0, 0);

        return Ok(new AdminPackageDetailDto(package.MaGoiHoc, package.TenGoiHoc, package.Gia,
            package.SoNgaySuDung, package.MoTa, package.TrangThai, package.NgayTao, courses, stats));
    }

    private NotFoundObjectResult PackageNotFound() =>
        NotFound(new { code = "PACKAGE_NOT_FOUND", message = "Không tìm thấy gói học." });
}
