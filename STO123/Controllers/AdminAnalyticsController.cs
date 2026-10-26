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
[Route("api/admin")]
public sealed class AdminAnalyticsController(ToeicDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rangeResult = Range(request, 366);
        if (rangeResult.Error is not null) return rangeResult.Error;
        var range = rangeResult.Value!;
        var newLearners = await LearnersIn(range).CountAsync(ct);
        var enrollments = await EnrollmentsIn(range).CountAsync(ct);
        var paymentDaily = await AdminAnalyticsQueries.DashboardPayments(db, range.From, range.ToExclusive)
            .ToListAsync(ct);
        var learnerTrend = await Daily(db.NguoiDung.AsNoTracking().Where(x => x.VaiTro == "HOC_VIEN" &&
            x.NgayTao >= range.From && x.NgayTao < range.ToExclusive).GroupBy(x => x.NgayTao.Date)
            .Select(g => new AdminTrendPointDto(g.Key, g.Count())), range).ToListAsync(ct);
        var revenueTrend = paymentDaily.Select(x => new AdminTrendPointDto(x.Date, x.Successful, x.Revenue)).ToList();
        var recentEnrollments = await EnrollmentsIn(range).OrderByDescending(x => x.NgayDangKy)
            .ThenByDescending(x => x.MaDangKy).Take(6).Select(x => new AdminRecentEnrollmentDto(x.MaDangKy,
                x.MaNguoiDung, x.MaNguoiDungNavigation.HoTen, x.MaGoiHoc == null ? "DIRECT_COURSES" : "PACKAGE",
                x.MaGoiHocNavigation == null ? null : x.MaGoiHocNavigation.TenGoiHoc,
                x.GiaDangKy, x.TrangThai, x.NgayDangKy)).ToListAsync(ct);
        var recentPayments = await db.GiaoDichThanhToan.AsNoTracking()
            .Where(x => x.NgayTao >= range.From && x.NgayTao < range.ToExclusive)
            .OrderByDescending(x => x.NgayTao)
            .ThenByDescending(x => x.MaGiaoDich).Take(6).Select(x => new AdminRecentPaymentDto(x.MaGiaoDich,
                x.MaDangKy, x.MaDangKyNavigation.MaNguoiDungNavigation.HoTen, x.SoTien, x.TrangThai,
                x.MaCongThanhToanNavigation.MaCong, x.NgayTao)).ToListAsync(ct);
        var performance = await PackagePerformance(range, 3, ct);
        performance.AddRange(await CoursePerformance(range, 3, ct));
        return Ok(new AdminDashboardDto(range.Dto, newLearners, enrollments, paymentDaily.Sum(x => x.Revenue),
            paymentDaily.Sum(x => x.Pending), paymentDaily.Sum(x => x.Failed), await Fill(learnerTrend, range),
            await Fill(revenueTrend, range), recentEnrollments, recentPayments, performance));
    }

    [HttpGet("reports/overview")]
    public async Task<IActionResult> Overview([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!;
        var previous = r.AllHistory ? null : r.Previous();
        var combined = previous is null ? r : new AnalyticsRange(previous.From, r.ToExclusive); var now = LocalNow;
        var learnerDaily = await LearnersIn(combined).GroupBy(x => x.NgayTao.Date)
            .Select(g => new AdminTrendPointDto(g.Key, g.Count())).ToListAsync(ct);
        var enrollmentDaily = await EnrollmentsIn(combined).GroupBy(x => x.NgayDangKy.Date)
            .Select(g => new AdminTrendPointDto(g.Key, g.Count())).ToListAsync(ct);
        var revenueDaily = await PaymentsIn(combined).Where(x => x.TrangThai == "THANH_CONG")
            .GroupBy(x => (x.NgayThanhToan ?? x.NgayTao).Date)
            .Select(g => new AdminTrendPointDto(g.Key, g.Count(), g.Sum(x => x.SoTien)))
            .ToListAsync(ct);
        var attemptDaily = await AttemptsIn(combined).GroupBy(x => x.NgayLamBai.Date)
            .Select(g => new AdminTrendPointDto(g.Key, g.Count())).ToListAsync(ct);
        var learnerTotals = await db.NguoiDung.AsNoTracking().Where(x => x.VaiTro == "HOC_VIEN").GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Active = g.Count(x => x.TrangThai == "HOAT_DONG") })
            .SingleOrDefaultAsync(ct);
        var valid = await db.DangKyGoiHoc.AsNoTracking().CountAsync(x => x.TrangThai == "DANG_SU_DUNG" &&
            x.NgayBatDau <= now && x.NgayKetThuc > now && x.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG"), ct);

        static int Count(IReadOnlyList<AdminTrendPointDto> rows, DateTime from, DateTime to) =>
            rows.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Count);
        static decimal Amount(IReadOnlyList<AdminTrendPointDto> rows, DateTime from, DateTime to) =>
            rows.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Amount);
        var learnerTrend = learnerDaily.Where(x => x.Date >= r.From).ToList();
        var enrollmentTrend = enrollmentDaily.Where(x => x.Date >= r.From).ToList();
        var revenueTrend = revenueDaily.Where(x => x.Date >= r.From).ToList();
        return Ok(new AdminOverviewReportDto(r.Dto,
            Compare(Count(learnerDaily, r.From, r.ToExclusive), previous is null ? null : Count(learnerDaily, previous.From, previous.ToExclusive)),
            Compare(Count(enrollmentDaily, r.From, r.ToExclusive), previous is null ? null : Count(enrollmentDaily, previous.From, previous.ToExclusive)),
            Compare(Amount(revenueDaily, r.From, r.ToExclusive), previous is null ? null : Amount(revenueDaily, previous.From, previous.ToExclusive)),
            Compare(Count(attemptDaily, r.From, r.ToExclusive), previous is null ? null : Count(attemptDaily, previous.From, previous.ToExclusive)),
            learnerTotals?.Total ?? 0, learnerTotals?.Active ?? 0, valid,
            await Fill(learnerTrend, r), await Fill(enrollmentTrend, r), await Fill(revenueTrend, r)));
    }

    [HttpGet("reports/learners")]
    public async Task<IActionResult> Learners([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!;
        var total = await db.NguoiDung.AsNoTracking().CountAsync(x => x.VaiTro == "HOC_VIEN", ct);
        var status = await db.NguoiDung.AsNoTracking().Where(x => x.VaiTro == "HOC_VIEN").GroupBy(x => x.TrangThai)
            .Select(g => new AdminBreakdownDto(g.Key, g.Count())).ToListAsync(ct);
        var attempts = AttemptsIn(r); var attemptStats = await attempts.GroupBy(_ => 1).Select(g => new {
            Total = g.Count(), Completed = g.Count(x => x.NgayNopBai != null), Learners = g.Select(x => x.MaHocVien).Distinct().Count()
        }).SingleOrDefaultAsync(ct);
        var learnerTrend = await LearnersIn(r).GroupBy(x => x.NgayTao.Date).Select(g => new AdminTrendPointDto(g.Key, g.Count())).ToListAsync(ct);
        var activityTrend = await attempts.GroupBy(x => x.NgayLamBai.Date).Select(g => new AdminTrendPointDto(g.Key, g.Count())).ToListAsync(ct);
        return Ok(new AdminLearnersReportDto(r.Dto, total, await LearnersIn(r).CountAsync(ct),
            attemptStats?.Learners ?? 0, attemptStats?.Total ?? 0, attemptStats?.Completed ?? 0,
            status, await Fill(learnerTrend, r), await Fill(activityTrend, r)));
    }

    [HttpGet("reports/enrollments")]
    public async Task<IActionResult> Enrollments([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!; var now = LocalNow;
        var stats = await EnrollmentsIn(r).GroupBy(_ => 1).Select(g => new AdminEnrollmentsReportDto(r.Dto,
            g.Count(), g.Count(x => x.MaGoiHoc != null), g.Count(x => x.MaGoiHoc == null),
            g.Count(x => x.TrangThai == "CHO_THANH_TOAN"), g.Count(x => x.TrangThai == "DANG_SU_DUNG"),
            g.Count(x => x.TrangThai == "HET_HAN"), g.Count(x => x.TrangThai == "DANG_SU_DUNG" &&
                x.NgayBatDau <= now && x.NgayKetThuc > now && x.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG")),
            g.Sum(x => x.GiaDangKy), Array.Empty<AdminTrendPointDto>())).SingleOrDefaultAsync(ct);
        var trend = await EnrollmentsIn(r).GroupBy(x => x.NgayDangKy.Date).Select(g => new AdminTrendPointDto(g.Key, g.Count(), g.Sum(x => x.GiaDangKy))).ToListAsync(ct);
        return Ok((stats ?? new(r.Dto, 0, 0, 0, 0, 0, 0, 0, 0, [])) with { Trend = await Fill(trend, r) });
    }

    [HttpGet("reports/packages-courses")]
    public async Task<IActionResult> Offerings([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!;
        var packageStates = await db.GoiHoc.AsNoTracking().GroupBy(_ => 1).Select(g => new {
            Open = g.Count(x => x.TrangThai == "DANG_MO"), Closed = g.Count(x => x.TrangThai == "DA_DONG") }).SingleOrDefaultAsync(ct);
        var courseStates = await db.KhoaHoc.AsNoTracking().GroupBy(_ => 1).Select(g => new {
            Open = g.Count(x => x.TrangThai == "DANG_MO"), Closed = g.Count(x => x.TrangThai == "DA_DONG") }).SingleOrDefaultAsync(ct);
        var packages = await PackagePerformance(r, 100, ct);
        var courses = await CoursePerformance(r, 100, ct);
        return Ok(new AdminOfferingsReportDto(r.Dto, packageStates?.Open ?? 0, packageStates?.Closed ?? 0,
            courseStates?.Open ?? 0, courseStates?.Closed ?? 0, packages, courses));
    }

    [HttpGet("reports/payments")]
    public async Task<IActionResult> Payments([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!; var q = PaymentsIn(r);
        var stats = await q.GroupBy(_ => 1).Select(g => new { Total = g.Count(),
            Pending = g.Count(x => x.TrangThai == "CHO_XU_LY"), Success = g.Count(x => x.TrangThai == "THANH_CONG"),
            Failed = g.Count(x => x.TrangThai == "THAT_BAI"), Revenue = g.Where(x => x.TrangThai == "THANH_CONG").Sum(x => x.SoTien),
            Average = g.Where(x => x.TrangThai == "THANH_CONG").Average(x => (decimal?)x.SoTien) }).SingleOrDefaultAsync(ct);
        var gateways = await q.GroupBy(x => new { x.MaCongThanhToanNavigation.MaCong, x.MaCongThanhToanNavigation.TenCongThanhToan })
            .Select(g => new AdminBreakdownDto(g.Key.MaCong, g.Count(),
                g.Where(x => x.TrangThai == "THANH_CONG").Sum(x => x.SoTien))).ToListAsync(ct);
        var trend = await q.GroupBy(x => (x.TrangThai == "THANH_CONG" && x.NgayThanhToan.HasValue
            ? x.NgayThanhToan.Value : x.NgayTao).Date).Select(g => new AdminTrendPointDto(g.Key, g.Count(),
            g.Where(x => x.TrangThai == "THANH_CONG").Sum(x => x.SoTien))).ToListAsync(ct);
        return Ok(new AdminPaymentsReportDto(r.Dto, stats?.Total ?? 0, stats?.Pending ?? 0,
            stats?.Success ?? 0, stats?.Failed ?? 0, stats?.Revenue ?? 0, stats?.Average ?? 0,
            gateways, await Fill(trend, r)));
    }

    [HttpGet("reports/toeic-results")]
    public async Task<IActionResult> ToeicResults([FromQuery] AdminAnalyticsQuery request, CancellationToken ct)
    {
        var rr = Range(request, 366, true); if (rr.Error is not null) return rr.Error; var r = rr.Value!; var q = AttemptsIn(r);
        var summary = await q.GroupBy(_ => 1).Select(g => new { Total = g.Count(),
            Completed = g.Count(x => x.NgayNopBai != null), Learners = g.Select(x => x.MaHocVien).Distinct().Count() }).SingleOrDefaultAsync(ct);
        var types = await q.GroupBy(x => x.LoaiBaiLam).Select(g => new AdminAttemptTypeDto(g.Key, g.Count(),
            g.Count(x => x.NgayNopBai != null), g.Where(x => x.NgayNopBai != null).Average(x => (double?)x.DiemNghe),
            g.Where(x => x.NgayNopBai != null).Average(x => (double?)x.DiemDoc),
            g.Where(x => x.NgayNopBai != null).Average(x => (double?)x.DiemTong))).ToListAsync(ct);
        var parts = await db.CauHoiLuotLam.AsNoTracking().Where(x => x.MaKetQuaNavigation.NgayLamBai >= r.From &&
            x.MaKetQuaNavigation.NgayLamBai < r.ToExclusive && x.MaKetQuaNavigation.NgayNopBai != null)
            .GroupBy(x => x.MaPartNavigation.SoPart).Select(g => new { Part = g.Key, Questions = g.Count(),
                Answered = g.Count(x => x.ChiTietKetQua != null && x.ChiTietKetQua.DapAnChon != null),
                Correct = g.Count(x => x.ChiTietKetQua != null && x.ChiTietKetQua.DapAnChon == x.PhuongAnDung) })
            .OrderBy(x => x.Part).ToListAsync(ct);
        var trend = await q.GroupBy(x => x.NgayLamBai.Date).Select(g => new AdminTrendPointDto(g.Key, g.Count(), 0,
            g.Where(x => x.NgayNopBai != null).Average(x => (double?)x.DiemTong),
            g.Count(x => x.NgayNopBai != null))).ToListAsync(ct);
        var classifications = await db.KetQuaPhanLopKNN.AsNoTracking().CountAsync(x =>
            x.NgayPhanLop >= r.From && x.NgayPhanLop < r.ToExclusive, ct);
        var progress = await db.TienDoBuocLoTrinh.AsNoTracking().Where(x => x.CapNhatLuc >= r.From && x.CapNhatLuc < r.ToExclusive)
            .GroupBy(_ => 1).Select(g => new { Total = g.Count(), Completed = g.Count(x => x.TrangThai == "HOAN_THANH" || x.TyLeHoanThanh == 100),
                Average = g.Average(x => (double?)x.TyLeHoanThanh) }).SingleOrDefaultAsync(ct);
        return Ok(new AdminToeicResultsReportDto(r.Dto, summary?.Total ?? 0, summary?.Completed ?? 0,
            summary?.Learners ?? 0, classifications, progress?.Total ?? 0, progress?.Completed ?? 0, progress?.Average,
            types, parts.Select(x => new AdminPartPerformanceDto(x.Part, x.Questions,
                x.Answered, x.Correct, AdminAnalyticsMath.Accuracy(x.Correct, x.Answered))).ToList(),
            await Fill(trend, r)));
    }

    private DateTime LocalNow => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
    private IQueryable<NguoiDung> LearnersIn(AnalyticsRange r) => db.NguoiDung.AsNoTracking().Where(x =>
        x.VaiTro == "HOC_VIEN" && x.NgayTao >= r.From && x.NgayTao < r.ToExclusive);
    private IQueryable<DangKyGoiHoc> EnrollmentsIn(AnalyticsRange r) => db.DangKyGoiHoc.AsNoTracking().Where(x =>
        x.NgayDangKy >= r.From && x.NgayDangKy < r.ToExclusive);
    private IQueryable<GiaoDichThanhToan> PaymentsIn(AnalyticsRange r) => db.GiaoDichThanhToan.AsNoTracking().Where(x =>
        (x.TrangThai == "THANH_CONG" && x.NgayThanhToan.HasValue ? x.NgayThanhToan.Value : x.NgayTao) >= r.From &&
        (x.TrangThai == "THANH_CONG" && x.NgayThanhToan.HasValue ? x.NgayThanhToan.Value : x.NgayTao) < r.ToExclusive);
    private IQueryable<KetQuaLamBai> AttemptsIn(AnalyticsRange r) => db.KetQuaLamBai.AsNoTracking().Where(x =>
        x.NgayLamBai >= r.From && x.NgayLamBai < r.ToExclusive &&
        (x.LoaiBaiLam == "PLACEMENT" || x.LoaiBaiLam == "MOCK" || x.LoaiBaiLam == "PRACTICE"));

    private async Task<List<AdminPerformanceDto>> PackagePerformance(AnalyticsRange r, int take, CancellationToken ct) =>
        (await AdminAnalyticsQueries.PackagePerformance(db, r.From, r.ToExclusive)
            .OrderByDescending(x => x.Revenue).ThenByDescending(x => x.Count).Take(take).ToListAsync(ct))
            .Select(x => new AdminPerformanceDto(x.Id, x.Name, x.Type, x.Count, x.Revenue)).ToList();

    private async Task<List<AdminPerformanceDto>> CoursePerformance(AnalyticsRange r, int take, CancellationToken ct) =>
        (await AdminAnalyticsQueries.CoursePerformance(db, r.From, r.ToExclusive)
            .Where(x => x.Count > 0).OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Revenue).Take(take).ToListAsync(ct))
            .Select(x => new AdminPerformanceDto(x.Id, x.Name, x.Type, x.Count, x.Revenue)).ToList();

    private (AnalyticsRange? Value, BadRequestObjectResult? Error) Range(AdminAnalyticsQuery request, int maxDays,
        bool allWhenMissing = false)
    {
        var dateError = AdminDateRangeValidation.Error(request.DateFrom, request.DateTo, maxDays);
        if (dateError is not null)
            return (null, BadRequest(new { code = "INVALID_DATE_RANGE", message = dateError }));
        var today = LocalNow.Date;
        var allHistory = allWhenMissing && !request.DateFrom.HasValue && !request.DateTo.HasValue;
        var from = request.DateFrom?.Date ?? (allHistory ? AdminDateRangeValidation.SqlDateTimeMinimum : new DateTime(today.Year, 1, 1));
        var to = request.DateTo?.Date ?? today;
        return (new AnalyticsRange(from, AdminDateRangeValidation.ExclusiveEnd(to), allHistory), null);
    }

    private static IQueryable<AdminTrendPointDto> Daily(IQueryable<AdminTrendPointDto> query, AnalyticsRange _) => query;
    private static Task<IReadOnlyList<AdminTrendPointDto>> Fill(IReadOnlyList<AdminTrendPointDto> rows, AnalyticsRange range)
    {
        var result = new List<AdminTrendPointDto>();
        if (range.AllHistory && rows.Count == 0)
            return Task.FromResult<IReadOnlyList<AdminTrendPointDto>>(result);
        var fillFrom = range.AllHistory ? rows.Min(x => x.Date).Date : range.From;
        if ((range.ToExclusive - fillFrom).TotalDays > 62)
        {
            for (var month = new DateTime(fillFrom.Year, fillFrom.Month, 1);
                month < range.ToExclusive; month = month.AddMonths(1))
            {
                var monthEnd = month.AddMonths(1); var matching = rows.Where(x =>
                    x.Date >= fillFrom && x.Date < range.ToExclusive && x.Date >= month && x.Date < monthEnd).ToList();
                var averageSamples = matching.Sum(x => x.AverageSamples);
                double? average = averageSamples == 0 ? null :
                    matching.Sum(x => (x.Average ?? 0) * x.AverageSamples) / averageSamples;
                result.Add(new AdminTrendPointDto(month, matching.Sum(x => x.Count), matching.Sum(x => x.Amount),
                    average, averageSamples));
            }
            return Task.FromResult<IReadOnlyList<AdminTrendPointDto>>(result);
        }
        var map = rows.ToDictionary(x => x.Date.Date);
        for (var day = fillFrom; day < range.ToExclusive; day = day.AddDays(1))
            result.Add(map.GetValueOrDefault(day, new AdminTrendPointDto(day, 0)));
        return Task.FromResult<IReadOnlyList<AdminTrendPointDto>>(result);
    }

    private static AdminComparisonDto Compare(decimal current, decimal? previous) => previous.HasValue
        ? AdminAnalyticsMath.Compare(current, previous.Value)
        : new AdminComparisonDto(current, 0, null);

    private sealed record AnalyticsRange(DateTime From, DateTime ToExclusive, bool AllHistory = false)
    {
        public int Days => (int)(ToExclusive - From).TotalDays;
        public AdminDateRangeDto Dto => new(From, ToExclusive.AddDays(-1), Days);
        public AnalyticsRange Previous() => new(From.AddDays(-Days), From);
    }
}
