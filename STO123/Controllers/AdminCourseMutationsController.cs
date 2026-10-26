using System.Security.Claims;
using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Admin;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/courses")]
public sealed class AdminCourseMutationsController(
    ToeicDbContext db,
    CourseImageStorage images,
    ILogger<AdminCourseMutationsController> logger) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses = ["DANG_MO", "DA_DONG"];
    private const decimal MaxRetailPrice = 9_999_999_999m;
    private const int MaxImageBytes = 5 * 1024 * 1024;

    [HttpPost]
    public Task<IActionResult> Create([FromBody] AdminCourseWriteRequest request, CancellationToken ct) =>
        CreateCore(request, null, ct);

    [HttpPost("with-image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageBytes + 128 * 1024)]
    public Task<IActionResult> CreateWithImage([FromForm] AdminCourseImageWriteRequest request, CancellationToken ct) =>
        CreateCore(request.ToCourseRequest(), request.File, ct);

    private async Task<IActionResult> CreateCore(AdminCourseWriteRequest request, IFormFile? file, CancellationToken ct)
    {
        var input = Normalize(request);
        var errors = Validate(input);
        if (errors.Count > 0) return CourseValidationProblem(errors);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0)
            return Unauthorized();
        var image = await ValidateImage(file, errors, ct);
        if (errors.Count > 0) return CourseValidationProblem(errors);

        var uploadedPath = await UploadImage(image, null, ct);
        if (image is not null && uploadedPath is null) return ImageStorageUnavailable();

        var course = new KhoaHoc
        {
            TenKhoaHoc = input.Name,
            GiaiDoan = input.Stage,
            DiemMucTieuToiDa = input.MaxTargetScore,
            GiaBanLe = input.RetailPrice,
            MoTa = input.Description,
            TrangThai = input.Status,
            DuongDanAnhDaiDien = uploadedPath ?? input.CoverImageUrl,
            MaAdminNoiDung = adminId,
            NgayTao = DateTime.Now
        };
        db.KhoaHoc.Add(course);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await DeleteUploadedAfterFailedSave(uploadedPath);
            throw;
        }
        catch
        {
            await DeleteUploadedAfterFailedSave(uploadedPath);
            throw;
        }

        return CreatedAtAction(nameof(AdminCoursesController.Detail), "AdminCourses",
            new { courseId = course.MaKhoaHoc }, Mutation(course));
    }

    [HttpPut("{courseId:int}")]
    public Task<IActionResult> Update(int courseId, [FromBody] AdminCourseWriteRequest request,
        CancellationToken ct) => UpdateCore(courseId, request, null, ct);

    [HttpPut("{courseId:int}/with-image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageBytes + 128 * 1024)]
    public Task<IActionResult> UpdateWithImage(int courseId, [FromForm] AdminCourseImageWriteRequest request,
        CancellationToken ct) => UpdateCore(courseId, request.ToCourseRequest(), request.File, ct);

    private async Task<IActionResult> UpdateCore(int courseId, AdminCourseWriteRequest request,
        IFormFile? file, CancellationToken ct)
    {
        var input = Normalize(request);
        var errors = Validate(input);
        if (errors.Count > 0) return CourseValidationProblem(errors);

        var course = await db.KhoaHoc.FirstOrDefaultAsync(item => item.MaKhoaHoc == courseId, ct);
        if (course is null) return CourseNotFound();
        var image = await ValidateImage(file, errors, ct);
        if (errors.Count > 0) return CourseValidationProblem(errors);

        var uploadedPath = await UploadImage(image, courseId, ct);
        if (image is not null && uploadedPath is null) return ImageStorageUnavailable();

        var previousImage = course.DuongDanAnhDaiDien;
        course.TenKhoaHoc = input.Name;
        course.GiaiDoan = input.Stage;
        course.DiemMucTieuToiDa = input.MaxTargetScore;
        course.GiaBanLe = input.RetailPrice;
        course.MoTa = input.Description;
        course.TrangThai = input.Status;
        course.DuongDanAnhDaiDien = uploadedPath ?? input.CoverImageUrl;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await DeleteUploadedAfterFailedSave(uploadedPath);
            throw;
        }
        catch
        {
            await DeleteUploadedAfterFailedSave(uploadedPath);
            throw;
        }

        await DeleteIfUnused(previousImage, course.DuongDanAnhDaiDien, ct);
        return Ok(Mutation(course));
    }

    [HttpPatch("{courseId:int}/status")]
    public async Task<IActionResult> ChangeStatus(int courseId,
        [FromBody] AdminChangeCourseStatusRequest request, CancellationToken ct)
    {
        var status = request.Status?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái khóa học không hợp lệ." });

        var course = await db.KhoaHoc.FirstOrDefaultAsync(item => item.MaKhoaHoc == courseId, ct);
        if (course is null) return CourseNotFound();
        if (course.TrangThai != status)
        {
            course.TrangThai = status;
            await db.SaveChangesAsync(ct);
        }
        return Ok(Mutation(course));
    }

    [HttpDelete("{courseId:int}")]
    public async Task<IActionResult> Delete(int courseId, CancellationToken ct)
    {
        var course = await db.KhoaHoc.FirstOrDefaultAsync(item => item.MaKhoaHoc == courseId, ct);
        if (course is null) return CourseNotFound();

        var blockers = await DeleteBlockers(courseId, ct);
        if (blockers.Count > 0) return DeleteConflict(blockers);

        var previousImage = course.DuongDanAnhDaiDien;
        db.KhoaHoc.Remove(course);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return DeleteConflict(["Dữ liệu liên quan vừa thay đổi; hãy tải lại khóa học và thử lại."]);
        }
        await DeleteIfUnused(previousImage, null, ct);
        return NoContent();
    }

    private async Task<List<string>> DeleteBlockers(int courseId, CancellationToken ct)
    {
        var blockers = new List<string>();
        if (await db.UnitKhoaHoc.AsNoTracking().AnyAsync(item => item.MaKhoaHoc == courseId, ct))
            blockers.Add("Đơn vị, bài học hoặc nội dung lộ trình");
        if (await db.GoiHoc.AsNoTracking().AnyAsync(package =>
            package.MaKhoaHoc.Any(course => course.MaKhoaHoc == courseId), ct))
            blockers.Add("Gói học đang liên kết");
        if (await db.ChiTietDangKyKhoaHoc.AsNoTracking().AnyAsync(item => item.MaKhoaHoc == courseId, ct))
            blockers.Add("Đăng ký khóa học và lịch sử mua hàng");
        return blockers;
    }

    private async Task<ValidatedImage?> ValidateImage(IFormFile? file,
        Dictionary<string, string> errors, CancellationToken ct)
    {
        if (file is null) return null;
        if (file.Length is <= 0 or > MaxImageBytes)
        {
            errors["coverImageUrl"] = "Ảnh phải có dung lượng từ 1 byte đến 5 MB.";
            return null;
        }

        var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        if (!AvatarFileValidator.TryValidate(file.FileName, file.ContentType,
                stream.GetBuffer().AsSpan(0, (int)stream.Length), out var extension, out var contentType))
        {
            await stream.DisposeAsync();
            errors["coverImageUrl"] = "Ảnh phải là tệp JPEG, PNG hoặc WebP hợp lệ và khớp với nội dung thực tế.";
            return null;
        }
        return new ValidatedImage(stream, extension, contentType);
    }

    private async Task<string?> UploadImage(ValidatedImage? image, int? courseId, CancellationToken ct)
    {
        if (image is null) return null;
        try
        {
            image.Stream.Position = 0;
            return await images.UploadAsync(image.Stream, image.Extension, image.ContentType, ct);
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            logger.LogError(error, "Course image upload failed for course {CourseId}.", courseId);
            return null;
        }
        finally
        {
            await image.Stream.DisposeAsync();
        }
    }

    private async Task DeleteUploadedAfterFailedSave(string? path)
    {
        if (path is null) return;
        try { await images.DeleteAsync(path, CancellationToken.None); }
        catch (Exception error) when (IsStorageFailure(error))
        {
            logger.LogWarning(error, "Could not remove unused course image {ImagePath} after a failed save.", path);
        }
    }

    private async Task DeleteIfUnused(string? previousPath, string? currentPath, CancellationToken ct)
    {
        if (previousPath is null || previousPath == currentPath ||
            !CourseImageStorage.TryReference(previousPath, out _)) return;
        if (await db.KhoaHoc.AsNoTracking().AnyAsync(course => course.DuongDanAnhDaiDien == previousPath, ct)) return;
        try { await images.DeleteAsync(previousPath, ct); }
        catch (Exception error) when (IsStorageFailure(error))
        {
            logger.LogWarning(error, "Could not remove superseded course image {ImagePath}.", previousPath);
        }
    }

    private static AdminCourseWriteRequest Normalize(AdminCourseWriteRequest request) => new()
    {
        Name = request.Name?.Trim() ?? string.Empty,
        Stage = request.Stage,
        MaxTargetScore = request.MaxTargetScore,
        RetailPrice = request.RetailPrice,
        Description = NullIfWhiteSpace(request.Description),
        Status = request.Status?.Trim().ToUpperInvariant() ?? string.Empty,
        CoverImageUrl = NullIfWhiteSpace(request.CoverImageUrl)
    };

    private static Dictionary<string, string> Validate(AdminCourseWriteRequest input)
    {
        var errors = new Dictionary<string, string>();
        if (input.Name.Length is < 1 or > 128)
            errors["name"] = "Tên khóa học phải có từ 1 đến 128 ký tự.";
        if (input.Stage is < 1 or > 3)
            errors["stage"] = "Giai đoạn phải từ 1 đến 3.";
        if (input.MaxTargetScore is < 10 or > 990)
            errors["maxTargetScore"] = "Điểm mục tiêu tối đa phải từ 10 đến 990.";
        if (input.RetailPrice is not null &&
            (input.RetailPrice <= 0 || input.RetailPrice > MaxRetailPrice ||
             input.RetailPrice != decimal.Truncate(input.RetailPrice.Value)))
            errors["retailPrice"] = "Giá bán lẻ phải là số nguyên dương và không vượt quá 9.999.999.999 đồng.";
        if (input.Description?.Length > 512)
            errors["description"] = "Mô tả tối đa 512 ký tự.";
        if (!SupportedStatuses.Contains(input.Status))
            errors["status"] = "Trạng thái chỉ có thể là Đang mở hoặc Đã đóng.";
        if (input.CoverImageUrl?.Length > 512 || !SafeImagePath(input.CoverImageUrl))
            errors["coverImageUrl"] = "Đường dẫn ảnh tối đa 512 ký tự và phải là đường dẫn tương đối hoặc URL HTTP(S).";
        return errors;
    }

    private IActionResult CourseValidationProblem(Dictionary<string, string> errors) =>
        BadRequest(new { code = "VALIDATION_FAILED", message = "Thông tin khóa học chưa hợp lệ.", errors });

    private ObjectResult ImageStorageUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = "IMAGE_STORAGE_UNAVAILABLE", message = "Không thể lưu ảnh khóa học lúc này. Vui lòng thử lại." });

    private static bool IsStorageFailure(Exception error) =>
        error is RequestFailedException or AuthenticationFailedException;

    private static bool SafeImagePath(string? value)
    {
        if (value is null) return true;
        if (value.StartsWith('/')) return !value.StartsWith("//", StringComparison.Ordinal);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private NotFoundObjectResult CourseNotFound() =>
        NotFound(new { code = "COURSE_NOT_FOUND", message = "Không tìm thấy khóa học." });

    private ConflictObjectResult DeleteConflict(IReadOnlyList<string> blockers) =>
        Conflict(new AdminDeleteCourseBlockedDto(
            "COURSE_HAS_RELATIONSHIPS",
            "Không thể xóa khóa học vì đang có dữ liệu cần được bảo toàn.",
            blockers));

    private static AdminCourseMutationDto Mutation(KhoaHoc course) => new(
        course.MaKhoaHoc,
        course.TenKhoaHoc,
        course.GiaiDoan,
        course.DiemMucTieuToiDa,
        course.GiaBanLe,
        course.TrangThai);

    private sealed record ValidatedImage(MemoryStream Stream, string Extension, string ContentType);
}
