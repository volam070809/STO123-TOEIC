using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;
using STO123.Services.Exam;
using STO123.Services.Admin;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/enrollments")]
public sealed class AdminEnrollmentsController(ToeicDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses =
        ["CHO_THANH_TOAN", "DANG_SU_DUNG", "HET_HAN"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminEnrollmentListQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", message = "Trang hoặc kích thước trang không hợp lệ." });
        var search = request.Search?.Trim();
        if (search?.Length > 100)
            return BadRequest(new { code = "INVALID_SEARCH", message = "Từ khóa tìm kiếm tối đa 100 ký tự." });
        var status = request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(status) && !SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái đăng ký không hợp lệ." });
        if (request.PackageId is <= 0 || request.CourseId is <= 0)
            return BadRequest(new { code = "INVALID_RELATION_FILTER", message = "Mã gói học hoặc khóa học không hợp lệ." });
        var dateError = AdminDateRangeValidation.Error(request.DateFrom, request.DateTo);
        if (dateError is not null)
            return BadRequest(new { code = "INVALID_DATE_RANGE", message = dateError });

        var localNow = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var query = ApplyFilters(db.DangKyGoiHoc.AsNoTracking(), request, search, status);
        var summary = await query.GroupBy(_ => 1).Select(group => new AdminEnrollmentSummaryDto(
            group.Count(), group.Count(item => item.MaGoiHoc != null), group.Count(item => item.MaGoiHoc == null),
            group.Count(item => item.TrangThai == "DANG_SU_DUNG" && item.NgayBatDau <= localNow &&
                item.NgayKetThuc > localNow && item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG")),
            group.Count(item => !item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG") &&
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "CHO_XU_LY"))))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0);

        var rows = await query.OrderByDescending(item => item.NgayDangKy).ThenByDescending(item => item.MaDangKy)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new EnrollmentRow(item.MaDangKy, item.MaNguoiDung,
                item.MaNguoiDungNavigation.HoTen, item.MaNguoiDungNavigation.Email,
                item.MaGoiHoc, item.MaGoiHocNavigation == null ? null : item.MaGoiHocNavigation.TenGoiHoc,
                item.GiaDangKy, item.SoNgaySuDung, item.NgayDangKy, item.NgayBatDau, item.NgayKetThuc,
                item.TrangThai, item.GiaoDichThanhToan.Count,
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "CHO_XU_LY"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THAT_BAI")))
            .ToListAsync(ct);
        var courses = await LoadCourses(rows, ct);
        var items = rows.Select(row => MapListItem(row, courses.GetValueOrDefault(row.EnrollmentId, []), localNow)).ToList();
        var totalPages = summary.Total == 0 ? 0 : (int)Math.Ceiling(summary.Total / (double)request.PageSize);
        return Ok(new AdminPagedEnrollmentsDto(items, request.Page, request.PageSize,
            summary.Total, totalPages, summary));
    }

    [HttpGet("{enrollmentId:int}")]
    public async Task<IActionResult> Detail(int enrollmentId, CancellationToken ct)
    {
        var row = await db.DangKyGoiHoc.AsNoTracking().Where(item => item.MaDangKy == enrollmentId)
            .Select(item => new EnrollmentDetailRow(item.MaDangKy, item.MaNguoiDung,
                item.MaNguoiDungNavigation.HoTen, item.MaNguoiDungNavigation.Email,
                item.MaNguoiDungNavigation.SoDienThoai, item.MaNguoiDungNavigation.TrangThai,
                item.MaGoiHoc, item.MaGoiHocNavigation == null ? null : item.MaGoiHocNavigation.TenGoiHoc,
                item.GiaDangKy, item.SoNgaySuDung, item.NgayDangKy, item.NgayBatDau, item.NgayKetThuc,
                item.TrangThai, item.GiaoDichThanhToan.Count,
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "CHO_XU_LY"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THAT_BAI")))
            .SingleOrDefaultAsync(ct);
        if (row is null)
            return NotFound(new { code = "ENROLLMENT_NOT_FOUND", message = "Không tìm thấy đăng ký học." });

        var localNow = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var courses = (await LoadCourses([row.ToListRow()], ct)).GetValueOrDefault(enrollmentId, []);
        var payments = await db.GiaoDichThanhToan.AsNoTracking().Where(item => item.MaDangKy == enrollmentId)
            .OrderByDescending(item => item.NgayTao).ThenByDescending(item => item.MaGiaoDich)
            .Select(item => new AdminEnrollmentPaymentDto(item.MaGiaoDich,
                item.MaCongThanhToanNavigation.MaCong, item.MaCongThanhToanNavigation.TenCongThanhToan,
                item.SoTien, item.TrangThai, item.NgayTao, item.NgayThanhToan)).ToListAsync(ct);
        var activeCourseIds = await CourseEntitlement.ActiveCourseIds(db, row.LearnerId, localNow).ToListAsync(ct);
        var historyRows = await db.DangKyGoiHoc.AsNoTracking()
            .Where(item => item.MaNguoiDung == row.LearnerId && item.MaDangKy != enrollmentId)
            .OrderByDescending(item => item.NgayDangKy).Take(10)
            .Select(item => new EnrollmentHistoryRow(item.MaDangKy, item.MaGoiHoc,
                item.MaGoiHocNavigation == null ? null : item.MaGoiHocNavigation.TenGoiHoc,
                item.GiaDangKy, item.NgayDangKy, item.NgayKetThuc, item.TrangThai,
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "CHO_XU_LY"),
                item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THAT_BAI"), item.NgayBatDau))
            .ToListAsync(ct);

        var paymentState = PaymentState(row.HasSuccess, row.HasPending, row.HasFailed);
        var accessState = AccessState(row.StoredStatus, row.StartsAt, row.ExpiresAt, row.HasSuccess, row.HasPending, row.HasFailed, localNow);
        var history = historyRows.Select(item => new AdminEnrollmentHistoryDto(item.EnrollmentId,
            PurchaseType(item.PackageId), item.PackageId, item.PackageName, item.Price, item.RegisteredAt,
            item.ExpiresAt, item.StoredStatus, PaymentState(item.HasSuccess, item.HasPending, item.HasFailed),
            AccessState(item.StoredStatus, item.StartsAt, item.ExpiresAt, item.HasSuccess, item.HasPending, item.HasFailed, localNow))).ToList();
        var learner = new AdminEnrollmentLearnerDto(row.LearnerId, row.LearnerName, row.LearnerEmail,
            row.PhoneNumber, row.LearnerStatus);
        var entitlement = new AdminEnrollmentEntitlementDto(accessState == "ACTIVE", accessState,
            localNow, activeCourseIds);
        return Ok(new AdminEnrollmentDetailDto(row.EnrollmentId, learner, PurchaseType(row.PackageId),
            row.PackageId, row.PackageName, row.Price, row.AccessDays, row.RegisteredAt, row.StartsAt,
            row.ExpiresAt, row.StoredStatus, paymentState, courses, payments, entitlement, history));
    }

    private static IQueryable<DangKyGoiHoc> ApplyFilters(IQueryable<DangKyGoiHoc> query,
        AdminEnrollmentListQuery request, string? search, string? status)
    {
        if (!string.IsNullOrEmpty(search))
        {
            var hasId = int.TryParse(search, out var id);
            query = query.Where(item => item.MaNguoiDungNavigation.HoTen.Contains(search) ||
                item.MaNguoiDungNavigation.Email.Contains(search) ||
                (hasId && (item.MaDangKy == id || item.MaNguoiDung == id || item.MaGoiHoc == id ||
                    item.GiaoDichThanhToan.Any(payment => payment.MaGiaoDich == id))));
        }
        if (!string.IsNullOrEmpty(status)) query = query.Where(item => item.TrangThai == status);
        if (request.PackageId.HasValue) query = query.Where(item => item.MaGoiHoc == request.PackageId);
        if (request.CourseId.HasValue) query = query.Where(item => item.MaGoiHoc != null
            ? item.MaGoiHocNavigation.MaKhoaHoc.Any(course => course.MaKhoaHoc == request.CourseId)
            : item.ChiTietDangKyKhoaHoc.Any(course => course.MaKhoaHoc == request.CourseId));
        if (request.DateFrom.HasValue) query = query.Where(item => item.NgayDangKy >= request.DateFrom.Value.Date);
        if (request.DateTo.HasValue)
        {
            var exclusiveEnd = AdminDateRangeValidation.ExclusiveEnd(request.DateTo.Value);
            query = query.Where(item => item.NgayDangKy < exclusiveEnd);
        }
        return query;
    }

    private async Task<Dictionary<int, IReadOnlyList<AdminEnrollmentCourseDto>>> LoadCourses(
        IReadOnlyCollection<EnrollmentRow> rows, CancellationToken ct)
    {
        var result = rows.ToDictionary(item => item.EnrollmentId,
            _ => (IReadOnlyList<AdminEnrollmentCourseDto>)Array.Empty<AdminEnrollmentCourseDto>());
        var packageIds = rows.Where(item => item.PackageId.HasValue).Select(item => item.PackageId!.Value).Distinct().ToList();
        if (packageIds.Count > 0)
        {
            var links = db.Set<Dictionary<string, object>>("GoiHocKhoaHoc").AsNoTracking();
            var packageCourses = await links
                .Where(link => packageIds.Contains(EF.Property<int>(link, "MaGoiHoc")))
                .Join(db.KhoaHoc.AsNoTracking(), link => EF.Property<int>(link, "MaKhoaHoc"),
                    course => course.MaKhoaHoc, (link, course) => new {
                        PackageId = EF.Property<int>(link, "MaGoiHoc"), course.MaKhoaHoc,
                        course.TenKhoaHoc, course.GiaiDoan, course.TrangThai
                    }).ToListAsync(ct);
            var byPackage = packageCourses.GroupBy(item => item.PackageId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<AdminEnrollmentCourseDto>)group
                    .Select(item => new AdminEnrollmentCourseDto(item.MaKhoaHoc, item.TenKhoaHoc,
                        item.GiaiDoan, item.TrangThai, null)).OrderBy(item => item.Stage).ToList());
            foreach (var row in rows.Where(item => item.PackageId.HasValue))
                result[row.EnrollmentId] = byPackage.GetValueOrDefault(row.PackageId!.Value, []);
        }
        var directIds = rows.Where(item => item.PackageId is null).Select(item => item.EnrollmentId).ToList();
        if (directIds.Count > 0)
        {
            var directCourses = await db.ChiTietDangKyKhoaHoc.AsNoTracking()
                .Where(item => directIds.Contains(item.MaDangKy))
                .Select(item => new { item.MaDangKy, Course = new AdminEnrollmentCourseDto(
                    item.MaKhoaHoc, item.MaKhoaHocNavigation.TenKhoaHoc, item.MaKhoaHocNavigation.GiaiDoan,
                    item.MaKhoaHocNavigation.TrangThai, item.DonGia) }).ToListAsync(ct);
            foreach (var group in directCourses.GroupBy(item => item.MaDangKy))
                result[group.Key] = group.Select(item => item.Course).OrderBy(item => item.Stage).ToList();
        }
        return result;
    }

    private static AdminEnrollmentListItemDto MapListItem(EnrollmentRow row,
        IReadOnlyList<AdminEnrollmentCourseDto> courses, DateTime now) => new(row.EnrollmentId,
        row.LearnerId, row.LearnerName, row.LearnerEmail, PurchaseType(row.PackageId), row.PackageId,
        row.PackageName, row.Price, row.AccessDays, row.RegisteredAt, row.StartsAt, row.ExpiresAt,
        row.StoredStatus, PaymentState(row.HasSuccess, row.HasPending, row.HasFailed),
        AccessState(row.StoredStatus, row.StartsAt, row.ExpiresAt, row.HasSuccess, row.HasPending, row.HasFailed, now),
        row.PaymentCount, courses);

    private static string PurchaseType(int? packageId) => packageId.HasValue ? "PACKAGE" : "DIRECT_COURSES";
    private static string PaymentState(bool success, bool pending, bool failed) => success ? "SUCCESS" :
        pending ? "PENDING" : failed ? "FAILED" : "NONE";
    private static string AccessState(string storedStatus, DateTime? startsAt, DateTime? expiresAt,
        bool success, bool pending, bool failed, DateTime now)
    {
        if (success && storedStatus == "DANG_SU_DUNG" && startsAt <= now && expiresAt > now) return "ACTIVE";
        if (success && (storedStatus == "HET_HAN" || expiresAt <= now)) return "EXPIRED";
        if (!success && pending) return "PAYMENT_PENDING";
        if (!success && failed) return "PAYMENT_FAILED";
        return "NOT_ACTIVE";
    }

    private record EnrollmentRow(int EnrollmentId, int LearnerId, string LearnerName, string LearnerEmail,
        int? PackageId, string? PackageName, decimal Price, int AccessDays, DateTime RegisteredAt,
        DateTime? StartsAt, DateTime? ExpiresAt, string StoredStatus, int PaymentCount,
        bool HasSuccess, bool HasPending, bool HasFailed);
    private sealed record EnrollmentDetailRow(int EnrollmentId, int LearnerId, string LearnerName,
        string LearnerEmail, string? PhoneNumber, string LearnerStatus, int? PackageId, string? PackageName,
        decimal Price, int AccessDays, DateTime RegisteredAt, DateTime? StartsAt, DateTime? ExpiresAt,
        string StoredStatus, int PaymentCount, bool HasSuccess, bool HasPending, bool HasFailed)
    {
        public EnrollmentRow ToListRow() => new(EnrollmentId, LearnerId, LearnerName, LearnerEmail,
            PackageId, PackageName, Price, AccessDays, RegisteredAt, StartsAt, ExpiresAt, StoredStatus,
            PaymentCount, HasSuccess, HasPending, HasFailed);
    }
    private sealed record EnrollmentHistoryRow(int EnrollmentId, int? PackageId, string? PackageName,
        decimal Price, DateTime RegisteredAt, DateTime? ExpiresAt, string StoredStatus,
        bool HasSuccess, bool HasPending, bool HasFailed, DateTime? StartsAt);
}
