using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.CourseReadPolicy)]
[Route("api/admin/courses")]
public sealed class AdminCoursesController(ToeicDbContext db) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses = ["DANG_MO", "DA_DONG"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminCourseListQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", message = "Trang hoặc kích thước trang không hợp lệ." });

        var search = request.Search?.Trim();
        if (search?.Length > 128)
            return BadRequest(new { code = "INVALID_SEARCH", message = "Từ khóa tìm kiếm tối đa 128 ký tự." });

        var status = request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(status) && !SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái khóa học không hợp lệ." });
        if (request.Stage is not null and (< 1 or > 3))
            return BadRequest(new { code = "INVALID_STAGE", message = "Giai đoạn khóa học phải từ 1 đến 3." });

        var query = db.KhoaHoc.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(course => course.TenKhoaHoc.Contains(search) ||
                (course.MoTa != null && course.MoTa.Contains(search)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(course => course.TrangThai == status);
        if (request.Stage.HasValue) query = query.Where(course => course.GiaiDoan == request.Stage.Value);

        var totalItems = await query.CountAsync(ct);
        var items = await query.OrderBy(course => course.GiaiDoan)
            .ThenBy(course => course.MaKhoaHoc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(course => new AdminCourseListItemDto(
                course.MaKhoaHoc,
                course.TenKhoaHoc,
                course.GiaiDoan,
                course.DiemMucTieuToiDa,
                course.GiaBanLe,
                course.TrangThai,
                course.NgayTao,
                course.MaAdminNoiDungNavigation == null ? null : course.MaAdminNoiDungNavigation.HoTen,
                course.UnitKhoaHoc.Count,
                course.UnitKhoaHoc.SelectMany(unit => unit.LessonKhoaHoc).Count(),
                course.UnitKhoaHoc.SelectMany(unit => unit.LessonKhoaHoc)
                    .SelectMany(lesson => lesson.BuocLoTrinh).Count(),
                course.MaGoiHoc.Count,
                course.ChiTietDangKyKhoaHoc.Count))
            .ToListAsync(ct);

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)request.PageSize);
        return Ok(new AdminPagedCoursesDto(items, request.Page, request.PageSize, totalItems, totalPages));
    }

    [HttpGet("{courseId:int}")]
    public async Task<IActionResult> Detail(int courseId, CancellationToken ct)
    {
        if (courseId <= 0) return CourseNotFound();

        var course = await db.KhoaHoc.AsNoTracking()
            .Where(item => item.MaKhoaHoc == courseId)
            .Select(item => new
            {
                item.MaKhoaHoc,
                item.TenKhoaHoc,
                item.GiaiDoan,
                item.DiemMucTieuToiDa,
                item.GiaBanLe,
                item.MoTa,
                item.TrangThai,
                item.DuongDanAnhDaiDien,
                item.NgayTao,
                item.MaAdminNoiDung,
                ContentAdminName = item.MaAdminNoiDungNavigation == null ? null : item.MaAdminNoiDungNavigation.HoTen,
                DirectEnrollmentCount = item.ChiTietDangKyKhoaHoc.Count
            })
            .SingleOrDefaultAsync(ct);
        if (course is null) return CourseNotFound();

        var units = await db.UnitKhoaHoc.AsNoTracking()
            .Where(unit => unit.MaKhoaHoc == courseId)
            .OrderBy(unit => unit.ThuTu)
            .ThenBy(unit => unit.MaUnit)
            .Select(unit => new AdminCourseUnitDto(
                unit.MaUnit,
                unit.TenUnit,
                unit.MoTa,
                unit.ThuTu,
                unit.LessonKhoaHoc.OrderBy(lesson => lesson.ThuTu)
                    .ThenBy(lesson => lesson.MaLesson)
                    .Select(lesson => new AdminCourseLessonDto(
                        lesson.MaLesson,
                        lesson.TenLesson,
                        lesson.MoTa,
                        lesson.ThuTu,
                        lesson.BuocLoTrinh.Count))
                    .ToList()))
            .ToListAsync(ct);

        var packages = await db.GoiHoc.AsNoTracking()
            .Where(package => package.MaKhoaHoc.Any(item => item.MaKhoaHoc == courseId))
            .OrderBy(package => package.MaGoiHoc)
            .Select(package => new AdminCoursePackageDto(
                package.MaGoiHoc,
                package.TenGoiHoc,
                package.Gia,
                package.SoNgaySuDung,
                package.TrangThai,
                package.DangKyGoiHoc.Count))
            .ToListAsync(ct);

        return Ok(new AdminCourseDetailDto(
            course.MaKhoaHoc,
            course.TenKhoaHoc,
            course.GiaiDoan,
            course.DiemMucTieuToiDa,
            course.GiaBanLe,
            course.MoTa,
            course.TrangThai,
            course.DuongDanAnhDaiDien,
            course.NgayTao,
            course.MaAdminNoiDung,
            course.ContentAdminName,
            course.DirectEnrollmentCount,
            units,
            packages));
    }

    private NotFoundObjectResult CourseNotFound() =>
        NotFound(new { code = "COURSE_NOT_FOUND", message = "Không tìm thấy khóa học." });
}
