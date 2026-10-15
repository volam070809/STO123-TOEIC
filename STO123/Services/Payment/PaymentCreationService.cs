using System.Data;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Services.Payment;

public sealed record PaymentCreationResult(int TransactionId, string CheckoutUrl);

public enum PaymentCreationError
{
    None,
    LearnerUnavailable,
    PackageUnavailable,
    AlreadyActive,
    AlreadyPending,
    InvalidSelection,
    PriceChanged
}

public sealed record PaymentCreationOutcome(PaymentCreationError Error, PaymentCreationResult? Payment = null);
public sealed record CourseQuoteItem(int CourseId, string Name, decimal Price);
public sealed record CourseQuote(IReadOnlyList<CourseQuoteItem> Courses, decimal Total, int ValidDays);
public sealed record CourseQuoteOutcome(PaymentCreationError Error, CourseQuote? Quote = null);

public sealed class PaymentCreationService(ToeicDbContext db, IPaymentGateway gateway, TimeProvider clock)
{
    public const int CourseValidDays = 30;

    public Task<CourseQuoteOutcome> QuoteCoursesAsync(int learnerId, IReadOnlyList<int>? courseIds,
        CancellationToken ct) => ValidateCoursesAsync(learnerId, courseIds, ct);

    private async Task<CourseQuoteOutcome> ValidateCoursesAsync(int learnerId, IReadOnlyList<int>? courseIds,
        CancellationToken ct)
    {
        if (courseIds is null || courseIds.Count is < 1 or > 100 ||
            courseIds.Any(id => id <= 0) || courseIds.Distinct().Count() != courseIds.Count)
            return new(PaymentCreationError.InvalidSelection);
        var requested = courseIds.ToArray();
        var courses = await db.KhoaHoc.AsNoTracking().Where(c => requested.Contains(c.MaKhoaHoc))
            .Select(c => new CourseQuoteItem(c.MaKhoaHoc, c.TenKhoaHoc,
                c.GiaBanLe ?? 0m)).ToListAsync(ct);
        if (courses.Count != requested.Length || courses.Any(c => c.Price <= 0 ||
            c.Price > 9_999_999_999m ||
            c.Price != decimal.Truncate(c.Price))) return new(PaymentCreationError.PackageUnavailable);
        var availableCount = await db.KhoaHoc.AsNoTracking().CountAsync(c =>
            requested.Contains(c.MaKhoaHoc) && c.TrangThai == "DANG_MO", ct);
        if (availableCount != requested.Length) return new(PaymentCreationError.PackageUnavailable);
        var localNow = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        if (await CourseEntitlement.ActiveCourseIds(db, learnerId, localNow)
            .AnyAsync(id => requested.Contains(id), ct)) return new(PaymentCreationError.AlreadyActive);
        if (await CourseEntitlement.PendingCourseIds(db, learnerId)
            .AnyAsync(id => requested.Contains(id), ct)) return new(PaymentCreationError.AlreadyPending);
        var ordered = courses.OrderBy(c => c.CourseId).ToArray();
        var total = ordered.Sum(c => c.Price);
        if (total > 9_999_999_999m) return new(PaymentCreationError.PackageUnavailable);
        return new(PaymentCreationError.None, new CourseQuote(ordered, total, CourseValidDays));
    }

