using System.Data;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Payment;

public enum PaymentConfirmationResult
{
    Confirmed,
    AlreadyProcessed,
    TransactionNotFound,
    InvalidAmount,
    NeedsReconciliation
}

public sealed class PaymentConfirmationService(
    ToeicDbContext db, TimeProvider clock, ILogger<PaymentConfirmationService> logger)
{
    private static readonly HashSet<string> KnownFailureCodes =
        ["09", "10", "11", "12", "13", "24", "51", "65", "75", "79"];

    public async Task<PaymentConfirmationResult> ConfirmAsync(VNPayCallback callback,
        CancellationToken cancellationToken)
    {
        var providerId = await db.CongThanhToan.AsNoTracking()
            .Where(item => item.MaCong == "VNPAY")
            .Select(item => (int?)item.MaCongThanhToan)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null) return PaymentConfirmationResult.TransactionNotFound;

        var learnerId = await db.GiaoDichThanhToan.AsNoTracking()
            .Where(item => item.MaCongThanhToan == providerId.Value &&
                item.MaGiaoDichCongThanhToan == callback.TransactionRef)
            .Select(item => (int?)item.MaDangKyNavigation.MaNguoiDung)
            .SingleOrDefaultAsync(cancellationToken);
        if (learnerId is null) return PaymentConfirmationResult.TransactionNotFound;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
            cancellationToken);

        // Serialize all checkout and IPN changes for this learner on the same row.
        if (db.Database.IsSqlServer())
            await db.NguoiDung.FromSqlInterpolated($"""
                SELECT * FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK)
                WHERE MaNguoiDung = {learnerId.Value}
                """).AsNoTracking().SingleAsync(cancellationToken);

        // SQL Server serializes callbacks for this merchant reference on the
        // payment row. SQLite takes the ordinary path for isolated tests.
        GiaoDichThanhToan? payment;
        if (db.Database.IsSqlServer())
        {
            payment = await db.GiaoDichThanhToan.FromSqlInterpolated($"""
                SELECT * FROM dbo.GiaoDichThanhToan WITH (UPDLOCK, HOLDLOCK)
                WHERE MaCongThanhToan = {providerId.Value}
                  AND MaGiaoDichCongThanhToan = {callback.TransactionRef}
                """).SingleOrDefaultAsync(cancellationToken);
        }
        else
        {
            payment = await db.GiaoDichThanhToan.SingleOrDefaultAsync(item =>
                item.MaCongThanhToan == providerId.Value &&
                item.MaGiaoDichCongThanhToan == callback.TransactionRef, cancellationToken);
        }

        if (payment is null) return PaymentConfirmationResult.TransactionNotFound;
        // The relationship is immutable in the application, but recheck it
        // inside the lock before acting on the earlier lookup.
        var owner = await db.DangKyGoiHoc.AsNoTracking()
            .Where(item => item.MaDangKy == payment.MaDangKy)
            .Select(item => item.MaNguoiDung).SingleAsync(cancellationToken);
        if (owner != learnerId.Value) return PaymentConfirmationResult.NeedsReconciliation;
        if (payment.SoTien * 100m != callback.Amount)
        {
            if (payment.TrangThai != "CHO_XU_LY")
                logger.LogWarning("Conflicting VNPay amount for finalized payment {PaymentId}; reconciliation required.",
                    payment.MaGiaoDich);
            return PaymentConfirmationResult.InvalidAmount;
        }

        var confirmedSuccess = callback.ResponseCode == "00" && callback.TransactionStatus == "00";
        // A known response failure is final only with VNPay's failed transaction
        // status. Other combinations may still involve money movement.
        var confirmedFailure = KnownFailureCodes.Contains(callback.ResponseCode) &&
                               callback.TransactionStatus == "02";

        if (payment.TrangThai != "CHO_XU_LY")
        {
            if ((payment.TrangThai == "THANH_CONG") != confirmedSuccess ||
                (!confirmedSuccess && !confirmedFailure))
                logger.LogWarning("Conflicting VNPay notification for payment {PaymentId}; reconciliation required.",
                    payment.MaGiaoDich);
            return PaymentConfirmationResult.AlreadyProcessed;
        }

        // Code 07 means money may have been deducted. Conflicting or unknown
        // provider codes also need reconciliation, not an irreversible failure.
        if (!confirmedSuccess && !confirmedFailure)
        {
            logger.LogWarning("Uncertain VNPay notification for payment {PaymentId}; reconciliation required.",
                payment.MaGiaoDich);
            return PaymentConfirmationResult.NeedsReconciliation;
        }

        if (confirmedSuccess)
        {
            await db.Entry(payment).Reference(item => item.MaDangKyNavigation)
                .LoadAsync(cancellationToken);
            var enrollment = payment.MaDangKyNavigation;
            if (enrollment is null || enrollment.TrangThai != "CHO_THANH_TOAN" ||
                enrollment.NgayBatDau is not null || enrollment.NgayKetThuc is not null ||
                enrollment.SoNgaySuDung <= 0)
            {
                logger.LogWarning("Invalid enrollment state for payment {PaymentId}; reconciliation required.",
                    payment.MaGiaoDich);
                return PaymentConfirmationResult.NeedsReconciliation;
            }

            if (enrollment.MaGoiHoc is null)
            {
                await db.Entry(enrollment).Collection(e => e.ChiTietDangKyKhoaHoc)
                    .LoadAsync(cancellationToken);
                var details = enrollment.ChiTietDangKyKhoaHoc;
                if (details.Count == 0 || enrollment.SoNgaySuDung != PaymentCreationService.CourseValidDays ||
                    details.Any(d => d.DonGia <= 0) || details.Sum(d => d.DonGia) != enrollment.GiaDangKy ||
                    enrollment.GiaDangKy != payment.SoTien)
                {
                    logger.LogWarning("Invalid custom-course enrollment for payment {PaymentId}; reconciliation required.",
                        payment.MaGiaoDich);
                    return PaymentConfirmationResult.NeedsReconciliation;
                }
            }

            var activatedAt = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            payment.TrangThai = "THANH_CONG";
            payment.NgayThanhToan = activatedAt;
            // Once VNPay has confirmed the charge, the purchased enrollment must
            // receive its entitlement, even if another checkout completed meanwhile.
            enrollment.TrangThai = "DANG_SU_DUNG";
            enrollment.NgayBatDau = activatedAt;
            enrollment.NgayKetThuc = activatedAt.AddDays(enrollment.SoNgaySuDung);
        }
        else
        {
            payment.TrangThai = "THAT_BAI";
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentConfirmationResult.Confirmed;
    }
}
