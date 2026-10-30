using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using STO123.Models;
using STO123.Services.Auth;
using STO123.Services.Admin;

namespace STO123.Controllers;

public sealed record ContentNameRequest(string Name, string? Description);
public sealed record ContentStepRequest(string Title, string ActivityType, int ActivityId);
public sealed record ContentOrderRequest(int[] Ids);
public sealed record ContentArchiveRequest(bool IsArchived);
public sealed record ContentDeleteImpact(int ProgressRecords, int Attempts, int Checks, int AffectedLearners = 0)
{
    public bool HasHistory => ProgressRecords + Attempts + Checks > 0;
}
public sealed record CourseLectureRequest(string Title, string? Summary, string? Content, int? CategoryId, int? Level, int? PartId);
public sealed record CourseQuestionRequest(string Content, string? Passage, string A, string B, string C, string D,
    string Correct, string? Explanation);
public sealed record CourseExerciseRequest(string Title, CourseQuestionRequest[] Questions, bool Publish);

[ApiController]
[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[Route("api/admin/content")]
[ServiceFilter(typeof(CourseBlobSyncFilter), Order = -1000)]
public sealed class AdminCurriculumController(ToeicDbContext db) : ControllerBase
{
    private static IActionResult Invalid(string message) => new BadRequestObjectResult(new { code = "INVALID_CONTENT", message });
    private static IActionResult Conflict(string message) => new ConflictObjectResult(new { code = "CONTENT_IN_USE", message });
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidName(string? name) => Clean(name) is { Length: > 0 and <= 128 };
    private async Task<IActionResult> Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return Ok(); }
        catch (DbUpdateException) { return Conflict("Thứ tự đã tồn tại hoặc dữ liệu đang được tham chiếu. Tải lại và thử lại."); }
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(new {
        courses = await db.KhoaHoc.CountAsync(ct),
        units = await db.UnitKhoaHoc.CountAsync(ct),
        lessons = await db.LessonKhoaHoc.CountAsync(ct),
        steps = await db.BuocLoTrinh.CountAsync(ct),
        lectures = await db.BaiGiang.CountAsync(ct)
    });

    [HttpGet("courses/{courseId:int}/progress-summary")]
    public async Task<IActionResult> ProgressSummary(int courseId, CancellationToken ct)
    {
        if (!await db.KhoaHoc.AnyAsync(x => x.MaKhoaHoc == courseId, ct)) return NotFound();
        var enrollmentIds = await db.DangKyGoiHoc.AsNoTracking().Where(e =>
            e.TrangThai == "DANG_SU_DUNG" && e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG") &&
            (e.MaGoiHoc != null && e.MaGoiHocNavigation.MaKhoaHoc.Any(c => c.MaKhoaHoc == courseId) ||
             e.MaGoiHoc == null && e.ChiTietDangKyKhoaHoc.Any(c => c.MaKhoaHoc == courseId)))
            .Select(e => e.MaDangKy).ToArrayAsync(ct);
        var activeStepIds = await db.BuocLoTrinh.AsNoTracking().Where(s =>
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived)
            .Select(s => s.MaBuoc).ToArrayAsync(ct);
        var progress = await db.TienDoBuocLoTrinh.AsNoTracking().Where(p =>
            enrollmentIds.Contains(p.MaDangKy) && activeStepIds.Contains(p.MaBuoc))
            .Select(p => new { p.MaDangKy, p.TrangThai }).ToListAsync(ct);
        return Ok(new { enrolled = enrollmentIds.Length,
            learnersStarted = progress.Select(p => p.MaDangKy).Distinct().Count(),
            completedActivityRecords = progress.Count(p => p.TrangThai == "HOAN_THANH"),
            activeActivities = activeStepIds.Length });
    }

    [HttpGet("courses/{courseId:int}/outline")]
    public async Task<IActionResult> Outline(int courseId, CancellationToken ct)
    {
        var course = await db.KhoaHoc.AsNoTracking().Where(x => x.MaKhoaHoc == courseId)
            .Select(x => new { id = x.MaKhoaHoc, name = x.TenKhoaHoc, description = x.MoTa,
                coverImageUrl = x.DuongDanAnhDaiDien, stage = x.GiaiDoan, status = x.TrangThai }).FirstOrDefaultAsync(ct);
        if (course is null) return NotFound();
        var units = await db.UnitKhoaHoc.AsNoTracking().Where(x => x.MaKhoaHoc == courseId)
            .OrderBy(x => x.ThuTu).Select(x => new {
                id = x.MaUnit, name = x.TenUnit, description = x.MoTa, order = x.ThuTu, isArchived = x.IsArchived,
                lessons = x.LessonKhoaHoc.OrderBy(l => l.ThuTu).Select(l => new {
                    id = l.MaLesson, name = l.TenLesson, description = l.MoTa, order = l.ThuTu, isArchived = l.IsArchived,
                    steps = l.BuocLoTrinh.OrderBy(s => s.ThuTu).Select(s => new {
                        id = s.MaBuoc, title = s.TieuDe, order = s.ThuTu, isArchived = s.IsArchived,
                        activityType = s.MaBaiGiang != null ? "LECTURE" : s.MaChuDe != null ? "VOCABULARY" : s.MaBaiTap != null ? "EXERCISE" : "EXAM",
                        activityId = s.MaBaiGiang ?? s.MaChuDe ?? s.MaBaiTap ?? s.MaDeThi
                    }).ToList()
                }).ToList()
            }).ToListAsync(ct);
        return Ok(new { course, units });
    }

    [HttpGet("topics")]
    public async Task<IActionResult> Topics(CancellationToken ct) => Ok(await db.ChuDe.AsNoTracking()
        .Where(x => x.TrangThai == "DANG_MO")
        .OrderBy(x => x.TenChuDe).Select(x => new { id = x.MaChuDe, name = x.TenChuDe }).ToListAsync(ct));

    [HttpGet("courses/{courseId:int}/steps/{stepId:int}/preview")]
    public async Task<IActionResult> PreviewStep(int courseId, int stepId, CancellationToken ct)
    {
        var step = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaBuoc == stepId &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)
            .Select(x => new { x.TieuDe, x.MaBaiGiang, x.MaChuDe, x.MaBaiTap }).FirstOrDefaultAsync(ct);
        if (step is null) return NotFound();
        if (step.MaBaiGiang is { } lectureId)
        {
            var lecture = await db.BaiGiang.AsNoTracking().Where(x => x.MaBaiGiang == lectureId)
                .Select(x => new { id = x.MaBaiGiang, title = x.TieuDe, summary = x.NoiDungTomTat,
                    content = x.NoiDung, status = x.TrangThai,
                    resources = x.TaiNguyenBaiGiang.Where(r => !r.IsArchived).OrderBy(r => r.ThuTu).Select(r => new {
                        id = r.MaTaiNguyen, name = r.TenTaiNguyen, kind = r.LoaiTaiNguyen
                    }).ToList() }).FirstOrDefaultAsync(ct);
            return lecture is null ? NotFound() : Ok(new { title = step.TieuDe, type = "LECTURE", lecture });
        }
        if (step.MaChuDe is { } topicId)
        {
            var topic = await db.ChuDe.AsNoTracking().Where(x => x.MaChuDe == topicId)
                .Select(x => new { title = x.TenChuDe, description = x.MoTa, status = x.TrangThai,
                    words = x.TuVungChuDe.OrderBy(w => w.MaTuVung).Select(w => new {
                        word = w.MaTuVungNavigation.TuVung1, meaning = w.MaTuVungNavigation.Nghia
                    }).ToList() }).FirstOrDefaultAsync(ct);
            return topic is null ? NotFound() : Ok(new { title = step.TieuDe, type = "VOCABULARY", topic });
        }
        if (step.MaBaiTap is { } exerciseId)
        {
            var version = await db.PhienBanBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaBaiTap == exerciseId)
                .OrderByDescending(x => x.SoPhienBan).FirstOrDefaultAsync(ct);
            if (version is null) return NotFound();
            var questions = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaPhienBan == version.MaPhienBan)
                .OrderBy(x => x.ThuTu).Select(x => new { content = x.NoiDung, passage = x.NguLieu,
                    a = x.PhuongAnA, b = x.PhuongAnB, c = x.PhuongAnC, d = x.PhuongAnD,
                    correct = x.PhuongAnDung, explanation = x.GiaiThich, order = x.ThuTu }).ToListAsync(ct);
            return Ok(new { title = step.TieuDe, type = "EXERCISE", status = version.TrangThai, questions });
        }
        return Conflict("Loại hoạt động này chưa hỗ trợ xem trước.");
    }

    [HttpPost("courses/{courseId:int}/units")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_UNIT" })]
    public async Task<IActionResult> AddUnit(int courseId, ContentNameRequest request, CancellationToken ct)
    {
        if (!ValidName(request.Name) || Clean(request.Description)?.Length > 512) return Invalid("Tên Unit hoặc mô tả không hợp lệ.");
        if (!await db.KhoaHoc.AnyAsync(x => x.MaKhoaHoc == courseId, ct)) return NotFound();
        var order = (await db.UnitKhoaHoc.Where(x => x.MaKhoaHoc == courseId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var item = new UnitKhoaHoc { MaKhoaHoc = courseId, TenUnit = request.Name.Trim(), MoTa = Clean(request.Description), ThuTu = order };
        db.UnitKhoaHoc.Add(item);
        var result = await Save(ct);
        return result is OkResult ? Created($"/api/admin/content/courses/{courseId}/outline", new { id = item.MaUnit, order }) : result;
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}")]
    public async Task<IActionResult> EditUnit(int courseId, int unitId, ContentNameRequest request, CancellationToken ct)
    {
        if (!ValidName(request.Name) || Clean(request.Description)?.Length > 512) return Invalid("Tên Unit hoặc mô tả không hợp lệ.");
        var item = await db.UnitKhoaHoc.FirstOrDefaultAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        item.TenUnit = request.Name.Trim(); item.MoTa = Clean(request.Description);
        return await Save(ct);
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/archive")]
    public async Task<IActionResult> ArchiveUnit(int courseId, int unitId, ContentArchiveRequest request, CancellationToken ct)
    {
        var item = await db.UnitKhoaHoc.FirstOrDefaultAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        item.IsArchived = request.IsArchived;
        var result = await Save(ct);
        return result is OkResult ? Ok(new { id = unitId, isArchived = item.IsArchived }) : result;
    }

    [HttpDelete("courses/{courseId:int}/units/{unitId:int}")]
    public async Task<IActionResult> DeleteUnit(int courseId, int unitId, CancellationToken ct,
        [FromQuery] bool deleteHistory = false, [FromQuery] int expectedProgress = -1,
        [FromQuery] int expectedAttempts = -1, [FromQuery] int expectedChecks = -1)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var item = await db.UnitKhoaHoc.FirstOrDefaultAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        var steps = await db.BuocLoTrinh.Where(x => x.MaLessonNavigation.MaUnit == unitId).ToListAsync(ct);
        var ids = steps.Select(x => x.MaBuoc).ToArray();
        var blocked = await ConfirmAndClearHistory(ids, deleteHistory, expectedProgress, expectedAttempts, expectedChecks, ct);
        if (blocked is not null) return blocked;
        db.BuocLoTrinh.RemoveRange(steps);
        db.LessonKhoaHoc.RemoveRange(await db.LessonKhoaHoc.Where(x => x.MaUnit == unitId).ToListAsync(ct));
        db.UnitKhoaHoc.Remove(item);
        var result = await Save(ct);
        if (result is OkResult) {
            var remaining = await db.UnitKhoaHoc.Where(x => x.MaKhoaHoc == courseId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            for (var i = 0; i < remaining.Count; i++)
                if (remaining[i].ThuTu != i + 1) { remaining[i].ThuTu = i + 1; await db.SaveChangesAsync(ct); }
            await RemoveDetachedExercises(steps.Select(x => x.MaBaiTap), ct);
            await tx.CommitAsync(ct);
        }
        return result;
    }

    [HttpPost("courses/{courseId:int}/units/order")]
    public Task<IActionResult> OrderUnits(int courseId, ContentOrderRequest request, CancellationToken ct) =>
        Reorder(request.Ids, db.UnitKhoaHoc.Where(x => x.MaKhoaHoc == courseId), x => x.MaUnit, x => x.ThuTu, (x, v) => x.ThuTu = v, ct);

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LESSON" })]
    public async Task<IActionResult> AddLesson(int courseId, int unitId, ContentNameRequest request, CancellationToken ct)
    {
        if (!ValidName(request.Name) || Clean(request.Description)?.Length > 512) return Invalid("Tên Lesson hoặc mô tả không hợp lệ.");
        if (!await db.UnitKhoaHoc.AnyAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct)) return NotFound();
        var order = (await db.LessonKhoaHoc.Where(x => x.MaUnit == unitId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var item = new LessonKhoaHoc { MaUnit = unitId, TenLesson = request.Name.Trim(), MoTa = Clean(request.Description), ThuTu = order };
        db.LessonKhoaHoc.Add(item);
        var result = await Save(ct);
        return result is OkResult ? Created($"/api/admin/content/courses/{courseId}/outline", new { id = item.MaLesson, order }) : result;
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}")]
    public async Task<IActionResult> EditLesson(int courseId, int unitId, int lessonId, ContentNameRequest request, CancellationToken ct)
    {
        if (!ValidName(request.Name) || Clean(request.Description)?.Length > 512) return Invalid("Tên Lesson hoặc mô tả không hợp lệ.");
        var item = await db.LessonKhoaHoc.FirstOrDefaultAsync(x => x.MaLesson == lessonId && x.MaUnit == unitId && x.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        item.TenLesson = request.Name.Trim(); item.MoTa = Clean(request.Description);
        return await Save(ct);
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/archive")]
    public async Task<IActionResult> ArchiveLesson(int courseId, int unitId, int lessonId, ContentArchiveRequest request, CancellationToken ct)
    {
        var item = await db.LessonKhoaHoc.FirstOrDefaultAsync(x => x.MaLesson == lessonId && x.MaUnit == unitId && x.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        item.IsArchived = request.IsArchived;
        var result = await Save(ct);
        return result is OkResult ? Ok(new { id = lessonId, isArchived = item.IsArchived }) : result;
    }

    [HttpDelete("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}")]
    public async Task<IActionResult> DeleteLesson(int courseId, int unitId, int lessonId, CancellationToken ct,
        [FromQuery] bool deleteHistory = false, [FromQuery] int expectedProgress = -1,
        [FromQuery] int expectedAttempts = -1, [FromQuery] int expectedChecks = -1)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var item = await db.LessonKhoaHoc.FirstOrDefaultAsync(x => x.MaLesson == lessonId && x.MaUnit == unitId && x.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        var steps = await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).ToListAsync(ct);
        var ids = steps.Select(x => x.MaBuoc).ToArray();
        var blocked = await ConfirmAndClearHistory(ids, deleteHistory, expectedProgress, expectedAttempts, expectedChecks, ct);
        if (blocked is not null) return blocked;
        db.BuocLoTrinh.RemoveRange(steps);
        db.LessonKhoaHoc.Remove(item);
        var result = await Save(ct);
        if (result is OkResult) {
            var remaining = await db.LessonKhoaHoc.Where(x => x.MaUnit == unitId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            for (var i = 0; i < remaining.Count; i++)
                if (remaining[i].ThuTu != i + 1) { remaining[i].ThuTu = i + 1; await db.SaveChangesAsync(ct); }
            await RemoveDetachedExercises(steps.Select(x => x.MaBaiTap), ct);
            await tx.CommitAsync(ct);
        }
        return result;
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/order")]
    public async Task<IActionResult> OrderLessons(int courseId, int unitId, ContentOrderRequest request, CancellationToken ct)
    {
        if (!await db.UnitKhoaHoc.AnyAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct)) return NotFound();
        return await Reorder(request.Ids, db.LessonKhoaHoc.Where(x => x.MaUnit == unitId), x => x.MaLesson, x => x.ThuTu, (x, v) => x.ThuTu = v, ct);
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_STEP" })]
    public async Task<IActionResult> AddStep(int courseId, int unitId, int lessonId, ContentStepRequest request, CancellationToken ct)
    {
        if (Clean(request.Title) is not { Length: > 0 and <= 128 }) return Invalid("Tiêu đề bước không hợp lệ.");
        if (!await LessonExists(courseId, unitId, lessonId, ct)) return NotFound();
        var valid = await ValidActivity(request, ct);
        if (!valid) return Invalid("Hoạt động không khả dụng. Bước chỉ hỗ trợ bài giảng hoặc chủ đề từ vựng đang mở.");
        var order = (await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        if (request.ActivityType == "LECTURE")
        {
            var existing = await db.BuocLoTrinh.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaBaiGiang == request.ActivityId, ct);
            if (existing is not null)
            {
                if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0)
                    return Unauthorized();
                var ownTransaction = db.Database.CurrentTransaction is null;
                await using var tx = ownTransaction
                    ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
                var copy = await CloneStep(existing, lessonId, order, adminId, ct);
                copy.TieuDe = request.Title.Trim();
                await db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);
                return Created($"/api/admin/content/courses/{courseId}/outline", new { id = copy.MaBuoc, order });
            }
        }
        var item = new BuocLoTrinh { MaLesson = lessonId, TieuDe = request.Title.Trim(), ThuTu = order };
        Assign(item, request); db.BuocLoTrinh.Add(item);
        var result = await Save(ct);
        return result is OkResult ? Created($"/api/admin/content/courses/{courseId}/outline", new { id = item.MaBuoc, order }) : result;
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/lecture")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LECTURE" })]
    public async Task<IActionResult> AddCourseLecture(int courseId, int unitId, int lessonId, CourseLectureRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0) return Unauthorized();
        if (Clean(request.Title) is not { Length: > 0 and <= 128 } || Clean(request.Summary)?.Length > 512 ||
            request.Content?.Length > 100_000 || request.Level is < 1 or > 3) return Invalid("Thông tin bài giảng không hợp lệ.");
        if (!await LessonExists(courseId, unitId, lessonId, ct)) return NotFound();
        if (request.CategoryId is { } category && !await db.DanhMucBaiGiang.AnyAsync(x => x.MaDanhMuc == category && x.TrangThai == "DANG_MO", ct))
            return Invalid("Danh mục không khả dụng.");
        if (request.PartId is { } part && !await db.PartTOEIC.AnyAsync(x => x.MaPart == part, ct)) return Invalid("Part không tồn tại.");

        var ownTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        try
        {
            var order = (await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
            var lecture = new BaiGiang { TieuDe = request.Title.Trim(), NoiDungTomTat = Clean(request.Summary),
                NoiDung = Clean(request.Content), MaDanhMuc = request.CategoryId, CapDo = request.Level,
                MaPart = request.PartId, TrangThai = "DANG_MO", MaAdminNoiDung = adminId, NgayTao = DateTime.Now };
            var step = new BuocLoTrinh { MaLesson = lessonId, TieuDe = lecture.TieuDe, ThuTu = order,
                MaBaiGiangNavigation = lecture };
            db.BuocLoTrinh.Add(step);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Created($"/api/admin/content/courses/{courseId}/outline", new { id = step.MaBuoc, lectureId = lecture.MaBaiGiang, order });
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            return Conflict("Nội dung đã thay đổi; tải lại và thử lại.");
        }
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/lecture")]
    public async Task<IActionResult> EditCourseLecture(int courseId, int unitId, int lessonId, int stepId,
        CourseLectureRequest request, CancellationToken ct)
    {
        if (Clean(request.Title) is not { Length: > 0 and <= 128 } || Clean(request.Summary)?.Length > 512 ||
            request.Content?.Length > 100_000 || request.Level is < 1 or > 3) return Invalid("Thông tin bài giảng không hợp lệ.");
        var step = await db.BuocLoTrinh.Include(x => x.MaBaiGiangNavigation).FirstOrDefaultAsync(x => x.MaBuoc == stepId &&
            x.MaLesson == lessonId && x.MaLessonNavigation.MaUnit == unitId &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && x.MaBaiGiang != null, ct);
        if (step is null) return NotFound();
        var lecture = step.MaBaiGiangNavigation;
        if (!User.IsInRole("ADMIN_QUAN_LY") && lecture.MaAdminNoiDung !=
            (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) ? adminId : 0)) return Forbid();
        if (request.CategoryId is { } category && !await db.DanhMucBaiGiang.AnyAsync(x => x.MaDanhMuc == category && x.TrangThai == "DANG_MO", ct))
            return Invalid("Danh mục không khả dụng.");
        if (request.PartId is { } part && !await db.PartTOEIC.AnyAsync(x => x.MaPart == part, ct)) return Invalid("Part không tồn tại.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var oldTitle = lecture.TieuDe;
        lecture.TieuDe = request.Title.Trim(); lecture.NoiDungTomTat = Clean(request.Summary);
        lecture.NoiDung = Clean(request.Content); lecture.MaDanhMuc = request.CategoryId;
        lecture.CapDo = request.Level; lecture.MaPart = request.PartId;
        if (step.TieuDe == oldTitle) step.TieuDe = lecture.TieuDe;
        var saved = await Save(ct);
        if (saved is not OkResult) { await tx.RollbackAsync(ct); return saved; }
        await tx.CommitAsync(ct);
        return Ok(new { id = step.MaBuoc, lectureId = lecture.MaBaiGiang, title = step.TieuDe });
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}")]
    public async Task<IActionResult> EditStep(int courseId, int unitId, int lessonId, int stepId, ContentStepRequest request, CancellationToken ct)
    {
        if (Clean(request.Title) is not { Length: > 0 and <= 128 } || !await ValidActivity(request, ct)) return Invalid("Tiêu đề hoặc hoạt động không hợp lệ.");
        var item = await db.BuocLoTrinh.FirstOrDefaultAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        var activityChanged = item.MaBaiGiang != (request.ActivityType == "LECTURE" ? request.ActivityId : null) ||
            item.MaChuDe != (request.ActivityType == "VOCABULARY" ? request.ActivityId : null);
        if (activityChanged && (await db.TienDoBuocLoTrinh.AnyAsync(x => x.MaBuoc == stepId, ct) ||
            await db.LuotBaiTapKhoaHoc.AnyAsync(x => x.MaBuoc == stepId, ct)))
            return Conflict("Hoạt động đã có lịch sử học; chỉ có thể đổi tiêu đề, không thể đổi nội dung liên kết.");
        item.TieuDe = request.Title.Trim(); Assign(item, request);
        return await Save(ct);
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/archive")]
    public async Task<IActionResult> ArchiveStep(int courseId, int unitId, int lessonId, int stepId, ContentArchiveRequest request, CancellationToken ct)
    {
        var item = await db.BuocLoTrinh.FirstOrDefaultAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        item.IsArchived = request.IsArchived;
        var result = await Save(ct);
        return result is OkResult ? Ok(new { id = stepId, isArchived = item.IsArchived }) : result;
    }

    [HttpDelete("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}")]
    public async Task<IActionResult> DeleteStep(int courseId, int unitId, int lessonId, int stepId, CancellationToken ct,
        [FromQuery] bool deleteHistory = false, [FromQuery] int expectedProgress = -1,
        [FromQuery] int expectedAttempts = -1, [FromQuery] int expectedChecks = -1)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var item = await db.BuocLoTrinh.FirstOrDefaultAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (item is null) return NotFound();
        var blocked = await ConfirmAndClearHistory([stepId], deleteHistory, expectedProgress, expectedAttempts, expectedChecks, ct);
        if (blocked is not null) return blocked;
        db.BuocLoTrinh.Remove(item);
        var result = await Save(ct);
        if (result is OkResult) { await RemoveDetachedExercises([item.MaBaiTap], ct); await tx.CommitAsync(ct); }
        return result;
    }

    private async Task<ContentDeleteImpact> DeleteImpact(int[] stepIds, CancellationToken ct)
    {
        if (stepIds.Length == 0) return new(0, 0, 0);
        var attempts = await db.LuotBaiTapKhoaHoc.AsNoTracking().Where(x => stepIds.Contains(x.MaBuoc))
            .Select(x => new { x.MaLuot, x.MaDangKy }).ToArrayAsync(ct);
        var progressLearners = await db.TienDoBuocLoTrinh.AsNoTracking().Where(x => stepIds.Contains(x.MaBuoc))
            .Select(x => x.MaDangKyNavigation.MaNguoiDung).ToArrayAsync(ct);
        var enrollmentIds = attempts.Select(x => x.MaDangKy).Distinct().ToArray();
        var attemptLearners = enrollmentIds.Length == 0 ? [] : await db.DangKyGoiHoc.AsNoTracking()
            .Where(x => enrollmentIds.Contains(x.MaDangKy)).Select(x => x.MaNguoiDung).ToArrayAsync(ct);
        var attemptIds = attempts.Select(x => x.MaLuot).ToArray();
        return new(progressLearners.Length, attempts.Length, attemptIds.Length == 0 ? 0 :
                await db.LanKiemTraBaiTapKhoaHoc.CountAsync(x => attemptIds.Contains(x.MaLuot), ct),
            progressLearners.Concat(attemptLearners).Distinct().Count());
    }

    private async Task<IActionResult?> ConfirmAndClearHistory(int[] stepIds, bool deleteHistory,
        int expectedProgress, int expectedAttempts, int expectedChecks, CancellationToken ct)
    {
        var impact = await DeleteImpact(stepIds, ct);
        if (!impact.HasHistory) return null;
        if (!deleteHistory) return Conflict(new { code = "HISTORY_DELETE_CONFIRM_REQUIRED", impact,
            message = "Nội dung đã có lịch sử học. Hãy lưu trữ hoặc xác nhận xóa vĩnh viễn sau khi xem số bản ghi bị ảnh hưởng." });
        if (impact.ProgressRecords != expectedProgress || impact.Attempts != expectedAttempts || impact.Checks != expectedChecks)
            return Conflict(new { code = "HISTORY_DELETE_IMPACT_CHANGED", impact,
                message = "Số bản ghi đã thay đổi. Tải lại số lượng và xác nhận lại trước khi xóa." });
        var attemptIds = await db.LuotBaiTapKhoaHoc.AsNoTracking().Where(x => stepIds.Contains(x.MaBuoc))
            .Select(x => x.MaLuot).ToArrayAsync(ct);
        if (attemptIds.Length > 0) await db.LanKiemTraBaiTapKhoaHoc.Where(x => attemptIds.Contains(x.MaLuot)).ExecuteDeleteAsync(ct);
        await db.LuotBaiTapKhoaHoc.Where(x => stepIds.Contains(x.MaBuoc)).ExecuteDeleteAsync(ct);
        await db.TienDoBuocLoTrinh.Where(x => stepIds.Contains(x.MaBuoc)).ExecuteDeleteAsync(ct);
        return null;
    }

    [HttpGet("courses/{courseId:int}/units/{unitId:int}/delete-impact")]
    public async Task<IActionResult> UnitDeleteImpact(int courseId, int unitId, CancellationToken ct)
    {
        if (!await db.UnitKhoaHoc.AnyAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct)) return NotFound();
        var ids = await db.BuocLoTrinh.Where(x => x.MaLessonNavigation.MaUnit == unitId).Select(x => x.MaBuoc).ToArrayAsync(ct);
        return Ok(await DeleteImpact(ids, ct));
    }

    [HttpGet("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/delete-impact")]
    public async Task<IActionResult> LessonDeleteImpact(int courseId, int unitId, int lessonId, CancellationToken ct)
    {
        if (!await LessonExists(courseId, unitId, lessonId, ct)) return NotFound();
        var ids = await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).Select(x => x.MaBuoc).ToArrayAsync(ct);
        return Ok(await DeleteImpact(ids, ct));
    }

    [HttpGet("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/delete-impact")]
    public async Task<IActionResult> StepDeleteImpact(int courseId, int unitId, int lessonId, int stepId, CancellationToken ct)
    {
        if (!await db.BuocLoTrinh.AnyAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct)) return NotFound();
        return Ok(await DeleteImpact([stepId], ct));
    }

    private async Task RemoveDetachedExercises(IEnumerable<int?> exerciseIds, CancellationToken ct)
    {
        foreach (var id in exerciseIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
        {
            if (await db.BuocLoTrinh.AnyAsync(x => x.MaBaiTap == id, ct) ||
                await db.LuotBaiTapKhoaHoc.AnyAsync(x => x.MaBaiTap == id, ct)) continue;
            await db.CauHoiBaiTapKhoaHoc.Where(x => x.MaBaiTap == id).ExecuteDeleteAsync(ct);
            await db.PhienBanBaiTapKhoaHoc.Where(x => x.MaBaiTap == id).ExecuteDeleteAsync(ct);
            await db.BaiTapKhoaHoc.Where(x => x.MaBaiTap == id).ExecuteDeleteAsync(ct);
        }
    }

    private static string CopyTitle(string title) => title.Length <= 117 ? title + " (bản sao)" : title[..117] + " (bản sao)";

    private async Task<BuocLoTrinh> CloneStep(BuocLoTrinh source, int lessonId, int order, int adminId, CancellationToken ct)
    {
        int? lectureId = source.MaBaiGiang;
        int? exerciseId = source.MaBaiTap;
        if (source.MaBaiGiang is { } originalLectureId)
        {
            var original = await db.BaiGiang.AsNoTracking().FirstAsync(x => x.MaBaiGiang == originalLectureId, ct);
            var copy = new BaiGiang { TieuDe = CopyTitle(original.TieuDe), NoiDungTomTat = original.NoiDungTomTat,
                NoiDung = original.NoiDung, MaDanhMuc = original.MaDanhMuc, CapDo = original.CapDo,
                MaPart = original.MaPart, TrangThai = original.TrangThai, MaAdminNoiDung = adminId, NgayTao = DateTime.Now };
            db.BaiGiang.Add(copy); await db.SaveChangesAsync(ct); lectureId = copy.MaBaiGiang;
            var resources = await db.TaiNguyenBaiGiang.AsNoTracking().Where(x => x.MaBaiGiang == originalLectureId)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            for (var i = 0; i < resources.Count; i++)
                db.TaiNguyenBaiGiang.Add(new TaiNguyenBaiGiang { MaBaiGiang = copy.MaBaiGiang,
                    TenTaiNguyen = resources[i].TenTaiNguyen, LoaiTaiNguyen = resources[i].LoaiTaiNguyen,
                    DuongDanTep = resources[i].DuongDanTep, ThuTu = i + 1,
                    IsArchived = resources[i].IsArchived });
            await db.SaveChangesAsync(ct);
        }
        if (source.MaBaiTap is { } originalExerciseId)
        {
            var original = await db.BaiTapKhoaHoc.AsNoTracking().FirstAsync(x => x.MaBaiTap == originalExerciseId, ct);
            var copy = new BaiTapKhoaHoc { TieuDe = CopyTitle(original.TieuDe) };
            db.BaiTapKhoaHoc.Add(copy); await db.SaveChangesAsync(ct); exerciseId = copy.MaBaiTap;
            var version = await db.PhienBanBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaBaiTap == originalExerciseId)
                .OrderByDescending(x => x.SoPhienBan).FirstAsync(ct);
            var newVersion = new PhienBanBaiTapKhoaHoc { MaBaiTap = copy.MaBaiTap, SoPhienBan = 1, TrangThai = version.TrangThai };
            db.PhienBanBaiTapKhoaHoc.Add(newVersion); await db.SaveChangesAsync(ct);
            var questions = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaPhienBan == version.MaPhienBan)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            foreach (var q in questions)
                db.CauHoiBaiTapKhoaHoc.Add(new CauHoiBaiTapKhoaHoc { MaBaiTap = copy.MaBaiTap,
                    MaPhienBan = newVersion.MaPhienBan, ThuTu = q.ThuTu, NoiDung = q.NoiDung,
                    NguLieu = q.NguLieu, PhuongAnA = q.PhuongAnA, PhuongAnB = q.PhuongAnB,
                    PhuongAnC = q.PhuongAnC, PhuongAnD = q.PhuongAnD, PhuongAnDung = q.PhuongAnDung,
                    GiaiThich = q.GiaiThich });
            await db.SaveChangesAsync(ct);
        }
        var step = new BuocLoTrinh { MaLesson = lessonId, TieuDe = CopyTitle(source.TieuDe),
            ThuTu = order, MaBaiGiang = lectureId, MaChuDe = source.MaChuDe,
            MaDeThi = source.MaDeThi, MaBaiTap = exerciseId };
        db.BuocLoTrinh.Add(step); await db.SaveChangesAsync(ct);
        return step;
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/duplicate")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_STEP" })]
    public async Task<IActionResult> DuplicateStep(int courseId, int unitId, int lessonId, int stepId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0) return Unauthorized();
        var source = await db.BuocLoTrinh.AsNoTracking().FirstOrDefaultAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (source is null) return NotFound();
        await using var tx = db.Database.CurrentTransaction is null ?
            await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var order = (await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var copy = await CloneStep(source, lessonId, order, adminId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Created($"/api/admin/content/courses/{courseId}/outline", new { id = copy.MaBuoc, order });
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/duplicate")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_LESSON" })]
    public async Task<IActionResult> DuplicateLesson(int courseId, int unitId, int lessonId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0) return Unauthorized();
        var source = await db.LessonKhoaHoc.AsNoTracking().FirstOrDefaultAsync(x => x.MaLesson == lessonId &&
            x.MaUnit == unitId && x.MaUnitNavigation.MaKhoaHoc == courseId, ct);
        if (source is null) return NotFound();
        await using var tx = db.Database.CurrentTransaction is null ?
            await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var order = (await db.LessonKhoaHoc.Where(x => x.MaUnit == unitId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var copy = new LessonKhoaHoc { MaUnit = unitId, TenLesson = CopyTitle(source.TenLesson),
            MoTa = source.MoTa, ThuTu = order };
        db.LessonKhoaHoc.Add(copy); await db.SaveChangesAsync(ct);
        var steps = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaLesson == lessonId && !x.IsArchived)
            .OrderBy(x => x.ThuTu).ToListAsync(ct);
        for (var i = 0; i < steps.Count; i++) await CloneStep(steps[i], copy.MaLesson, i + 1, adminId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Created($"/api/admin/content/courses/{courseId}/outline", new { id = copy.MaLesson, order });
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/duplicate")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_UNIT" })]
    public async Task<IActionResult> DuplicateUnit(int courseId, int unitId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0) return Unauthorized();
        var source = await db.UnitKhoaHoc.AsNoTracking().FirstOrDefaultAsync(x => x.MaUnit == unitId && x.MaKhoaHoc == courseId, ct);
        if (source is null) return NotFound();
        await using var tx = db.Database.CurrentTransaction is null ?
            await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var order = (await db.UnitKhoaHoc.Where(x => x.MaKhoaHoc == courseId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var copy = new UnitKhoaHoc { MaKhoaHoc = courseId, TenUnit = CopyTitle(source.TenUnit),
            MoTa = source.MoTa, ThuTu = order };
        db.UnitKhoaHoc.Add(copy); await db.SaveChangesAsync(ct);
        var lessons = await db.LessonKhoaHoc.AsNoTracking().Where(x => x.MaUnit == unitId && !x.IsArchived)
            .OrderBy(x => x.ThuTu).ToListAsync(ct);
        for (var i = 0; i < lessons.Count; i++)
        {
            var newLesson = new LessonKhoaHoc { MaUnit = copy.MaUnit, TenLesson = lessons[i].TenLesson,
                MoTa = lessons[i].MoTa, ThuTu = i + 1 };
            db.LessonKhoaHoc.Add(newLesson); await db.SaveChangesAsync(ct);
            var steps = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaLesson == lessons[i].MaLesson && !x.IsArchived)
                .OrderBy(x => x.ThuTu).ToListAsync(ct);
            for (var j = 0; j < steps.Count; j++) await CloneStep(steps[j], newLesson.MaLesson, j + 1, adminId, ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return Created($"/api/admin/content/courses/{courseId}/outline", new { id = copy.MaUnit, order });
    }

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/order")]
    public async Task<IActionResult> OrderSteps(int courseId, int unitId, int lessonId, ContentOrderRequest request, CancellationToken ct)
    {
        if (!await LessonExists(courseId, unitId, lessonId, ct)) return NotFound();
        return await Reorder(request.Ids, db.BuocLoTrinh.Where(x => x.MaLesson == lessonId), x => x.MaBuoc, x => x.ThuTu, (x, v) => x.ThuTu = v, ct);
    }

    private Task<bool> LessonExists(int courseId, int unitId, int lessonId, CancellationToken ct) =>
        db.LessonKhoaHoc.AnyAsync(x => x.MaLesson == lessonId && x.MaUnit == unitId && x.MaUnitNavigation.MaKhoaHoc == courseId, ct);

    private static bool ValidExercise(CourseExerciseRequest request) =>
        Clean(request.Title) is { Length: > 0 and <= 128 } && request.Questions is { Length: > 0 and <= 100 } &&
        request.Questions.All(q => Clean(q.Content) is { Length: > 0 and <= 4000 } &&
            Clean(q.Passage) is not { Length: > 20000 } && Clean(q.A) is { Length: > 0 and <= 2000 } &&
            Clean(q.B) is { Length: > 0 and <= 2000 } && Clean(q.C) is { Length: > 0 and <= 2000 } &&
            Clean(q.D) is { Length: > 0 and <= 2000 } && q.Correct is "A" or "B" or "C" or "D" &&
            Clean(q.Explanation) is not { Length: > 10000 });

    [HttpPost("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/exercises")]
    [TypeFilter(typeof(AdminContentIdempotencyFilter), Arguments = new object[] { "CREATE_STEP" })]
    public async Task<IActionResult> AddExercise(int courseId, int unitId, int lessonId, CourseExerciseRequest request, CancellationToken ct)
    {
        if (!ValidExercise(request)) return Invalid("Bài tập cần tiêu đề và ít nhất một câu hỏi hợp lệ.");
        if (!await LessonExists(courseId, unitId, lessonId, ct)) return NotFound();
        var own = db.Database.CurrentTransaction is null;
        await using var tx = own ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var order = (await db.BuocLoTrinh.Where(x => x.MaLesson == lessonId).MaxAsync(x => (int?)x.ThuTu, ct) ?? 0) + 1;
        var exercise = new BaiTapKhoaHoc { TieuDe = request.Title.Trim() };
        db.BaiTapKhoaHoc.Add(exercise); await db.SaveChangesAsync(ct);
        var version = new PhienBanBaiTapKhoaHoc { MaBaiTap = exercise.MaBaiTap, SoPhienBan = 1,
            TrangThai = request.Publish ? "XUAT_BAN" : "NHAP" };
        db.PhienBanBaiTapKhoaHoc.Add(version); await db.SaveChangesAsync(ct);
        for (var i = 0; i < request.Questions.Length; i++) db.CauHoiBaiTapKhoaHoc.Add(ToQuestion(request.Questions[i], exercise.MaBaiTap, version.MaPhienBan, i + 1));
        var step = new BuocLoTrinh { MaLesson = lessonId, TieuDe = exercise.TieuDe, ThuTu = order, MaBaiTap = exercise.MaBaiTap };
        db.BuocLoTrinh.Add(step); await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Created($"/api/admin/content/courses/{courseId}/outline", new { id = step.MaBuoc, exerciseId = exercise.MaBaiTap });
    }

    [HttpPut("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/exercise")]
    public async Task<IActionResult> ReviseExercise(int courseId, int unitId, int lessonId, int stepId, CourseExerciseRequest request, CancellationToken ct)
    {
        if (!ValidExercise(request)) return Invalid("Bài tập cần tiêu đề và ít nhất một câu hỏi hợp lệ.");
        var step = await db.BuocLoTrinh.FirstOrDefaultAsync(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && x.MaBaiTap != null, ct);
        if (step is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var exercise = await db.BaiTapKhoaHoc.FirstAsync(x => x.MaBaiTap == step.MaBaiTap, ct);
        var versions = await db.PhienBanBaiTapKhoaHoc.Where(x => x.MaBaiTap == exercise.MaBaiTap).ToListAsync(ct);
        var next = versions.Max(x => x.SoPhienBan) + 1;
        var old = versions.FirstOrDefault(x => x.TrangThai == "XUAT_BAN");
        if (request.Publish && old is not null) { old.TrangThai = "NGUNG"; await db.SaveChangesAsync(ct); }
        var version = new PhienBanBaiTapKhoaHoc { MaBaiTap = exercise.MaBaiTap, SoPhienBan = next,
            TrangThai = request.Publish ? "XUAT_BAN" : "NHAP" };
        db.PhienBanBaiTapKhoaHoc.Add(version);
        var oldTitle = exercise.TieuDe;
        exercise.TieuDe = request.Title.Trim();
        if (step.TieuDe == oldTitle) step.TieuDe = exercise.TieuDe;
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < request.Questions.Length; i++) db.CauHoiBaiTapKhoaHoc.Add(ToQuestion(request.Questions[i], exercise.MaBaiTap, version.MaPhienBan, i + 1));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { id = step.MaBuoc, exerciseId = exercise.MaBaiTap, versionId = version.MaPhienBan });
    }

    [HttpGet("courses/{courseId:int}/units/{unitId:int}/lessons/{lessonId:int}/steps/{stepId:int}/exercise")]
    public async Task<IActionResult> ExerciseDetail(int courseId, int unitId, int lessonId, int stepId, CancellationToken ct)
    {
        var step = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaBuoc == stepId && x.MaLesson == lessonId &&
            x.MaLessonNavigation.MaUnit == unitId && x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)
            .Select(x => new { x.MaBaiTap }).FirstOrDefaultAsync(ct);
        if (step?.MaBaiTap is not { } exerciseId) return NotFound();
        var exercise = await db.BaiTapKhoaHoc.AsNoTracking().FirstAsync(x => x.MaBaiTap == exerciseId, ct);
        var version = await db.PhienBanBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaBaiTap == exerciseId)
            .OrderByDescending(x => x.SoPhienBan).FirstAsync(ct);
        var questions = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaPhienBan == version.MaPhienBan)
            .OrderBy(x => x.ThuTu).Select(x => new { content = x.NoiDung, passage = x.NguLieu,
                a = x.PhuongAnA, b = x.PhuongAnB, c = x.PhuongAnC, d = x.PhuongAnD,
                correct = x.PhuongAnDung, explanation = x.GiaiThich }).ToListAsync(ct);
        return Ok(new { title = exercise.TieuDe, status = version.TrangThai, version = version.SoPhienBan, questions });
    }

    private static CauHoiBaiTapKhoaHoc ToQuestion(CourseQuestionRequest q, int exerciseId, int versionId, int order) => new() {
        MaBaiTap = exerciseId, MaPhienBan = versionId, ThuTu = order, NoiDung = q.Content.Trim(),
        NguLieu = Clean(q.Passage), PhuongAnA = q.A.Trim(), PhuongAnB = q.B.Trim(),
        PhuongAnC = q.C.Trim(), PhuongAnD = q.D.Trim(), PhuongAnDung = q.Correct, GiaiThich = Clean(q.Explanation) };

    private async Task<bool> ValidActivity(ContentStepRequest request, CancellationToken ct) => request.ActivityId > 0 &&
        request.ActivityType switch {
            "LECTURE" => await db.BaiGiang.AnyAsync(x => x.MaBaiGiang == request.ActivityId && x.TrangThai == "DANG_MO" &&
                (x.MaDanhMuc == null || x.MaDanhMucNavigation!.TrangThai == "DANG_MO"), ct),
            "VOCABULARY" => await db.ChuDe.AnyAsync(x => x.MaChuDe == request.ActivityId && x.TrangThai == "DANG_MO", ct),
            _ => false // Exam-linked steps need a separately approved eligibility contract.
        };

    private static void Assign(BuocLoTrinh item, ContentStepRequest request)
    {
        item.MaBaiGiang = request.ActivityType == "LECTURE" ? request.ActivityId : null;
        item.MaChuDe = request.ActivityType == "VOCABULARY" ? request.ActivityId : null;
        item.MaDeThi = null;
    }

    private async Task<IActionResult> Reorder<T>(int[]? ids, IQueryable<T> query, Func<T, int> id,
        Func<T, int> order, Action<T, int> setOrder, CancellationToken ct) where T : class
    {
        if (ids is null || ids.Length != ids.Distinct().Count()) return Invalid("Danh sách thứ tự có ID trùng hoặc không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var rows = await query.ToListAsync(ct);
        if (rows.Count != ids.Length || rows.Any(x => !ids.Contains(id(x)))) return Conflict("Danh sách đã thay đổi; tải lại trước khi sắp xếp.");
        if (rows.Count == 0) { await tx.CommitAsync(ct); return Ok(); }
        var max = rows.Max(order);
        if (max > int.MaxValue - rows.Count - 1) return Conflict("Thứ tự vượt giới hạn.");
        try
        {
            for (var i = 0; i < rows.Count; i++) setOrder(rows[i], max + i + 1);
            await db.SaveChangesAsync(ct);
            foreach (var row in rows) setOrder(row, Array.IndexOf(ids, id(row)) + 1);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Conflict("Thứ tự đã thay đổi đồng thời; tải lại và thử lại.");
        }
    }
}