    public async Task<PaymentCreationOutcome> CreateCoursesAsync(int learnerId,
        IReadOnlyList<int>? courseIds, decimal expectedTotal, string clientIp, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (db.Database.IsSqlServer())
        {
            var user = await db.NguoiDung.FromSqlInterpolated($"""
                SELECT * FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK)
                WHERE MaNguoiDung = {learnerId}
                """).AsNoTracking().SingleOrDefaultAsync(ct);
            if (user is null || user.VaiTro != "HOC_VIEN" || user.TrangThai != "HOAT_DONG")
                return new(PaymentCreationError.LearnerUnavailable);
        }
        else if (!await db.NguoiDung.AsNoTracking().AnyAsync(u => u.MaNguoiDung == learnerId &&
            u.VaiTro == "HOC_VIEN" && u.TrangThai == "HOAT_DONG", ct))
            return new(PaymentCreationError.LearnerUnavailable);
        var quote = await ValidateCoursesAsync(learnerId, courseIds, ct);
        if (quote.Error != PaymentCreationError.None) return new(quote.Error);
        if (quote.Quote!.Total != expectedTotal) return new(PaymentCreationError.PriceChanged);
        var providerId = await db.CongThanhToan.AsNoTracking().Where(p => p.MaCong == "VNPAY")
            .Select(p => (int?)p.MaCongThanhToan).SingleOrDefaultAsync(ct);
        if (providerId is null) throw new InvalidOperationException("VNPay provider is not configured in the database.");
        var reference = Guid.NewGuid().ToString("N");
        var checkoutUrl = gateway.CreateCheckoutUrl(reference, quote.Quote.Total, clientIp, now);
        var enrollment = new DangKyGoiHoc
        {
            MaNguoiDung = learnerId, MaGoiHoc = null, GiaDangKy = quote.Quote.Total,
            SoNgaySuDung = CourseValidDays,
            NgayDangKy = now.ToOffset(TimeSpan.FromHours(7)).DateTime,
            TrangThai = "CHO_THANH_TOAN",
            ChiTietDangKyKhoaHoc = quote.Quote.Courses.Select(c => new ChiTietDangKyKhoaHoc
                { MaKhoaHoc = c.CourseId, DonGia = c.Price }).ToList()
        };
        var payment = new GiaoDichThanhToan
        {
            MaDangKyNavigation = enrollment, MaCongThanhToan = providerId.Value,
            SoTien = quote.Quote.Total, MaGiaoDichCongThanhToan = reference,
            TrangThai = "CHO_XU_LY", NgayTao = enrollment.NgayDangKy
        };
        db.GiaoDichThanhToan.Add(payment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(PaymentCreationError.None, new(payment.MaGiaoDich, checkoutUrl));
    }

    public async Task<PaymentCreationOutcome> CreateAsync(int learnerId, int packageId, string clientIp,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var localNow = now.ToOffset(TimeSpan.FromHours(7)).DateTime;
        // Keep the eligibility read and both inserts in one serializable unit.
        // Concurrent requests for the same learner/package cannot both pass the
        // pending check and commit a new checkout.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        if (db.Database.IsSqlServer())
        {
            var lockedLearner = await db.NguoiDung.FromSqlInterpolated($"""
                SELECT * FROM dbo.NguoiDung WITH (UPDLOCK, HOLDLOCK)
                WHERE MaNguoiDung = {learnerId}
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (lockedLearner is null || lockedLearner.VaiTro != "HOC_VIEN" ||
                lockedLearner.TrangThai != "HOAT_DONG")
                return new(PaymentCreationError.LearnerUnavailable);
        }
        else if (!await db.NguoiDung.AsNoTracking().AnyAsync(user =>
            user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN" && user.TrangThai == "HOAT_DONG",
            cancellationToken))
            return new(PaymentCreationError.LearnerUnavailable);
        var package = await db.GoiHoc.AsNoTracking().FirstOrDefaultAsync(item =>
            item.MaGoiHoc == packageId && item.TrangThai == "DANG_MO", cancellationToken);
        if (package is null || package.Gia <= 0 || package.Gia != decimal.Truncate(package.Gia) ||
            package.SoNgaySuDung <= 0)
            return new(PaymentCreationError.PackageUnavailable);

        var courseIds = await db.GoiHoc.AsNoTracking()
            .Where(item => item.MaGoiHoc == packageId)
            .SelectMany(item => item.MaKhoaHoc.Select(course => new { course.MaKhoaHoc, course.TrangThai }))
            .ToListAsync(cancellationToken);
        if (courseIds.Count == 0 || courseIds.Any(course => course.TrangThai != "DANG_MO"))
            return new(PaymentCreationError.PackageUnavailable);
        var selectedCourseIds = courseIds.Select(course => course.MaKhoaHoc).ToArray();

        var active = await db.DangKyGoiHoc.AsNoTracking().AnyAsync(item =>
            item.MaNguoiDung == learnerId &&
            item.TrangThai == "DANG_SU_DUNG" && item.NgayBatDau <= localNow &&
            item.NgayKetThuc > localNow &&
            item.GiaoDichThanhToan.Any(payment => payment.TrangThai == "THANH_CONG") &&
            item.MaGoiHocNavigation.MaKhoaHoc.Any(course =>
                selectedCourseIds.Contains(course.MaKhoaHoc)), cancellationToken);
        if (active) return new(PaymentCreationError.AlreadyActive);

        var pending = await db.DangKyGoiHoc.AsNoTracking().AnyAsync(item =>
            item.MaNguoiDung == learnerId && item.TrangThai == "CHO_THANH_TOAN" &&
            item.MaGoiHocNavigation.MaKhoaHoc.Any(course =>
                selectedCourseIds.Contains(course.MaKhoaHoc)) &&
            item.GiaoDichThanhToan.Any(transaction =>
                transaction.TrangThai == "CHO_XU_LY" || transaction.TrangThai == "THANH_CONG"),
            cancellationToken);
        if (pending) return new(PaymentCreationError.AlreadyPending);

        var providerId = await db.CongThanhToan.AsNoTracking()
            .Where(provider => provider.MaCong == "VNPAY")
            .Select(provider => (int?)provider.MaCongThanhToan)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null)
            throw new InvalidOperationException("VNPay provider is not configured in the database.");

        var transactionRef = Guid.NewGuid().ToString("N");
        var checkoutUrl = gateway.CreateCheckoutUrl(transactionRef, package.Gia, clientIp, now);
        var enrollment = new DangKyGoiHoc
        {
            MaNguoiDung = learnerId,
            MaGoiHoc = packageId,
            GiaDangKy = package.Gia,
            SoNgaySuDung = package.SoNgaySuDung,
            NgayDangKy = localNow,
            TrangThai = "CHO_THANH_TOAN"
        };
        var payment = new GiaoDichThanhToan
        {
            MaDangKyNavigation = enrollment,
            MaCongThanhToan = providerId.Value,
            SoTien = package.Gia,
            MaGiaoDichCongThanhToan = transactionRef,
            TrangThai = "CHO_XU_LY",
            NgayTao = localNow
        };

        db.DangKyGoiHoc.Add(enrollment);
        db.GiaoDichThanhToan.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PaymentCreationError.None, new(payment.MaGiaoDich, checkoutUrl));
    }
}
