using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;

namespace STO123.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN_QUAN_LY,ADMIN_NOI_DUNG")]
[Route("api/admin/preview")]
public sealed class AdminReadOnlyPreviewController(ToeicDbContext db,
    AdminLectureMediaTicket tickets) : ControllerBase
{
    private bool ContentAdmin => User.IsInRole("ADMIN_NOI_DUNG");

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct)
    {
        var query = db.KhoaHoc.AsNoTracking().AsQueryable();
        if (!ContentAdmin) query = query.Where(x => x.TrangThai == "DANG_MO");
        return Ok(await query.OrderBy(x => x.GiaiDoan).ThenBy(x => x.MaKhoaHoc)
            .Select(x => new { id = x.MaKhoaHoc, name = x.TenKhoaHoc,
                description = x.MoTa, status = x.TrangThai }).ToListAsync(ct));
    }

    [HttpGet("courses/{courseId:int}")]
    public async Task<IActionResult> Course(int courseId, CancellationToken ct)
    {
        var course = await db.KhoaHoc.AsNoTracking().Where(x => x.MaKhoaHoc == courseId &&
            (ContentAdmin || x.TrangThai == "DANG_MO"))
            .Select(x => new { id = x.MaKhoaHoc, name = x.TenKhoaHoc,
                description = x.MoTa, status = x.TrangThai }).SingleOrDefaultAsync(ct);
        if (course is null) return NotFound();
        var units = await db.UnitKhoaHoc.AsNoTracking().Where(x => x.MaKhoaHoc == courseId && !x.IsArchived)
            .OrderBy(x => x.ThuTu).Select(x => new { id = x.MaUnit, name = x.TenUnit,
                lessons = x.LessonKhoaHoc.Where(l => !l.IsArchived).OrderBy(l => l.ThuTu)
                    .Select(l => new { id = l.MaLesson, name = l.TenLesson,
                        steps = l.BuocLoTrinh.Where(s => !s.IsArchived &&
                            (s.MaBaiGiang != null || s.MaChuDe != null || s.MaBaiTap != null) && (ContentAdmin ||
                            s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO" ||
                            s.MaChuDe != null && s.MaChuDeNavigation.TrangThai == "DANG_MO" ||
                            s.MaBaiTap != null && db.PhienBanBaiTapKhoaHoc.Any(v =>
                                v.MaBaiTap == s.MaBaiTap && v.TrangThai == "XUAT_BAN")))
                            .OrderBy(s => s.ThuTu)
                            .Select(s => new { id = s.MaBuoc, title = s.TieuDe,
                                type = s.MaBaiGiang != null ? "LECTURE" :
                                    s.MaChuDe != null ? "VOCABULARY" :
                                    s.MaBaiTap != null ? "EXERCISE" : "EXAM" }).ToList() }).ToList() })
            .ToListAsync(ct);
        return Ok(new { course, units });
    }

    [HttpGet("courses/{courseId:int}/steps/{stepId:int}")]
    public async Task<IActionResult> Step(int courseId, int stepId, CancellationToken ct)
    {
        var step = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == stepId &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            (ContentAdmin || s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO"))
            .Select(s => new { title = s.TieuDe, unit = s.MaLessonNavigation.MaUnitNavigation.TenUnit,
                lesson = s.MaLessonNavigation.TenLesson, s.MaBaiGiang, s.MaChuDe, s.MaBaiTap })
            .SingleOrDefaultAsync(ct);
        if (step is null) return NotFound();
        if (step.MaBaiGiang is { } lectureId)
        {
            var lecture = await db.BaiGiang.AsNoTracking().Where(l => l.MaBaiGiang == lectureId &&
                (ContentAdmin || l.TrangThai == "DANG_MO"))
                .Select(l => new { title = l.TieuDe, summary = l.NoiDungTomTat,
                    content = l.NoiDung, resources = l.TaiNguyenBaiGiang.Where(r => !r.IsArchived)
                        .OrderBy(r => r.ThuTu).Select(r => new { id = r.MaTaiNguyen,
                            name = r.TenTaiNguyen, kind = r.LoaiTaiNguyen }).ToList() })
                .SingleOrDefaultAsync(ct);
            return lecture is null ? NotFound() : Ok(new { step.title, step.unit, step.lesson,
                type = "LECTURE", lecture });
        }
        if (step.MaChuDe is { } topicId)
        {
            var topic = await db.ChuDe.AsNoTracking().Where(t => t.MaChuDe == topicId &&
                (ContentAdmin || t.TrangThai == "DANG_MO"))
                .Select(t => new { title = t.TenChuDe, description = t.MoTa,
                    words = t.TuVungChuDe.OrderBy(w => w.MaTuVung).Select(w => new {
                        word = w.MaTuVungNavigation.TuVung1, meaning = w.MaTuVungNavigation.Nghia,
                        example = w.MaTuVungNavigation.ViDu }).ToList() }).SingleOrDefaultAsync(ct);
            return topic is null ? NotFound() : Ok(new { step.title, step.unit, step.lesson,
                type = "VOCABULARY", topic });
        }
        if (step.MaBaiTap is { } exerciseId)
        {
            var versions = db.PhienBanBaiTapKhoaHoc.AsNoTracking().Where(v => v.MaBaiTap == exerciseId);
            if (!ContentAdmin) versions = versions.Where(v => v.TrangThai == "XUAT_BAN");
            var versionId = await versions.OrderByDescending(v => v.SoPhienBan)
                .Select(v => (int?)v.MaPhienBan).FirstOrDefaultAsync(ct);
            if (versionId is null) return NotFound();
            var questions = await db.CauHoiBaiTapKhoaHoc.AsNoTracking()
                .Where(q => q.MaPhienBan == versionId).OrderBy(q => q.ThuTu)
                .Select(q => new { text = q.NoiDung, passage = q.NguLieu,
                    a = q.PhuongAnA, b = q.PhuongAnB, c = q.PhuongAnC, d = q.PhuongAnD,
                    correct = q.PhuongAnDung, explanation = q.GiaiThich }).ToListAsync(ct);
            return Ok(new { step.title, step.unit, step.lesson, type = "EXERCISE", questions });
        }
        return NotFound();
    }

    [HttpPost("courses/{courseId:int}/steps/{stepId:int}/resources/{resourceId:int}/ticket")]
    public async Task<IActionResult> ResourceTicket(int courseId, int stepId, int resourceId, CancellationToken ct)
    {
        var resource = await db.TaiNguyenBaiGiang.AsNoTracking().Where(r => r.MaTaiNguyen == resourceId &&
            !r.IsArchived && r.MaBaiGiangNavigation.BuocLoTrinh.Any(s => s.MaBuoc == stepId &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
                !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                (ContentAdmin || s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO")) &&
            (ContentAdmin || r.MaBaiGiangNavigation.TrangThai == "DANG_MO"))
            .Select(r => new { r.MaBaiGiang }).SingleOrDefaultAsync(ct);
        if (resource is null) return NotFound();
        var adminId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (adminId <= 0) return Unauthorized();
        var ticket = tickets.Issue(adminId, resource.MaBaiGiang, resourceId);
        return Ok(new { url = $"/api/admin/content/lectures/{resource.MaBaiGiang}/resources/{resourceId}/preview-stream?ticket={ticket}" });
    }
}
