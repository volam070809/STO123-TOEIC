using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Courses;
using STO123.Models;
using STO123.Services.Payment;

namespace STO123.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/public/courses")]
public sealed class PublicCoursesController(ToeicDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicCourseSummaryDto>>> List(CancellationToken ct)
    {
        var courses = await db.KhoaHoc.AsNoTracking()
            .Where(course => course.TrangThai == "DANG_MO")
            .OrderBy(course => course.GiaiDoan)
            .ThenBy(course => course.MaKhoaHoc)
            .Select(course => new PublicCourseSummaryDto(
                course.MaKhoaHoc,
                course.TenKhoaHoc,
                course.GiaiDoan,
                course.DiemMucTieuToiDa,
                course.MoTa,
                course.GiaBanLe,
                PaymentCreationService.CourseValidDays,
                course.DuongDanAnhDaiDien))
            .ToListAsync(ct);

        return Ok(courses);
    }

    [HttpGet("{courseId:int}")]
    public async Task<ActionResult<PublicCourseDetailDto>> Detail(int courseId, CancellationToken ct)
    {
        var course = await db.KhoaHoc.AsNoTracking()
            .Where(item => item.MaKhoaHoc == courseId && item.TrangThai == "DANG_MO")
            .Select(item => new PublicCourseDetailDto(
                item.MaKhoaHoc,
                item.TenKhoaHoc,
                item.GiaiDoan,
                item.DiemMucTieuToiDa,
                item.MoTa,
                item.GiaBanLe,
                PaymentCreationService.CourseValidDays,
                item.DuongDanAnhDaiDien))
            .SingleOrDefaultAsync(ct);

        return course is null
            ? NotFound(new { code = "COURSE_NOT_FOUND", message = "Không tìm thấy khóa học đang mở." })
            : Ok(course);
    }
}
