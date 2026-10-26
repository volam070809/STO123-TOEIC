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
[Route("api/admin/learners")]
public sealed class AdminLearnersController(ToeicDbContext db) : ControllerBase
{
    private static readonly HashSet<string> SupportedStatuses =
        ["CHO_XAC_THUC", "HOAT_DONG", "BI_KHOA"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AdminLearnerListQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", message = "Trang hoặc kích thước trang không hợp lệ." });

        var search = request.Search?.Trim();
        if (search?.Length > 100)
            return BadRequest(new { code = "INVALID_SEARCH", message = "Từ khóa tìm kiếm tối đa 100 ký tự." });

        var status = request.Status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(status) && !SupportedStatuses.Contains(status))
            return BadRequest(new { code = "INVALID_STATUS", message = "Trạng thái học viên không hợp lệ." });
        var dateError = AdminDateRangeValidation.Error(request.DateFrom, request.DateTo);
        if (dateError is not null)
            return BadRequest(new { code = "INVALID_DATE_RANGE", message = dateError });

        var query = db.NguoiDung.AsNoTracking().Where(user => user.VaiTro == "HOC_VIEN");
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(user => user.HoTen.Contains(search) || user.Email.Contains(search) ||
                (user.SoDienThoai != null && user.SoDienThoai.Contains(search)));
        }
        if (!string.IsNullOrEmpty(status)) query = query.Where(user => user.TrangThai == status);
        if (request.DateFrom.HasValue) query = query.Where(user => user.NgayTao >= request.DateFrom.Value.Date);
        if (request.DateTo.HasValue)
        {
            var exclusiveEnd = AdminDateRangeValidation.ExclusiveEnd(request.DateTo.Value);
            query = query.Where(user => user.NgayTao < exclusiveEnd);
        }

        var totalItems = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(user => user.NgayTao)
            .ThenByDescending(user => user.MaNguoiDung)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(user => new AdminLearnerListItemDto(
                user.MaNguoiDung,
                user.HoTen,
                user.Email,
                user.SoDienThoai,
                user.TrangThai,
                user.NgayTao,
                user.LanDangNhapCuoi,
                user.DangKyGoiHoc.Count,
                user.DangKyGoiHoc.Count(enrollment => enrollment.TrangThai == "DANG_SU_DUNG"),
                user.KetQuaLamBai.Count,
                user.KetQuaLamBai.OrderByDescending(attempt => attempt.NgayLamBai)
                    .Select(attempt => (DateTime?)attempt.NgayLamBai).FirstOrDefault()))
            .ToListAsync(ct);

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)request.PageSize);
        return Ok(new AdminPagedLearnersDto(items, request.Page, request.PageSize, totalItems, totalPages));
    }

    [HttpGet("{learnerId:int}")]
    public async Task<IActionResult> Detail(int learnerId, CancellationToken ct)
    {
        if (learnerId <= 0) return NotFound(new { code = "LEARNER_NOT_FOUND" });

        var learner = await db.NguoiDung.AsNoTracking()
            .Where(user => user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN")
            .Select(user => new
            {
                user.MaNguoiDung,
                user.HoTen,
                user.Email,
                user.SoDienThoai,
                user.TrangThai,
                user.NgayTao,
                user.LanDangNhapCuoi
            })
            .SingleOrDefaultAsync(ct);
        if (learner is null) return NotFound(new { code = "LEARNER_NOT_FOUND", message = "Không tìm thấy học viên." });

        var enrollmentRows = await db.DangKyGoiHoc.AsNoTracking()
            .Where(enrollment => enrollment.MaNguoiDung == learnerId)
            .OrderByDescending(enrollment => enrollment.NgayDangKy)
            .ThenByDescending(enrollment => enrollment.MaDangKy)
            .Include(enrollment => enrollment.MaGoiHocNavigation)
                .ThenInclude(package => package.MaKhoaHoc)
            .Include(enrollment => enrollment.ChiTietDangKyKhoaHoc)
                .ThenInclude(detail => detail.MaKhoaHocNavigation)
            .Include(enrollment => enrollment.GiaoDichThanhToan)
            .AsSplitQuery()
            .ToListAsync(ct);

        var enrollments = enrollmentRows.Select(enrollment =>
        {
            var courses = (enrollment.MaGoiHocNavigation?.MaKhoaHoc
                    .Select(course => new AdminLearnerCourseDto(course.MaKhoaHoc, course.TenKhoaHoc,
                        course.GiaiDoan, course.TrangThai)) ?? [])
                .Concat(enrollment.ChiTietDangKyKhoaHoc.Select(detail => new AdminLearnerCourseDto(
                    detail.MaKhoaHocNavigation.MaKhoaHoc,
                    detail.MaKhoaHocNavigation.TenKhoaHoc,
                    detail.MaKhoaHocNavigation.GiaiDoan,
                    detail.MaKhoaHocNavigation.TrangThai)))
                .DistinctBy(course => course.CourseId)
                .OrderBy(course => course.Stage)
                .ThenBy(course => course.CourseId)
                .ToList();
            return new AdminLearnerEnrollmentDto(
                enrollment.MaDangKy,
                enrollment.MaGoiHoc,
                enrollment.MaGoiHocNavigation?.TenGoiHoc,
                enrollment.GiaDangKy,
                enrollment.SoNgaySuDung,
                enrollment.NgayDangKy,
                enrollment.NgayBatDau,
                enrollment.NgayKetThuc,
                enrollment.TrangThai,
                courses,
                enrollment.GiaoDichThanhToan.Count,
                enrollment.GiaoDichThanhToan.Where(payment => payment.TrangThai == "THANH_CONG")
                    .Sum(payment => payment.SoTien));
        }).ToList();

        var attempts = await db.KetQuaLamBai.AsNoTracking()
            .Where(attempt => attempt.MaHocVien == learnerId)
            .OrderByDescending(attempt => attempt.NgayLamBai)
            .ThenByDescending(attempt => attempt.MaKetQua)
            .Take(10)
            .Select(attempt => new AdminLearnerAttemptDto(
                attempt.MaKetQua,
                attempt.LoaiBaiLam,
                attempt.TrangThai,
                attempt.MaDeThiNavigation == null ? null : attempt.MaDeThiNavigation.TenDe,
                attempt.DiemNghe,
                attempt.DiemDoc,
                attempt.DiemTong,
                attempt.NgayLamBai,
                attempt.NgayNopBai))
            .ToListAsync(ct);

        var attemptSummary = await db.KetQuaLamBai.AsNoTracking()
            .Where(attempt => attempt.MaHocVien == learnerId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Completed = group.Count(attempt => attempt.NgayNopBai != null),
                LastLearningAt = group.Max(attempt => (DateTime?)attempt.NgayLamBai)
            })
            .SingleOrDefaultAsync(ct);

        var progress = await db.TienDoBuocLoTrinh.AsNoTracking()
            .Where(step => step.MaNguoiDung == learnerId)
            .Select(step => new { step.TrangThai, step.TyLeHoanThanh, step.CapNhatLuc })
            .ToListAsync(ct);
        var percentages = progress.Where(step => step.TyLeHoanThanh.HasValue)
            .Select(step => (double)step.TyLeHoanThanh!.Value).ToList();
        var lastProgressAt = progress.Count == 0 ? (DateTime?)null : progress.Max(step => step.CapNhatLuc);
        var lastLearningAt = attemptSummary?.LastLearningAt;
        if (lastProgressAt.HasValue && (!lastLearningAt.HasValue || lastProgressAt > lastLearningAt))
            lastLearningAt = lastProgressAt;
        var learning = new AdminLearnerLearningSummaryDto(
            attemptSummary?.Total ?? 0,
            attemptSummary?.Completed ?? 0,
            progress.Count,
            progress.Count(step => step.TrangThai == "HOAN_THANH" || step.TyLeHoanThanh == 100),
            percentages.Count == 0 ? null : Math.Round(percentages.Average(), 1),
            lastLearningAt,
            attempts);

        return Ok(new AdminLearnerDetailDto(
            learner.MaNguoiDung,
            learner.HoTen,
            learner.Email,
            learner.SoDienThoai,
            learner.TrangThai,
            learner.NgayTao,
            learner.LanDangNhapCuoi,
            enrollments,
            learning));
    }
}
