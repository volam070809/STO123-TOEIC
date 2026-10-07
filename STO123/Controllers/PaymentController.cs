using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Payment;
using STO123.Services.Payment.Models;
using VNPAY;
using VNPAY.Models.Exceptions;

namespace STO123.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentController : ControllerBase
{
    private readonly ToeicDbContext _context;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IVnpayClient _vnpayClient;

    public PaymentController(
        ToeicDbContext context,
        IPaymentGateway paymentGateway,
        IVnpayClient vnpayClient)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _vnpayClient = vnpayClient;
    }

    [Authorize]
    [HttpPost("create")]
    public async Task<IActionResult> CreatePayment(
    [FromBody] CreatePaymentRequest request)
    {
        // 1. Lấy MaNguoiDung từ JWT
        var userIdClaim = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier);

        if (userIdClaim == null)
        {
            return Unauthorized(new
            {
                message = "Không xác định được người dùng."
            });
        }

        if (!int.TryParse(userIdClaim.Value, out int maNguoiDung))
        {
            return Unauthorized(new
            {
                message = "MaNguoiDung không hợp lệ."
            });
        }

        // 2. Kiểm tra gói học
        var goiHoc = await _context.GoiHoc
            .FirstOrDefaultAsync(x =>
                x.MaGoiHoc == request.MaGoiHoc &&
                x.TrangThai == "DANG_MO");

        if (goiHoc == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy gói học."
            });
        }

        // Kiểm tra user đã có gói này còn hạn hay chưa
        var dangKyDangSuDung = await _context.DangKyGoiHoc
            .FirstOrDefaultAsync(x =>
                x.MaNguoiDung == maNguoiDung &&
                x.MaGoiHoc == goiHoc.MaGoiHoc &&
                x.NgayKetThuc.HasValue &&
                x.NgayKetThuc.Value > DateTime.Now);

        if (dangKyDangSuDung != null)
        {
            return BadRequest(new
            {
                message = "Bạn đang sử dụng gói học này.",
                ngayKetThuc = dangKyDangSuDung.NgayKetThuc
            });
        }

        // 3. Tạo đăng ký gói học
        var dangKy = new DangKyGoiHoc
        {
            MaNguoiDung = maNguoiDung,
            MaGoiHoc = goiHoc.MaGoiHoc,
            GiaDangKy = goiHoc.Gia,
            SoNgaySuDung = goiHoc.SoNgaySuDung,
            NgayDangKy = DateTime.Now,
            TrangThai = "CHO_THANH_TOAN"
        };

        _context.DangKyGoiHoc.Add(dangKy);
        await _context.SaveChangesAsync();

        // 4. Tạo giao dịch thanh toán
        var giaoDich = new GiaoDichThanhToan
        {
            MaDangKy = dangKy.MaDangKy,
            MaCongThanhToan = 1,
            SoTien = goiHoc.Gia,
            MaGiaoDichCongThanhToan = Guid.NewGuid().ToString(),
            TrangThai = "CHO_XU_LY",
            NgayTao = DateTime.Now
        };

        _context.GiaoDichThanhToan.Add(giaoDich);
        await _context.SaveChangesAsync();

        // 5. Tạo URL VNPay
        var paymentResult = _paymentGateway.CreatePaymentUrl(
            goiHoc.Gia,
            $"Thanh toan goi {goiHoc.TenGoiHoc}",
            "https://contest-comply-portfolio.ngrok-free.dev/api/payment/vnpay-return"
        );

        // 6. Lưu mã giao dịch VNPay vào DB
        giaoDich.MaGiaoDichCongThanhToan =
            paymentResult.TransactionRef;

        await _context.SaveChangesAsync();

        // 7. Trả URL cho frontend
        return Ok(new
        {
            maGiaoDich = giaoDich.MaGiaoDich,
            maDangKy = dangKy.MaDangKy,
            transactionRef = paymentResult.TransactionRef,
            paymentUrl = paymentResult.PaymentUrl,
            qrCode = paymentResult.QrCode,
            soTien = giaoDich.SoTien,
            tenGoiHoc = goiHoc.TenGoiHoc
        });
    }

    [HttpGet("vnpay-return")]
    public async Task<IActionResult> VNPayReturn()
    {
        try
        {
            // 1. VNPay.NET kiểm tra chữ ký và đọc kết quả
            var paymentResult = _vnpayClient.GetPaymentResult(Request);

            // 2. Lấy mã giao dịch VNPay
            string transactionRef = paymentResult.PaymentId.ToString();

            // 3. Tìm giao dịch trong DB
            var giaoDich = await _context.GiaoDichThanhToan
                .Include(x => x.MaDangKyNavigation)
                .FirstOrDefaultAsync(x =>
                    x.MaCongThanhToan == 1 &&
                    x.MaGiaoDichCongThanhToan == transactionRef);

            if (giaoDich == null)
            {
                return NotFound(new
                {
                    message = "Không tìm thấy giao dịch thanh toán.",
                    transactionRef
                });
            }

            // 4. Nếu giao dịch đã xử lý thành công rồi
            // thì không xử lý lại
            if (giaoDich.TrangThai == "THANH_CONG")
            {
                return Ok(new
                {
                    message = "Giao dịch đã được xử lý trước đó.",
                    maGiaoDich = giaoDich.MaGiaoDich,
                    trangThai = giaoDich.TrangThai
                });
            }

            // 5. Kiểm tra kết quả thanh toán từ VNPay
            string responseCode =
                Request.Query["vnp_ResponseCode"].ToString();

            string transactionStatus =
                Request.Query["vnp_TransactionStatus"].ToString();

            if (responseCode != "00" || transactionStatus != "00")
            {
                giaoDich.TrangThai = "THAT_BAI";

                await _context.SaveChangesAsync();

                return BadRequest(new
                {
                    message = "Thanh toán thất bại.",
                    responseCode,
                    transactionStatus
                });
            }

            // 6. Thanh toán thành công
            var dangKy = giaoDich.MaDangKyNavigation;

            var ngayBatDau = DateTime.Now;

            giaoDich.TrangThai = "THANH_CONG";
            giaoDich.NgayThanhToan = ngayBatDau;

            dangKy.TrangThai = "DANG_SU_DUNG";
            dangKy.NgayBatDau = ngayBatDau;
            dangKy.NgayKetThuc =
                ngayBatDau.AddDays(dangKy.SoNgaySuDung);

            await _context.SaveChangesAsync();

            // 7. Trả kết quả
            return Ok(new
            {
                message = "Thanh toán thành công.",
                maGiaoDich = giaoDich.MaGiaoDich,
                maDangKy = dangKy.MaDangKy,
                transactionRef,
                trangThaiGiaoDich = giaoDich.TrangThai,
                trangThaiDangKy = dangKy.TrangThai,
                ngayBatDau = dangKy.NgayBatDau,
                ngayKetThuc = dangKy.NgayKetThuc
            });
        }
        catch (VnpayException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
    [Authorize]
    [HttpGet("status/{id}")]
    public async Task<IActionResult> GetPaymentStatus(int id)
    {
        try
        {
            var giaoDich = await _context.GiaoDichThanhToan
                .FirstOrDefaultAsync(x => x.MaGiaoDich == id);

            if (giaoDich == null)
            {
                return NotFound(new
                {
                    message = "Không tìm thấy giao dịch."
                });
            }

            if (giaoDich.TrangThai == "CHO_XU_LY" &&
                giaoDich.NgayTao.AddSeconds(30) <= DateTime.Now)
            {
                giaoDich.TrangThai = "THAT_BAI";

                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                maGiaoDich = giaoDich.MaGiaoDich,
                trangThai = giaoDich.TrangThai
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }
}