using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;
using STO123.Services.Admin;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/payments")]
public sealed class AdminPaymentsController(ToeicDbContext db) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses =
        ["CHO_XU_LY", "THANH_CONG", "THAT_BAI"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminPaymentListQuery request, CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return validation;
        var status = request.Status?.Trim().ToUpperInvariant();
        var query = FilteredPayments(request);

        var summary = await PaymentSummary(query, ct);
        var ordered = status == "THANH_CONG"
            ? query.OrderByDescending(item => item.NgayThanhToan ?? item.NgayTao).ThenByDescending(item => item.MaGiaoDich)
            : query.OrderByDescending(item => item.NgayTao).ThenByDescending(item => item.MaGiaoDich);
        var items = await PaymentItems(ordered, request, ct);
        var gateways = await PaymentGateways(ct);
        var totalPages = summary.Total == 0 ? 0 : (int)Math.Ceiling(summary.Total / (double)request.PageSize);
        return Ok(new AdminPagedPaymentsDto(items, request.Page, request.PageSize,
            summary.Total, totalPages, summary, gateways));
    }

    [HttpGet("learners")]
    public async Task<IActionResult> Learners([FromQuery] AdminPaymentListQuery request, CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return validation;
        var query = FilteredPayments(request);
        var summary = await PaymentSummary(query, ct);
        var grouped = query.GroupBy(item => new
        {
            item.MaDangKyNavigation.MaNguoiDung,
            item.MaDangKyNavigation.MaNguoiDungNavigation.HoTen,
            item.MaDangKyNavigation.MaNguoiDungNavigation.Email
        });
        var totalLearners = await grouped.CountAsync(ct);
        var aggregates = grouped.Select(group => new
        {
            LearnerId = group.Key.MaNguoiDung,
            LearnerName = group.Key.HoTen,
            LearnerEmail = group.Key.Email,
            TotalTransactions = group.Select(item => item.MaGiaoDich).Distinct().Count(),
            SuccessfulTransactions = group.Count(item => item.TrangThai == "THANH_CONG"),
            FailedTransactions = group.Count(item => item.TrangThai == "THAT_BAI"),
            PendingTransactions = group.Count(item => item.TrangThai == "CHO_XU_LY"),
            SuccessfulAmount = group.Where(item => item.TrangThai == "THANH_CONG")
                .Sum(item => (decimal?)item.SoTien) ?? 0m,
            LatestTransactionAt = group.Max(item => item.NgayThanhToan ?? item.NgayTao)
        });
        var items = await aggregates
            .OrderByDescending(item => item.LatestTransactionAt).ThenBy(item => item.LearnerId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new AdminPaymentLearnerSummaryItemDto(item.LearnerId,
                item.LearnerName, item.LearnerEmail, item.TotalTransactions,
                item.SuccessfulTransactions, item.FailedTransactions, item.PendingTransactions,
                item.SuccessfulAmount, item.LatestTransactionAt)).ToListAsync(ct);
        var totalPages = totalLearners == 0 ? 0 :
            (int)Math.Ceiling(totalLearners / (double)request.PageSize);
        return Ok(new AdminPagedPaymentLearnersDto(items, request.Page, request.PageSize,
            totalLearners, totalPages, summary, await PaymentGateways(ct)));
    }

    [HttpGet("learners/{learnerId:int}")]
    public async Task<IActionResult> LearnerHistory(int learnerId,
        [FromQuery] AdminPaymentListQuery request, CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return validation;
        var query = FilteredPayments(request)
            .Where(item => item.MaDangKyNavigation.MaNguoiDung == learnerId);
        var summary = await PaymentSummary(query, ct);
        var status = request.Status?.Trim().ToUpperInvariant();
        var ordered = status == "THANH_CONG"
            ? query.OrderByDescending(item => item.NgayThanhToan ?? item.NgayTao)
                .ThenByDescending(item => item.MaGiaoDich)
            : query.OrderByDescending(item => item.NgayTao).ThenByDescending(item => item.MaGiaoDich);
        var items = await PaymentItems(ordered, request, ct);
        var totalPages = summary.Total == 0 ? 0 :
            (int)Math.Ceiling(summary.Total / (double)request.PageSize);
        return Ok(new AdminPagedPaymentsDto(items, request.Page, request.PageSize,
            summary.Total, totalPages, summary, Array.Empty<AdminPaymentGatewayDto>()));
    }

    [HttpGet("{paymentId:int}")]
    public async Task<IActionResult> Detail(int paymentId, CancellationToken ct)
    {
        var detail = await db.GiaoDichThanhToan.AsNoTracking().Where(item => item.MaGiaoDich == paymentId)
            .Select(item => new AdminPaymentDetailDto(item.MaGiaoDich, item.MaGiaoDichCongThanhToan,
                item.SoTien, item.TrangThai, item.NgayTao, item.NgayThanhToan,
                new AdminPaymentGatewayDto(item.MaCongThanhToan,
                    item.MaCongThanhToanNavigation.MaCong, item.MaCongThanhToanNavigation.TenCongThanhToan),
                new AdminPaymentLearnerDto(item.MaDangKyNavigation.MaNguoiDung,
                    item.MaDangKyNavigation.MaNguoiDungNavigation.HoTen,
                    item.MaDangKyNavigation.MaNguoiDungNavigation.Email,
                    item.MaDangKyNavigation.MaNguoiDungNavigation.SoDienThoai,
                    item.MaDangKyNavigation.MaNguoiDungNavigation.TrangThai),
                new AdminPaymentEnrollmentDto(item.MaDangKy,
                    item.MaDangKyNavigation.MaGoiHoc == null ? "DIRECT_COURSES" : "PACKAGE",
                    item.MaDangKyNavigation.MaGoiHoc,
                    item.MaDangKyNavigation.MaGoiHocNavigation == null ? null :
                        item.MaDangKyNavigation.MaGoiHocNavigation.TenGoiHoc,
                    item.MaDangKyNavigation.GiaDangKy, item.MaDangKyNavigation.SoNgaySuDung,
                    item.MaDangKyNavigation.NgayDangKy, item.MaDangKyNavigation.NgayBatDau,
                    item.MaDangKyNavigation.NgayKetThuc, item.MaDangKyNavigation.TrangThai),
                Array.Empty<AdminPaymentCourseDto>(), Array.Empty<AdminRelatedPaymentDto>()))
            .SingleOrDefaultAsync(ct);
        if (detail is null)
            return NotFound(new { code = "PAYMENT_NOT_FOUND", message = "Không tìm thấy giao dịch thanh toán." });

        IReadOnlyList<AdminPaymentCourseDto> courses;
        if (detail.Enrollment.PackageId.HasValue)
        {
            var packageId = detail.Enrollment.PackageId.Value;
            courses = await db.KhoaHoc.AsNoTracking()
                .Where(course => course.MaGoiHoc.Any(package => package.MaGoiHoc == packageId))
                .OrderBy(course => course.GiaiDoan)
                .Select(course => new AdminPaymentCourseDto(course.MaKhoaHoc, course.TenKhoaHoc,
                    course.GiaiDoan, course.TrangThai, null)).ToListAsync(ct);
        }
        else
        {
            courses = await db.ChiTietDangKyKhoaHoc.AsNoTracking()
                .Where(item => item.MaDangKy == detail.Enrollment.EnrollmentId)
                .OrderBy(item => item.MaKhoaHocNavigation.GiaiDoan)
                .Select(item => new AdminPaymentCourseDto(item.MaKhoaHoc,
                    item.MaKhoaHocNavigation.TenKhoaHoc, item.MaKhoaHocNavigation.GiaiDoan,
                    item.MaKhoaHocNavigation.TrangThai, item.DonGia)).ToListAsync(ct);
        }
        var related = await db.GiaoDichThanhToan.AsNoTracking()
            .Where(item => item.MaDangKy == detail.Enrollment.EnrollmentId)
            .OrderByDescending(item => item.NgayTao).ThenByDescending(item => item.MaGiaoDich)
            .Select(item => new AdminRelatedPaymentDto(item.MaGiaoDich, item.SoTien,
                item.TrangThai, item.NgayTao, item.NgayThanhToan)).ToListAsync(ct);
        return Ok(detail with { Courses = courses, RelatedPayments = related });
    }

    private IActionResult? Validate(AdminPaymentListQuery request)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", message = "Trang hoặc kích thước trang không hợp lệ." });
        var search = request.Search?.Trim();
        if (search?.Length > 128)
            return BadRequest(new { code = "INVALID_SEARCH", message = "Từ khóa tìm kiếm tối đa 128 ký tự." });
        var status = request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(status) && !SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái thanh toán không hợp lệ." });
        var gateway = request.Gateway?.Trim().ToUpperInvariant();
        if (gateway?.Length > 32)
            return BadRequest(new { code = "INVALID_GATEWAY", message = "Mã cổng thanh toán không hợp lệ." });
        var dateError = AdminDateRangeValidation.Error(request.DateFrom, request.DateTo);
        return dateError is null ? null :
            BadRequest(new { code = "INVALID_DATE_RANGE", message = dateError });
    }

    private IQueryable<GiaoDichThanhToan> FilteredPayments(AdminPaymentListQuery request)
    {
        var search = request.Search?.Trim();
        var status = request.Status?.Trim().ToUpperInvariant();
        var gateway = request.Gateway?.Trim().ToUpperInvariant();
        var query = db.GiaoDichThanhToan.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(search))
        {
            var hasId = int.TryParse(search, out var id);
            query = query.Where(item => item.MaGiaoDichCongThanhToan.Contains(search) ||
                item.MaDangKyNavigation.MaNguoiDungNavigation.HoTen.Contains(search) ||
                item.MaDangKyNavigation.MaNguoiDungNavigation.Email.Contains(search) ||
                (hasId && (item.MaGiaoDich == id || item.MaDangKy == id ||
                    item.MaDangKyNavigation.MaNguoiDung == id)));
        }
        if (!string.IsNullOrEmpty(status)) query = query.Where(item => item.TrangThai == status);
        if (!string.IsNullOrEmpty(gateway))
            query = query.Where(item => item.MaCongThanhToanNavigation.MaCong == gateway);
        if (status == "THANH_CONG")
        {
            if (request.DateFrom.HasValue) query = query.Where(item =>
                (item.NgayThanhToan ?? item.NgayTao) >= request.DateFrom.Value.Date);
            if (request.DateTo.HasValue)
            {
                var exclusiveEnd = AdminDateRangeValidation.ExclusiveEnd(request.DateTo.Value);
                query = query.Where(item => (item.NgayThanhToan ?? item.NgayTao) < exclusiveEnd);
            }
        }
        else
        {
            if (request.DateFrom.HasValue)
                query = query.Where(item => item.NgayTao >= request.DateFrom.Value.Date);
            if (request.DateTo.HasValue)
            {
                var exclusiveEnd = AdminDateRangeValidation.ExclusiveEnd(request.DateTo.Value);
                query = query.Where(item => item.NgayTao < exclusiveEnd);
            }
        }
        return query;
    }

    private static async Task<AdminPaymentSummaryDto> PaymentSummary(
        IQueryable<GiaoDichThanhToan> query, CancellationToken ct) =>
        await query.GroupBy(_ => 1).Select(group => new AdminPaymentSummaryDto(
            group.Select(item => item.MaGiaoDich).Distinct().Count(),
            group.Count(item => item.TrangThai == "CHO_XU_LY"),
            group.Count(item => item.TrangThai == "THANH_CONG"),
            group.Count(item => item.TrangThai == "THAT_BAI"),
            group.Where(item => item.TrangThai == "THANH_CONG")
                .Sum(item => (decimal?)item.SoTien) ?? 0m))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0);

    private static Task<List<AdminPaymentListItemDto>> PaymentItems(
        IQueryable<GiaoDichThanhToan> ordered, AdminPaymentListQuery request, CancellationToken ct) =>
        ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new AdminPaymentListItemDto(item.MaGiaoDich,
                item.MaGiaoDichCongThanhToan, item.MaDangKy,
                item.MaDangKyNavigation.MaNguoiDung,
                item.MaDangKyNavigation.MaNguoiDungNavigation.HoTen,
                item.MaDangKyNavigation.MaNguoiDungNavigation.Email,
                item.MaDangKyNavigation.MaGoiHoc == null ? "DIRECT_COURSES" : "PACKAGE",
                item.MaDangKyNavigation.MaGoiHoc,
                item.MaDangKyNavigation.MaGoiHocNavigation == null ? null :
                    item.MaDangKyNavigation.MaGoiHocNavigation.TenGoiHoc,
                item.MaDangKyNavigation.ChiTietDangKyKhoaHoc
                    .OrderBy(detail => detail.MaKhoaHocNavigation.GiaiDoan)
                    .Select(detail => detail.MaKhoaHocNavigation.TenKhoaHoc).ToList(),
                item.MaDangKyNavigation.GiaDangKy, item.SoTien, item.TrangThai,
                new AdminPaymentGatewayDto(item.MaCongThanhToan,
                    item.MaCongThanhToanNavigation.MaCong,
                    item.MaCongThanhToanNavigation.TenCongThanhToan),
                item.NgayTao, item.NgayThanhToan)).ToListAsync(ct);

    private Task<List<AdminPaymentGatewayDto>> PaymentGateways(CancellationToken ct) =>
        db.CongThanhToan.AsNoTracking().OrderBy(item => item.TenCongThanhToan)
            .Select(item => new AdminPaymentGatewayDto(item.MaCongThanhToan,
                item.MaCong, item.TenCongThanhToan)).ToListAsync(ct);
}
