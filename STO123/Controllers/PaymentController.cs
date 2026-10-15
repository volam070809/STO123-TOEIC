using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using STO123.Models;
using STO123.Services.Payment;

namespace STO123.Controllers;

public sealed record CreatePaymentRequest(int MaGoiHoc);
public sealed record CoursePurchaseRequest(int[] CourseIds, decimal ExpectedTotal);
public sealed record CreatePaymentResponse(int MaGiaoDich, string CheckoutUrl);
public sealed record PaymentStatusDto(int MaGiaoDich, string TrangThai, string TrangThaiDangKy);
public sealed record PaymentHistoryDto(int MaGiaoDich, int? MaGoiHoc, string TenGoiHoc,
    decimal SoTien, DateTime NgayTao, DateTime? NgayThanhToan,
    string TrangThai, string TrangThaiDangKy, IReadOnlyList<int> CourseIds);

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/payment")]
public sealed class PaymentController(
    PaymentCreationService payments,
    PaymentConfirmationService confirmations,
    VNPayCallbackVerifier verifier,
    ToeicDbContext db,
    IOptions<VNPayOptions> options,
    ILogger<PaymentController> logger) : ControllerBase
{
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] CoursePurchaseRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        var result = await payments.QuoteCoursesAsync(learnerId, request.CourseIds, ct);
        return result.Error switch
        {
            PaymentCreationError.InvalidSelection => BadRequest(new { code = "INVALID_COURSE_SELECTION" }),
            PaymentCreationError.PackageUnavailable => BadRequest(new { code = "COURSE_UNAVAILABLE" }),
            PaymentCreationError.AlreadyActive => Conflict(new { code = "COURSE_ALREADY_ACTIVE" }),
            PaymentCreationError.AlreadyPending => Conflict(new { code = "PAYMENT_ALREADY_PENDING" }),
            _ => Ok(result.Quote)
        };
    }

    [HttpPost("create-courses")]
    public async Task<IActionResult> CreateCourses([FromBody] CoursePurchaseRequest request, CancellationToken ct)
    {
        if (!options.Value.CheckoutEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "CHECKOUT_DISABLED", message = "VNPay checkout is not available yet." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        var ip = HttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        var result = await payments.CreateCoursesAsync(learnerId, request.CourseIds,
            request.ExpectedTotal, ip.ToString(), ct);
        return result.Error switch
        {
            PaymentCreationError.LearnerUnavailable => Unauthorized(),
            PaymentCreationError.InvalidSelection => BadRequest(new { code = "INVALID_COURSE_SELECTION" }),
            PaymentCreationError.PackageUnavailable => BadRequest(new { code = "COURSE_UNAVAILABLE" }),
            PaymentCreationError.AlreadyActive => Conflict(new { code = "COURSE_ALREADY_ACTIVE" }),
            PaymentCreationError.AlreadyPending => Conflict(new { code = "PAYMENT_ALREADY_PENDING" }),
            PaymentCreationError.PriceChanged => Conflict(new { code = "QUOTE_CHANGED" }),
            _ => Ok(new CreatePaymentResponse(result.Payment!.TransactionId, result.Payment.CheckoutUrl))
        };
    }

    [NonAction] // Historical checkouts remain confirmable; new purchases use create-courses.
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        // Keep the gate ahead of every database read and write until the IPN phase is complete.
        if (!options.Value.CheckoutEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "CHECKOUT_DISABLED", message = "VNPay checkout is not available yet." });

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        if (request.MaGoiHoc <= 0)
            return BadRequest(new { code = "INVALID_PACKAGE" });

        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null || IPAddress.IsLoopback(ip))
            ip = IPAddress.Loopback;

        var outcome = await payments.CreateAsync(learnerId, request.MaGoiHoc, ip.ToString(), cancellationToken);
        return outcome.Error switch
        {
            PaymentCreationError.LearnerUnavailable => Unauthorized(),
            PaymentCreationError.PackageUnavailable => NotFound(new { code = "PACKAGE_UNAVAILABLE" }),
            PaymentCreationError.AlreadyActive => Conflict(new { code = "PACKAGE_ALREADY_ACTIVE" }),
            PaymentCreationError.AlreadyPending => Conflict(new { code = "PAYMENT_ALREADY_PENDING" }),
            _ => Ok(new CreatePaymentResponse(outcome.Payment!.TransactionId, outcome.Payment.CheckoutUrl))
        };
    }

    [AllowAnonymous]
    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> Ipn(CancellationToken cancellationToken)
    {
        var verification = verifier.Verify(Request.Query);
        if (verification.Error == VNPayCallbackError.InvalidSignature)
            return IpnReply("97", "Invalid signature");
        if (verification.Error != VNPayCallbackError.None)
            return IpnReply("99", "Invalid request");

        try
        {
            var result = await confirmations.ConfirmAsync(verification.Callback!, cancellationToken);
            return result switch
            {
                PaymentConfirmationResult.Confirmed => IpnReply("00", "Confirm Success"),
                PaymentConfirmationResult.AlreadyProcessed => IpnReply("02", "Order already confirmed"),
                PaymentConfirmationResult.TransactionNotFound => IpnReply("01", "Order not found"),
                PaymentConfirmationResult.InvalidAmount => IpnReply("04", "Invalid amount"),
                _ => IpnReply("99", "Reconciliation required")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Do not log the query, secret, signed URL, or database exception text.
            logger.LogError("VNPay IPN processing failed ({ErrorType}).", error.GetType().Name);
            return IpnReply("99", "Processing error");
        }
    }

    [AllowAnonymous]
    [HttpGet("vnpay-return")]
    public async Task<IActionResult> Return(CancellationToken cancellationToken)
    {
        int? paymentId = null;
        var verification = verifier.Verify(Request.Query);
        if (verification.Error == VNPayCallbackError.None)
        {
            paymentId = await db.GiaoDichThanhToan.AsNoTracking()
                .Where(item => item.MaCongThanhToanNavigation.MaCong == "VNPAY" &&
                    item.MaGiaoDichCongThanhToan == verification.Callback!.TransactionRef)
                .Select(item => (int?)item.MaGiaoDich)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var destination = options.Value.FrontendResultUrl.Trim();
        if (!string.IsNullOrEmpty(destination))
        {
            // The target is validated configuration, never a callback parameter.
            return Redirect(destination + (paymentId.HasValue ? $"?paymentId={paymentId.Value}" : string.Empty));
        }

        var detail = paymentId.HasValue ? $" Reference: {paymentId.Value}." : string.Empty;
        return Content($"<!doctype html><html><body><p>Payment is being processed. Return to STO123 to check its status.{detail}</p></body></html>",
            "text/html; charset=utf-8");
    }

    [HttpGet("status/{id:int}")]
    public async Task<IActionResult> Status(int id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        var result = await db.GiaoDichThanhToan.AsNoTracking()
            .Where(item => item.MaGiaoDich == id && item.MaDangKyNavigation.MaNguoiDung == learnerId)
            .Select(item => new PaymentStatusDto(item.MaGiaoDich, item.TrangThai,
                item.MaDangKyNavigation.TrangThai))
            .SingleOrDefaultAsync(cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var learnerId) || learnerId <= 0)
            return Unauthorized();
        var rows = await db.GiaoDichThanhToan.AsNoTracking()
            .Where(item => item.MaDangKyNavigation.MaNguoiDung == learnerId)
            .OrderByDescending(item => item.NgayTao).ThenByDescending(item => item.MaGiaoDich)
            .Include(item => item.MaDangKyNavigation)
                .ThenInclude(e => e.MaGoiHocNavigation)
                .ThenInclude(g => g.MaKhoaHoc)
            .Include(item => item.MaDangKyNavigation)
                .ThenInclude(e => e.ChiTietDangKyKhoaHoc)
                .ThenInclude(d => d.MaKhoaHocNavigation)
            .Take(100).ToListAsync(cancellationToken);
        var result = rows.Select(item => new PaymentHistoryDto(item.MaGiaoDich,
            item.MaDangKyNavigation.MaGoiHoc,
            item.MaDangKyNavigation.MaGoiHocNavigation?.TenGoiHoc ??
                string.Join(", ", item.MaDangKyNavigation.ChiTietDangKyKhoaHoc
                    .OrderBy(d => d.MaKhoaHoc).Select(d => d.MaKhoaHocNavigation.TenKhoaHoc)),
            item.SoTien, item.NgayTao, item.NgayThanhToan,
            item.TrangThai, item.MaDangKyNavigation.TrangThai,
            item.MaDangKyNavigation.MaGoiHocNavigation?.MaKhoaHoc.Select(c => c.MaKhoaHoc).ToArray() ??
                item.MaDangKyNavigation.ChiTietDangKyKhoaHoc.Select(d => d.MaKhoaHoc).ToArray())).ToList();
        return Ok(result);
    }

    private static IActionResult IpnReply(string code, string message) =>
        new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json; charset=utf-8",
            Content = JsonSerializer.Serialize(new { RspCode = code, Message = message })
        };
}
