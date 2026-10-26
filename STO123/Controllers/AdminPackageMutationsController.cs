using System.Data;
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
public sealed class AdminPackageMutationsController(ToeicDbContext db) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses = ["DANG_MO", "DA_DONG"];
    private const decimal MaxPrice = 9_999_999_999m;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminPackageWriteRequest request, CancellationToken ct)
    {
        var input = Normalize(request);
        var errors = Validate(input);
        if (errors.Count > 0) return ValidationFailure(errors);
        var courses = await Courses(input.CourseIds!, ct);
        if (courses.Count != input.CourseIds!.Count) return InvalidCourses();
        if (input.Status == "DANG_MO" && courses.Any(course => course.TrangThai != "DANG_MO"))
            return ClosedCourseConflict();

        var package = new GoiHoc
        {
            TenGoiHoc = input.Name, Gia = input.Price, SoNgaySuDung = input.ValidDays,
            MoTa = input.Description, TrangThai = input.Status, NgayTao = DateTime.Now,
            MaKhoaHoc = courses
        };
        db.GoiHoc.Add(package);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(AdminPackagesController.Detail), "AdminPackages",
            new { packageId = package.MaGoiHoc }, Mutation(package));
    }

    [HttpPut("{packageId:int}")]
    public async Task<IActionResult> Update(int packageId, [FromBody] AdminPackageWriteRequest request,
        CancellationToken ct)
    {
        var input = Normalize(request);
        var errors = Validate(input);
        if (errors.Count > 0) return ValidationFailure(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var package = await db.GoiHoc.Include(item => item.MaKhoaHoc)
            .SingleOrDefaultAsync(item => item.MaGoiHoc == packageId, ct);
        if (package is null) return PackageNotFound();
        var courses = await Courses(input.CourseIds!, ct);
        if (courses.Count != input.CourseIds!.Count) return InvalidCourses();
        if (input.Status == "DANG_MO" && courses.Any(course => course.TrangThai != "DANG_MO"))
            return ClosedCourseConflict();

        var oldIds = package.MaKhoaHoc.Select(course => course.MaKhoaHoc).Order().ToArray();
        var newIds = input.CourseIds.Order().ToArray();
        if (!oldIds.SequenceEqual(newIds) && await db.DangKyGoiHoc.AsNoTracking()
            .AnyAsync(item => item.MaGoiHoc == packageId, ct))
            return Conflict(new AdminPackageConflictDto("PACKAGE_COURSES_IMMUTABLE",
                "Không thể thay đổi khóa học của gói đã có đăng ký vì sẽ làm thay đổi quyền học lịch sử.",
                ["Gói học đã có đăng ký hoặc giao dịch liên quan."]));

        package.TenGoiHoc = input.Name;
        package.Gia = input.Price;
        package.SoNgaySuDung = input.ValidDays;
        package.MoTa = input.Description;
        package.TrangThai = input.Status;
        if (!oldIds.SequenceEqual(newIds))
        {
            package.MaKhoaHoc.Clear();
            foreach (var course in courses) package.MaKhoaHoc.Add(course);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(Mutation(package));
    }

    [HttpPatch("{packageId:int}/status")]
    public async Task<IActionResult> ChangeStatus(int packageId,
        [FromBody] AdminChangePackageStatusRequest request, CancellationToken ct)
    {
        var status = request.Status?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái gói học không hợp lệ." });
        var package = await db.GoiHoc.Include(item => item.MaKhoaHoc)
            .SingleOrDefaultAsync(item => item.MaGoiHoc == packageId, ct);
        if (package is null) return PackageNotFound();
        if (status == "DANG_MO" && (package.Gia <= 0 || package.Gia > MaxPrice ||
            package.Gia != decimal.Truncate(package.Gia) || package.SoNgaySuDung <= 0 ||
            package.MaKhoaHoc.Count == 0 || package.MaKhoaHoc.Any(course => course.TrangThai != "DANG_MO")))
            return Conflict(new { code = "PACKAGE_NOT_SELLABLE",
                message = "Không thể mở bán: giá, thời hạn hoặc khóa học liên kết chưa hợp lệ." });
        if (package.TrangThai != status)
        {
            package.TrangThai = status;
            await db.SaveChangesAsync(ct);
        }
        return Ok(Mutation(package));
    }

    [HttpDelete("{packageId:int}")]
    public async Task<IActionResult> Delete(int packageId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var package = await db.GoiHoc.Include(item => item.MaKhoaHoc)
            .SingleOrDefaultAsync(item => item.MaGoiHoc == packageId, ct);
        if (package is null) return PackageNotFound();
        var enrollmentCount = await db.DangKyGoiHoc.AsNoTracking().CountAsync(item => item.MaGoiHoc == packageId, ct);
        if (enrollmentCount > 0)
        {
            var paymentCount = await db.GiaoDichThanhToan.AsNoTracking()
                .CountAsync(payment => payment.MaDangKyNavigation.MaGoiHoc == packageId, ct);
            var blockers = new List<string> { $"{enrollmentCount} đăng ký gói học" };
            if (paymentCount > 0) blockers.Add($"{paymentCount} giao dịch thanh toán");
            return Conflict(new AdminPackageConflictDto("PACKAGE_HAS_HISTORY",
                "Không thể xóa gói học vì đang có lịch sử kinh doanh cần được bảo toàn.", blockers));
        }

        package.MaKhoaHoc.Clear();
        db.GoiHoc.Remove(package);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Conflict(new AdminPackageConflictDto("PACKAGE_HAS_HISTORY",
                "Không thể xóa gói học vì dữ liệu liên quan vừa thay đổi.", ["Hãy tải lại và thử lại."]));
        }
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    private async Task<List<KhoaHoc>> Courses(IReadOnlyList<int> ids, CancellationToken ct) =>
        await db.KhoaHoc.Where(course => ids.Contains(course.MaKhoaHoc)).ToListAsync(ct);

    private static AdminPackageWriteRequest Normalize(AdminPackageWriteRequest request) => new()
    {
        Name = request.Name?.Trim() ?? string.Empty, Price = request.Price, ValidDays = request.ValidDays,
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        Status = request.Status?.Trim().ToUpperInvariant() ?? string.Empty,
        CourseIds = request.CourseIds?.ToArray()
    };

    private static Dictionary<string, string> Validate(AdminPackageWriteRequest input)
    {
        var errors = new Dictionary<string, string>();
        if (input.Name.Length is < 1 or > 64) errors["name"] = "Tên gói học phải có từ 1 đến 64 ký tự.";
        if (input.Price <= 0 || input.Price > MaxPrice || input.Price != decimal.Truncate(input.Price))
            errors["price"] = "Giá phải là số nguyên dương và không vượt quá 9.999.999.999 đồng.";
        if (input.ValidDays <= 0) errors["validDays"] = "Thời hạn sử dụng phải lớn hơn 0 ngày.";
        if (input.Description?.Length > 512) errors["description"] = "Mô tả tối đa 512 ký tự.";
        if (!SupportedStatuses.Contains(input.Status)) errors["status"] = "Trạng thái gói học không hợp lệ.";
        if (input.CourseIds is null || input.CourseIds.Count == 0 || input.CourseIds.Any(id => id <= 0))
            errors["courseIds"] = "Gói học phải có ít nhất một khóa học hợp lệ.";
        if (input.CourseIds is not null && input.CourseIds.Count != input.CourseIds.Distinct().Count())
            errors["courseIds"] = "Danh sách khóa học không được trùng lặp.";
        return errors;
    }

    private IActionResult ValidationFailure(Dictionary<string, string> errors) =>
        BadRequest(new { code = "VALIDATION_FAILED", message = "Thông tin gói học chưa hợp lệ.", errors });
    private IActionResult InvalidCourses() => BadRequest(new { code = "INVALID_COURSES",
        message = "Một hoặc nhiều khóa học được chọn không tồn tại." });
    private IActionResult ClosedCourseConflict() => Conflict(new { code = "CLOSED_COURSE",
        message = "Gói đang mở bán chỉ được liên kết với các khóa học đang mở." });
    private NotFoundObjectResult PackageNotFound() =>
        NotFound(new { code = "PACKAGE_NOT_FOUND", message = "Không tìm thấy gói học." });
    private static AdminPackageMutationDto Mutation(GoiHoc package) => new(package.MaGoiHoc,
        package.TenGoiHoc, package.Gia, package.SoNgaySuDung, package.TrangThai,
        package.MaKhoaHoc.Select(course => course.MaKhoaHoc).Order().ToArray());
}
