using System.Security.Claims;
using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;

namespace STO123.Controllers;

public sealed record LectureCategoryRequest(string Name, string? Description, string Status);
public sealed record LectureRequest(string Title, string? Summary, string? Content, int? CategoryId, int? Level, int? PartId, string Status);
public sealed record LectureResourceNameRequest(string Name);
public sealed class LectureUploadRequest
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public IFormFile? File { get; set; }
}

[ApiController]
[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[Route("api/admin/content/lectures")]
[ServiceFilter(typeof(CourseBlobSyncFilter), Order = -1000)]
public sealed class AdminLecturesController(ToeicDbContext db, LectureResourceStorage storage,
    CourseBlobSynchronizer sync,
    ILogger<AdminLecturesController> logger) : ControllerBase
{
    private int AdminId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool Manager => User.IsInRole("ADMIN_QUAN_LY");
    private bool CanEdit(BaiGiang lecture) => Manager || lecture.MaAdminNoiDung == AdminId;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool StatusOk(string? status) => status is "DANG_MO" or "DA_DONG";
    private static IActionResult Bad(string message) => new BadRequestObjectResult(new { code = "INVALID_LECTURE", message });
    private static IActionResult Blocked(string message) => new ConflictObjectResult(new { code = "LECTURE_IN_USE", message });
    private async Task<IActionResult> Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return Ok(); }
        catch (DbUpdateException) { return Blocked("Dữ liệu đang được tham chiếu hoặc thứ tự đã tồn tại."); }
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await db.DanhMucBaiGiang.AsNoTracking()
        .OrderBy(x => x.TenDanhMuc).Select(x => new { id = x.MaDanhMuc, name = x.TenDanhMuc,
            description = x.MoTa, status = x.TrangThai, lectureCount = x.BaiGiang.Count }).ToListAsync(ct));

    [HttpGet("resource-capabilities")]
    public IActionResult ResourceCapabilities() => Ok(new { maxFileBytes = storage.MaxFileBytes,
        excelEnabled = storage.ExcelEnabled });

    [HttpPost("categories")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LECTURE_CATEGORY" })]
    public async Task<IActionResult> AddCategory(LectureCategoryRequest request, CancellationToken ct)
    {
        if (Clean(request.Name) is not { Length: > 0 and <= 64 } || Clean(request.Description)?.Length > 512 || !StatusOk(request.Status))
            return Bad("Danh mục không hợp lệ.");
        var row = new DanhMucBaiGiang { TenDanhMuc = request.Name.Trim(), MoTa = Clean(request.Description), TrangThai = request.Status };
        db.DanhMucBaiGiang.Add(row);
        var result = await Save(ct); return result is OkResult ? Created("/api/admin/content/lectures/categories", new { id = row.MaDanhMuc }) : result;
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> EditCategory(int id, LectureCategoryRequest request, CancellationToken ct)
    {
        if (Clean(request.Name) is not { Length: > 0 and <= 64 } || Clean(request.Description)?.Length > 512 || !StatusOk(request.Status))
            return Bad("Danh mục không hợp lệ.");
        var row = await db.DanhMucBaiGiang.FirstOrDefaultAsync(x => x.MaDanhMuc == id, ct);
        if (row is null) return NotFound();
        if (request.Status == "DA_DONG" && await db.BaiGiang.AnyAsync(x => x.MaDanhMuc == id && x.TrangThai == "DANG_MO" &&
            x.BuocLoTrinh.Any(), ct)) return Blocked("Danh mục còn bài giảng đang dùng trong lộ trình.");
        row.TenDanhMuc = request.Name.Trim(); row.MoTa = Clean(request.Description); row.TrangThai = request.Status;
        return await Save(ct);
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        var row = await db.DanhMucBaiGiang.FirstOrDefaultAsync(x => x.MaDanhMuc == id, ct);
        if (row is null) return NotFound();
        if (await db.BaiGiang.AnyAsync(x => x.MaDanhMuc == id, ct)) return Blocked("Danh mục còn bài giảng.");
        db.DanhMucBaiGiang.Remove(row); return await Save(ct);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 128) return Bad("Bộ lọc không hợp lệ.");
        var query = db.BaiGiang.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.TieuDe.Contains(search.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.MaBaiGiang).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { id = x.MaBaiGiang, title = x.TieuDe, summary = x.NoiDungTomTat,
                categoryId = x.MaDanhMuc, categoryName = x.MaDanhMucNavigation == null ? null : x.MaDanhMucNavigation.TenDanhMuc,
                level = x.CapDo, partId = x.MaPart, status = x.TrangThai, ownerId = x.MaAdminNoiDung,
                resourceCount = x.TaiNguyenBaiGiang.Count, courseStepCount = x.BuocLoTrinh.Count }).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("available")]
    public async Task<IActionResult> Available([FromQuery] string? search, CancellationToken ct)
    {
        if (search?.Length > 128) return Bad("Từ khóa quá dài.");
        var query = db.BaiGiang.AsNoTracking().Where(x => x.TrangThai == "DANG_MO" &&
            (x.MaDanhMuc == null || x.MaDanhMucNavigation!.TrangThai == "DANG_MO"));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.TieuDe.Contains(search.Trim()));
        return Ok(await query.OrderBy(x => x.TieuDe).ThenBy(x => x.MaBaiGiang).Take(100)
            .Select(x => new { id = x.MaBaiGiang, title = x.TieuDe }).ToListAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var row = await db.BaiGiang.AsNoTracking().Where(x => x.MaBaiGiang == id)
            .Select(x => new { id = x.MaBaiGiang, title = x.TieuDe, summary = x.NoiDungTomTat, content = x.NoiDung,
                categoryId = x.MaDanhMuc, level = x.CapDo, partId = x.MaPart, status = x.TrangThai,
                ownerId = x.MaAdminNoiDung, courseStepCount = x.BuocLoTrinh.Count,
                resources = x.TaiNguyenBaiGiang.OrderBy(r => r.ThuTu).Select(r => new {
                    id = r.MaTaiNguyen, name = r.TenTaiNguyen, kind = r.LoaiTaiNguyen, order = r.ThuTu, isArchived = r.IsArchived
                }).ToList() }).FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : Ok(row);
    }

    private async Task<IActionResult?> Validate(LectureRequest request, CancellationToken ct)
    {
        if (Clean(request.Title) is not { Length: > 0 and <= 128 } || Clean(request.Summary)?.Length > 512 ||
            request.Content?.Length > 100_000 || !StatusOk(request.Status) || request.Level is < 1 or > 3)
            return Bad("Thông tin bài giảng không hợp lệ.");
        if (request.CategoryId is { } category && !await db.DanhMucBaiGiang.AnyAsync(x => x.MaDanhMuc == category && x.TrangThai == "DANG_MO", ct))
            return Bad("Danh mục không khả dụng.");
        if (request.PartId is { } part && !await db.PartTOEIC.AnyAsync(x => x.MaPart == part, ct)) return Bad("Part không tồn tại.");
        return null;
    }

    [HttpPost]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LECTURE" })]
    public async Task<IActionResult> Add(LectureRequest request, CancellationToken ct)
    {
        if (AdminId <= 0) return Unauthorized();
        var invalid = await Validate(request, ct); if (invalid is not null) return invalid;
        var row = new BaiGiang { TieuDe = request.Title.Trim(), NoiDungTomTat = Clean(request.Summary), NoiDung = Clean(request.Content),
            MaDanhMuc = request.CategoryId, CapDo = request.Level, MaPart = request.PartId, TrangThai = request.Status,
            MaAdminNoiDung = AdminId, NgayTao = DateTime.Now };
        db.BaiGiang.Add(row); var result = await Save(ct);
        return result is OkResult ? Created($"/api/admin/content/lectures/{row.MaBaiGiang}", new { id = row.MaBaiGiang }) : result;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, LectureRequest request, CancellationToken ct)
    {
        var invalid = await Validate(request, ct); if (invalid is not null) return invalid;
        var row = await db.BaiGiang.FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (row is null) return NotFound(); if (!CanEdit(row)) return Forbid();
        if (request.Status == "DA_DONG" && await db.BuocLoTrinh.AnyAsync(x => x.MaBaiGiang == id, ct))
            return Blocked("Bài giảng còn được dùng trong lộ trình.");
        row.TieuDe = request.Title.Trim(); row.NoiDungTomTat = Clean(request.Summary); row.NoiDung = Clean(request.Content);
        row.MaDanhMuc = request.CategoryId; row.CapDo = request.Level; row.MaPart = request.PartId; row.TrangThai = request.Status;
        return await Save(ct);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var row = await db.BaiGiang.FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (row is null) return NotFound(); if (!CanEdit(row)) return Forbid();
        if (await db.BuocLoTrinh.AnyAsync(x => x.MaBaiGiang == id, ct) ||
            await db.TaiNguyenBaiGiang.AnyAsync(x => x.MaBaiGiang == id, ct)) return Blocked("Bài giảng còn bước hoặc tài nguyên.");
        db.BaiGiang.Remove(row); return await Save(ct);
    }

    [HttpPost("{id:int}/resources")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LECTURE_RESOURCE" })]
    [HttpPut("{id:int}/resources/{resourceId:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(510_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<IActionResult> Upload(int id, [FromForm] LectureUploadRequest request, CancellationToken ct, int? resourceId = null)
    {
        var lecture = await db.BaiGiang.FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        if (Clean(request.Name) is not { Length: > 0 and <= 128 } || request.File is null ||
            request.File.Length < 12 || request.File.Length > storage.MaxFileBytes ||
            !LectureResourceStorage.SafeFileName(request.File.FileName))
            return Bad("Tên hoặc dung lượng tệp không hợp lệ.");
        if (request.Kind == "EXCEL" && !storage.ExcelEnabled)
            return Conflict(new { code = "EXCEL_SCHEMA_PENDING", message = "Excel cần cập nhật ràng buộc LoaiTaiNguyen trước khi lưu." });
        if (resourceId.HasValue && await db.BuocLoTrinh.AnyAsync(x => x.MaBaiGiang == id, ct))
            return Conflict(new { code = "REPLACE_WITH_NEW_RESOURCE",
                message = "Hãy tải tài nguyên mới rồi lưu trữ tệp cũ để giữ đúng đường dẫn và lịch sử học." });
        TaiNguyenBaiGiang? row = null;
        if (resourceId.HasValue)
        {
            row = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.MaTaiNguyen == resourceId && x.MaBaiGiang == id, ct);
            if (row is null) return NotFound();
        }
        var tempPath = Path.Combine(Path.GetTempPath(), "sto123-lecture-" + Guid.NewGuid().ToString("N"));
        await using var input = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await request.File.CopyToAsync(input, ct);
        if (input.Length != request.File.Length || input.Length > storage.MaxFileBytes)
            return Bad("Dung lượng tệp không hợp lệ.");
        if (!LectureResourceStorage.TryValidate(request.Kind, request.File.FileName, request.File.ContentType,
            input, out var extension, out var contentType)) return Bad("Định dạng, MIME hoặc chữ ký tệp không hợp lệ.");
        string? coursePath = null;
        if (row is null)
        {
            var locations = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBaiGiang == id)
                .Select(s => new { StepId = s.MaBuoc, LessonId = s.MaLesson,
                    UnitId = s.MaLessonNavigation.MaUnit,
                    CourseId = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                    UnitOrder = s.MaLessonNavigation.MaUnitNavigation.ThuTu,
                    LessonOrder = s.MaLessonNavigation.ThuTu, s.TieuDe }).Take(2).ToListAsync(ct);
            if (locations.Count > 1) return Conflict(new { code = "SHARED_LECTURE_RESOURCE",
                message = "Bài giảng thuộc nhiều hoạt động. Hãy tạo bản sao riêng trước khi tải tài nguyên theo khóa học." });
            if (locations.Count == 1)
            {
                if (db.Database.CurrentTransaction is null ||
                    !HttpContext.Items.TryGetValue("LectureResourceSetPath", out var setter) ||
                    setter is not Func<string, CancellationToken, Task> setReservedPath)
                    return StatusCode(503, new { code = "RESOURCE_RESERVATION_REQUIRED" });
                var order = (await db.TaiNguyenBaiGiang.Where(x => x.MaBaiGiang == id)
                    .MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
                row = new TaiNguyenBaiGiang { MaBaiGiang = id, ThuTu = order,
                    TenTaiNguyen = request.File.FileName, LoaiTaiNguyen = request.Kind,
                    DuongDanTep = "pending/" + Guid.NewGuid().ToString("N") };
                db.TaiNguyenBaiGiang.Add(row);
                await db.SaveChangesAsync(ct);
                var location = locations[0];
                coursePath = LectureResourceStorage.CoursePath(location.CourseId, location.UnitOrder,
                    location.LessonOrder, location.TieuDe, location.StepId, row.MaTaiNguyen, request.File.FileName);
                await setReservedPath(coursePath, ct);
            }
        }
        string path;
        try { path = await storage.UploadAsync(input, extension, contentType, request.File.FileName, ct,
            coursePath ?? (HttpContext.Items.TryGetValue("LectureResourceReservedPath", out var reserved) ? reserved as string : null)); }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
        { logger.LogError(e, "Lecture resource upload failed for {LectureId}", id); return StatusCode(503, new { code = "MEDIA_UNAVAILABLE" }); }
        var oldPath = row?.DuongDanTep;
        var persisted = false;
        var sqlOutcomeUnknown = false;
        try
        {
            if (row is null)
            {
                var order = (await db.TaiNguyenBaiGiang.Where(x => x.MaBaiGiang == id).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
                row = new TaiNguyenBaiGiang { MaBaiGiang = id, ThuTu = order };
                db.TaiNguyenBaiGiang.Add(row);
            }
            row.TenTaiNguyen = request.File.FileName; row.LoaiTaiNguyen = request.Kind; row.DuongDanTep = path;
            sqlOutcomeUnknown = true;
            var saved = await Save(ct);
            sqlOutcomeUnknown = false;
            if (saved is not OkResult) return saved;
            persisted = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Lecture resource SQL outcome is uncertain for {LectureId} and {Path}", id, path);
            return StatusCode(503, new { code = "MEDIA_PERSISTENCE_UNCERTAIN",
                message = "Không xác định được kết quả lưu. Hãy tải lại danh sách tài nguyên trước khi thử lại." });
        }
        finally
        {
            if (!persisted && !sqlOutcomeUnknown)
            {
                try { await storage.DeleteAsync(path, CancellationToken.None); }
                catch (Exception e) { logger.LogWarning(e, "Unused lecture blob cleanup failed for {Path}", path); }
            }
        }
        if (oldPath is not null) await RemoveBlob(oldPath, ct);
        return Ok(new { id = row.MaTaiNguyen, name = row.TenTaiNguyen, kind = row.LoaiTaiNguyen, order = row.ThuTu });
    }

    [HttpPut("{id:int}/resources/{resourceId:int}/name")]
    public async Task<IActionResult> RenameResource(int id, int resourceId, LectureResourceNameRequest request, CancellationToken ct)
    {
        if (Clean(request.Name) is not { Length: > 0 and <= 128 }) return Bad("Tên tài nguyên không hợp lệ.");
        var lecture = await db.BaiGiang.AsNoTracking().FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        var row = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.MaBaiGiang == id && x.MaTaiNguyen == resourceId, ct);
        if (row is null) return NotFound();
        if ((row.DuongDanTep.StartsWith("course-", StringComparison.Ordinal) ||
            row.DuongDanTep.StartsWith("courses/course-", StringComparison.Ordinal) ||
            row.DuongDanTep.StartsWith("lecture-resources/", StringComparison.Ordinal) &&
            row.DuongDanTep["lecture-resources/".Length..].Contains('/')) &&
            !string.Equals(request.Name, row.TenTaiNguyen, StringComparison.Ordinal))
            return Bad("Tên tệp gốc phải được giữ nguyên. Hãy tải tệp mới nếu cần đổi tên.");
        row.TenTaiNguyen = request.Name.Trim();
        return await Save(ct);
    }

    [HttpPost("{id:int}/resources/order")]
    public async Task<IActionResult> OrderResources(int id, ContentOrderRequest request, CancellationToken ct)
    {
        var lecture = await db.BaiGiang.AsNoTracking().FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        if (request.Ids is null || request.Ids.Length != request.Ids.Distinct().Count())
            return Bad("Danh sách thứ tự không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var rows = await db.TaiNguyenBaiGiang.Where(x => x.MaBaiGiang == id).ToListAsync(ct);
        if (rows.Count != request.Ids.Length || rows.Any(x => !request.Ids.Contains(x.MaTaiNguyen)))
            return Blocked("Danh sách đã thay đổi; tải lại trước khi sắp xếp.");
        if (rows.Count == 0) { await tx.CommitAsync(ct); return Ok(); }
        var max = rows.Max(x => x.ThuTu);
        if (max > int.MaxValue - rows.Count - 1) return Blocked("Thứ tự vượt giới hạn.");
        try
        {
            for (var i = 0; i < rows.Count; i++) rows[i].ThuTu = max + i + 1;
            await db.SaveChangesAsync(ct);
            foreach (var row in rows) row.ThuTu = Array.IndexOf(request.Ids, row.MaTaiNguyen) + 1;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); return Ok();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Blocked("Thứ tự đã thay đổi đồng thời; tải lại và thử lại.");
        }
    }

    [HttpDelete("{id:int}/resources/{resourceId:int}")]
    public async Task<IActionResult> DeleteResource(int id, int resourceId, CancellationToken ct,
        [FromQuery] bool confirmWithProgress = false, [FromQuery] int expectedProgress = -1)
    {
        var lecture = await db.BaiGiang.AsNoTracking().FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        var row = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.MaTaiNguyen == resourceId && x.MaBaiGiang == id, ct);
        if (row is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var progressRecords = await db.TienDoBuocLoTrinh.CountAsync(p =>
            db.BuocLoTrinh.Any(s => s.MaBuoc == p.MaBuoc && s.MaBaiGiang == id), ct);
        if (progressRecords > 0 && !confirmWithProgress) return Conflict(new { code = "RESOURCE_DELETE_CONFIRM_REQUIRED",
            progressRecords, message = "Tệp thuộc nội dung đã có tiến độ. Hãy lưu trữ hoặc xác nhận xóa tệp vĩnh viễn." });
        if (progressRecords > 0 && expectedProgress != progressRecords) return Conflict(new { code = "RESOURCE_DELETE_IMPACT_CHANGED",
            progressRecords, message = "Số bản ghi tiến độ đã thay đổi. Kiểm tra và xác nhận lại." });
        var path = row.DuongDanTep;
        var courseIds = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBaiGiang == id)
            .Select(s => s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc).Distinct().ToListAsync(ct);
        foreach (var courseId in courseIds)
            await sync.TrackRemoval(courseId, resourceId, path, ct);
        db.TaiNguyenBaiGiang.Remove(row);
        var saved = await Save(ct); if (saved is not OkResult) return saved;
        await tx.CommitAsync(ct);
        if (courseIds.Count == 0 && !await db.TaiNguyenBaiGiang.AnyAsync(x => x.DuongDanTep == path, ct))
            await RemoveBlob(path, ct);
        return Ok();
    }

    [HttpGet("{id:int}/resources/{resourceId:int}/delete-impact")]
    public async Task<IActionResult> ResourceDeleteImpact(int id, int resourceId, CancellationToken ct)
    {
        var lecture = await db.BaiGiang.AsNoTracking().FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        if (!await db.TaiNguyenBaiGiang.AnyAsync(x => x.MaTaiNguyen == resourceId && x.MaBaiGiang == id, ct)) return NotFound();
        var progressRecords = await db.TienDoBuocLoTrinh.CountAsync(p =>
            db.BuocLoTrinh.Any(s => s.MaBuoc == p.MaBuoc && s.MaBaiGiang == id), ct);
        return Ok(new { progressRecords, attempts = 0, checks = 0 });
    }

    [HttpPut("{id:int}/resources/{resourceId:int}/archive")]
    public async Task<IActionResult> ArchiveResource(int id, int resourceId, ContentArchiveRequest request, CancellationToken ct)
    {
        var lecture = await db.BaiGiang.AsNoTracking().FirstOrDefaultAsync(x => x.MaBaiGiang == id, ct);
        if (lecture is null) return NotFound(); if (!CanEdit(lecture)) return Forbid();
        var row = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(x => x.MaTaiNguyen == resourceId && x.MaBaiGiang == id, ct);
        if (row is null) return NotFound();
        row.IsArchived = request.IsArchived;
        var result = await Save(ct);
        return result is OkResult ? Ok(new { id = resourceId, isArchived = row.IsArchived }) : result;
    }

    [HttpGet("{id:int}/resources/{resourceId:int}/file")]
    public async Task<IActionResult> ResourceFile(int id, int resourceId, [FromQuery] bool download, CancellationToken ct)
    {
        var row = await db.TaiNguyenBaiGiang.AsNoTracking()
            .Where(x => x.MaBaiGiang == id && x.MaTaiNguyen == resourceId)
            .Select(x => new { x.DuongDanTep, x.TenTaiNguyen }).FirstOrDefaultAsync(ct);
        if (row is null) return NotFound();
        try
        {
            var file = await storage.OpenAsync(row.DuongDanTep, ct);
            return file is null ? NotFound() : File(file.Value.Stream, file.Value.ContentType,
                download ? LectureResourceStorage.DownloadName(row.TenTaiNguyen, row.DuongDanTep) : null,
                enableRangeProcessing: true);
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
        { return StatusCode(503, new { code = "MEDIA_UNAVAILABLE" }); }
    }

    private async Task RemoveBlob(string path, CancellationToken ct)
    {
        if (await db.TaiNguyenBaiGiang.AsNoTracking().AnyAsync(x => x.DuongDanTep == path, ct)) return;
        try { await storage.DeleteAsync(path, ct); }
        catch (Exception e) { logger.LogWarning(e, "Unused lecture blob cleanup failed for {Path}", path); }
    }
}
