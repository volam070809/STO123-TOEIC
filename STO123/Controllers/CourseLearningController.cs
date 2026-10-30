using System.Security.Claims;
using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;
using STO123.Services.Exam;

namespace STO123.Controllers;

public sealed record ExerciseCheckRequest(int QuestionId, string Choice, Guid RequestKey);
public sealed record StartExerciseRequest(long? RestartFromAttemptId);

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/learning/courses/{courseId:int}/steps")]
public sealed class CourseLearningController(ToeicDbContext db, LectureResourceStorage storage,
    LectureMediaTicket tickets, TimeProvider clock) : ControllerBase
{
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private Task<bool> HasAccess(int courseId, CancellationToken ct) => HasAccess(courseId, LearnerId, ct);
    private async Task<bool> HasAccess(int courseId, int learnerId, CancellationToken ct)
    {
        if (learnerId <= 0) return false;
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        return await CourseEntitlement.ActiveCourseIds(db, learnerId, now).AnyAsync(id => id == courseId, ct);
    }

    [HttpGet("{stepId:int}")]
    public async Task<IActionResult> Detail(int courseId, int stepId, CancellationToken ct)
    {
        if (!await HasAccess(courseId, ct)) return Forbid();
        var step = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaBuoc == stepId && !x.IsArchived &&
            !x.MaLessonNavigation.IsArchived && !x.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO")
            .Select(x => new { x.MaBuoc, x.TieuDe, x.MaBaiGiang, x.MaChuDe, x.MaDeThi, x.MaBaiTap,
                lessonId = x.MaLesson, unitId = x.MaLessonNavigation.MaUnit,
                lessonName = x.MaLessonNavigation.TenLesson, unitName = x.MaLessonNavigation.MaUnitNavigation.TenUnit })
            .FirstOrDefaultAsync(ct);
        if (step is null) return NotFound();
        if (step.MaBaiGiang is { } lectureId)
        {
            var lecture = await db.BaiGiang.AsNoTracking().Where(x => x.MaBaiGiang == lectureId &&
                x.TrangThai == "DANG_MO" && (x.MaDanhMuc == null || x.MaDanhMucNavigation!.TrangThai == "DANG_MO"))
                .Select(x => new { title = x.TieuDe, summary = x.NoiDungTomTat, content = x.NoiDung,
                    resources = x.TaiNguyenBaiGiang.Where(r => !r.IsArchived).OrderBy(r => r.ThuTu).Select(r => new {
                        id = r.MaTaiNguyen, name = r.TenTaiNguyen, kind = r.LoaiTaiNguyen
                    }).ToList() }).FirstOrDefaultAsync(ct);
            return lecture is null ? NotFound() : Ok(new { id = step.MaBuoc, title = step.TieuDe,
                step.unitId, step.lessonId, step.unitName, step.lessonName, type = "LECTURE", lecture });
        }
        if (step.MaChuDe is { } topicId)
        {
            var topic = await db.ChuDe.AsNoTracking().Where(x => x.MaChuDe == topicId && x.TrangThai == "DANG_MO")
                .Select(x => new { title = x.TenChuDe, description = x.MoTa,
                    words = x.TuVungChuDe.OrderBy(w => w.MaTuVung).Select(w => new {
                        word = w.MaTuVungNavigation.TuVung1, meaning = w.MaTuVungNavigation.Nghia,
                        example = w.MaTuVungNavigation.ViDu
                    }).ToList() }).FirstOrDefaultAsync(ct);
            return topic is null ? NotFound() : Ok(new { id = step.MaBuoc, title = step.TieuDe,
                step.unitId, step.lessonId, step.unitName, step.lessonName, type = "VOCABULARY", topic });
        }
        if (step.MaBaiTap is { } exerciseId)
        {
            var version = await db.PhienBanBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaBaiTap == exerciseId && x.TrangThai == "XUAT_BAN")
                .OrderByDescending(x => x.SoPhienBan)
                .Select(x => new { x.MaPhienBan, x.SoPhienBan }).FirstOrDefaultAsync(ct);
            if (version is null) return NotFound();
            return Ok(new { id = step.MaBuoc, title = step.TieuDe, step.unitId, step.lessonId, step.unitName, step.lessonName,
                type = "EXERCISE", exercise = new { exerciseId, versionId = version.MaPhienBan, version = version.SoPhienBan } });
        }
        return Conflict(new { code = "ACTIVITY_NOT_SUPPORTED", message = "Hoạt động này chưa có trình xem hợp lệ." });
    }

    private async Task<int> ActiveEnrollment(int courseId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        return await db.DangKyGoiHoc.AsNoTracking().Where(e => e.MaNguoiDung == LearnerId &&
            e.TrangThai == "DANG_SU_DUNG" && e.NgayBatDau <= now && e.NgayKetThuc > now &&
            e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG") &&
            (e.MaGoiHoc != null && e.MaGoiHocNavigation.MaKhoaHoc.Any(c => c.MaKhoaHoc == courseId) ||
             e.MaGoiHoc == null && e.ChiTietDangKyKhoaHoc.Any(c => c.MaKhoaHoc == courseId)))
            .OrderBy(e => e.MaDangKy).Select(e => e.MaDangKy).FirstOrDefaultAsync(ct);
    }

    private async Task<IActionResult?> ProgressOwnershipConflict(int enrollment, int stepId, CancellationToken ct)
    {
        var owner = db.Database.IsSqlServer()
            ? await db.Database.SqlQuery<int>($"""
                SELECT MaNguoiDung AS Value FROM dbo.TienDoBuocLoTrinh WITH (UPDLOCK, HOLDLOCK)
                WHERE MaDangKy = {enrollment} AND MaBuoc = {stepId}
                """).FirstOrDefaultAsync(ct)
            : await db.TienDoBuocLoTrinh.AsNoTracking()
                .Where(x => x.MaDangKy == enrollment && x.MaBuoc == stepId)
                .Select(x => x.MaNguoiDung).FirstOrDefaultAsync(ct);
        return owner != 0 && owner != LearnerId
            ? Conflict(new { code = "PROGRESS_OWNER_MISMATCH" })
            : null;
    }

    [HttpGet("{stepId:int}/exercise")]
    public async Task<IActionResult> Exercise(int courseId, int stepId, CancellationToken ct)
    {
        var enrollment = await ActiveEnrollment(courseId, ct);
        if (enrollment == 0) return Forbid();
        var exerciseId = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == stepId && !s.IsArchived &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId).Select(s => s.MaBaiTap).FirstOrDefaultAsync(ct);
        if (exerciseId is null) return NotFound();
        var attempt = await db.LuotBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaDangKy == enrollment && x.MaBuoc == stepId)
            .OrderByDescending(x => x.LanLam).FirstOrDefaultAsync(ct);
        var versionId = attempt?.MaPhienBan ?? await db.PhienBanBaiTapKhoaHoc.AsNoTracking()
            .Where(x => x.MaBaiTap == exerciseId && x.TrangThai == "XUAT_BAN").OrderByDescending(x => x.SoPhienBan)
            .Select(x => x.MaPhienBan).FirstOrDefaultAsync(ct);
        if (versionId == 0) return NotFound();
        var questions = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaBaiTap == exerciseId && x.MaPhienBan == versionId)
            .OrderBy(x => x.ThuTu).Select(x => new { id = x.MaCauHoi, order = x.ThuTu, content = x.NoiDung,
                passage = x.NguLieu, a = x.PhuongAnA, b = x.PhuongAnB, c = x.PhuongAnC, d = x.PhuongAnD }).ToListAsync(ct);
        var checks = attempt is null ? [] : await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaLuot == attempt.MaLuot)
            .OrderBy(x => x.MaLan)
            .Select(x => new { questionId = x.MaCauHoi, x.LanKiemTra, choice = x.PhuongAnChon, correct = x.Dung }).ToListAsync(ct);
        return Ok(new { attemptId = attempt?.MaLuot, completed = attempt?.TrangThai == "HOAN_THANH",
            versionId, questions, checks });
    }

    [HttpGet("{stepId:int}/exercise/attempts")]
    public async Task<IActionResult> ExerciseHistory(int courseId, int stepId, CancellationToken ct)
    {
        var enrollment = await ActiveEnrollment(courseId, ct); if (enrollment == 0) return Forbid();
        if (!await db.BuocLoTrinh.AnyAsync(s => s.MaBuoc == stepId && s.MaBaiTap != null &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId, ct)) return NotFound();
        return Ok(await db.LuotBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaDangKy == enrollment && x.MaBuoc == stepId)
            .OrderByDescending(x => x.LanLam).Select(x => new { id = x.MaLuot, number = x.LanLam,
                versionId = x.MaPhienBan, status = x.TrangThai, startedAt = x.BatDauLuc,
                completedAt = x.HoanThanhLuc }).ToListAsync(ct));
    }

    [HttpPost("{stepId:int}/exercise/attempts")]
    public async Task<IActionResult> StartExercise(int courseId, int stepId, StartExerciseRequest? request, CancellationToken ct)
    {
        var enrollment = await ActiveEnrollment(courseId, ct);
        if (enrollment == 0) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await ProgressOwnershipConflict(enrollment, stepId, ct) is { } ownershipConflict) return ownershipConflict;
        var exerciseId = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == stepId && !s.IsArchived &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId).Select(s => s.MaBaiTap).FirstOrDefaultAsync(ct);
        if (exerciseId is null) return NotFound();
        var latest = await db.LuotBaiTapKhoaHoc.Where(x => x.MaDangKy == enrollment && x.MaBuoc == stepId)
            .OrderByDescending(x => x.LanLam).FirstOrDefaultAsync(ct);
        if (latest?.TrangThai == "DANG_LAM") return Ok(new { id = latest.MaLuot });
        if (latest is not null && request?.RestartFromAttemptId != latest.MaLuot)
            return Ok(new { id = latest.MaLuot, completed = true });
        var versionId = await db.PhienBanBaiTapKhoaHoc.Where(x => x.MaBaiTap == exerciseId && x.TrangThai == "XUAT_BAN")
            .OrderByDescending(x => x.SoPhienBan)
            .Select(x => x.MaPhienBan).FirstOrDefaultAsync(ct);
        if (versionId == 0) return NotFound();
        var row = new LuotBaiTapKhoaHoc { MaDangKy = enrollment, MaBuoc = stepId,
            MaBaiTap = exerciseId.Value, MaPhienBan = versionId, LanLam = (latest?.LanLam ?? 0) + 1 };
        db.LuotBaiTapKhoaHoc.Add(row);
        try {
            await db.SaveChangesAsync(ct);
            if (db.Database.IsSqlServer()) {
                var localNow = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
                await db.Database.ExecuteSqlInterpolatedAsync($@"IF EXISTS(SELECT 1 FROM dbo.TienDoBuocLoTrinh WITH(UPDLOCK,HOLDLOCK) WHERE MaDangKy={enrollment} AND MaBuoc={stepId})
                    UPDATE dbo.TienDoBuocLoTrinh SET TrangThai=CASE WHEN TrangThai='HOAN_THANH' THEN TrangThai ELSE 'DANG_LAM' END,
                    CapNhatLuc={localNow} WHERE MaDangKy={enrollment} AND MaBuoc={stepId} AND MaNguoiDung={LearnerId};
                    ELSE INSERT dbo.TienDoBuocLoTrinh(MaNguoiDung,MaDangKy,MaBuoc,TrangThai,TyLeHoanThanh,CapNhatLuc)
                    VALUES({LearnerId},{enrollment},{stepId},'DANG_LAM',0,{localNow});", ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { code = "ATTEMPT_CONCURRENT", message = "Yêu cầu đồng thời; tải lại bài tập rồi thử lại." });
        }
        return Created($"/api/learning/courses/{courseId}/steps/{stepId}/exercise", new { id = row.MaLuot });
    }

    [HttpPost("{stepId:int}/exercise/attempts/{attemptId:long}/check")]
    public async Task<IActionResult> CheckExercise(int courseId, int stepId, long attemptId, ExerciseCheckRequest request, CancellationToken ct)
    {
        if (request.RequestKey == Guid.Empty || request.Choice is not ("A" or "B" or "C" or "D")) return BadRequest();
        var enrollment = await ActiveEnrollment(courseId, ct);
        if (enrollment == 0) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await ProgressOwnershipConflict(enrollment, stepId, ct) is { } ownershipConflict) return ownershipConflict;
        var attempt = await db.LuotBaiTapKhoaHoc.FirstOrDefaultAsync(x => x.MaLuot == attemptId &&
            x.MaDangKy == enrollment && x.MaBuoc == stepId &&
            db.BuocLoTrinh.Any(s => s.MaBuoc == stepId && s.MaBaiTap == x.MaBaiTap &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId), ct);
        if (attempt is null) return NotFound();
        var previous = await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking().FirstOrDefaultAsync(x => x.MaLuot == attemptId && x.KhoaYeuCau == request.RequestKey, ct);
        if (previous is not null)
        {
            if (previous.MaCauHoi != request.QuestionId || previous.PhuongAnChon != request.Choice)
                return Conflict(new { code = "REQUEST_KEY_REUSED" });
            var savedCount = await db.LanKiemTraBaiTapKhoaHoc.Where(x => x.MaLuot == attemptId)
                .Select(x => x.MaCauHoi).Distinct().CountAsync(ct);
            var savedTotal = await db.CauHoiBaiTapKhoaHoc.CountAsync(x => x.MaPhienBan == attempt.MaPhienBan, ct);
            return Ok(new { correct = previous.Dung, choice = previous.PhuongAnChon,
                checkedCount = savedCount, total = savedTotal, completed = attempt.TrangThai == "HOAN_THANH" });
        }
        var question = await db.CauHoiBaiTapKhoaHoc.AsNoTracking().FirstOrDefaultAsync(x => x.MaCauHoi == request.QuestionId &&
            x.MaBaiTap == attempt.MaBaiTap && x.MaPhienBan == attempt.MaPhienBan, ct);
        if (question is null) return NotFound();
        if (attempt.TrangThai != "DANG_LAM") return Conflict(new { code = "ATTEMPT_COMPLETED" });
        var checkCount = await db.LanKiemTraBaiTapKhoaHoc.CountAsync(x => x.MaLuot == attemptId && x.MaCauHoi == request.QuestionId, ct);
        var correct = request.Choice == question.PhuongAnDung;
        db.LanKiemTraBaiTapKhoaHoc.Add(new LanKiemTraBaiTapKhoaHoc { MaLuot = attemptId, MaBaiTap = attempt.MaBaiTap,
            MaPhienBan = attempt.MaPhienBan, MaCauHoi = request.QuestionId, LanKiemTra = checkCount + 1,
            KhoaYeuCau = request.RequestKey, PhuongAnChon = request.Choice, PhuongAnDungLucKiemTra = question.PhuongAnDung,
            Dung = correct, GiaiThichLucKiemTra = question.GiaiThich });
        await db.SaveChangesAsync(ct);
        var total = await db.CauHoiBaiTapKhoaHoc.CountAsync(q => q.MaPhienBan == attempt.MaPhienBan, ct);
        var checkedQuestions = await db.LanKiemTraBaiTapKhoaHoc.Where(x => x.MaLuot == attemptId)
            .Select(x => x.MaCauHoi).Distinct().CountAsync(ct);
        if (total > 0 && checkedQuestions == total)
        {
            attempt.TrangThai = "HOAN_THANH";
            attempt.HoanThanhLuc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"IF EXISTS(SELECT 1 FROM dbo.TienDoBuocLoTrinh WITH(UPDLOCK,HOLDLOCK) WHERE MaDangKy={enrollment} AND MaBuoc={stepId})
                    UPDATE dbo.TienDoBuocLoTrinh SET TrangThai='HOAN_THANH',TyLeHoanThanh=100,CapNhatLuc={now} WHERE MaDangKy={enrollment} AND MaBuoc={stepId} AND MaNguoiDung={LearnerId};
                    ELSE INSERT dbo.TienDoBuocLoTrinh(MaNguoiDung,MaDangKy,MaBuoc,TrangThai,TyLeHoanThanh,CapNhatLuc)
                    VALUES({LearnerId},{enrollment},{stepId},'HOAN_THANH',100,{now});", ct);
            }
        }
        await tx.CommitAsync(ct);
        return Ok(new { correct, choice = request.Choice, checkedCount = checkedQuestions, total, completed = total > 0 && checkedQuestions == total });
    }

    [HttpGet("{stepId:int}/exercise/attempts/{attemptId:long}/questions/{questionId:int}/answer")]
    public async Task<IActionResult> ExerciseAnswer(int courseId, int stepId, long attemptId, int questionId, CancellationToken ct)
    {
        var enrollment = await ActiveEnrollment(courseId, ct); if (enrollment == 0) return Forbid();
        var answer = await db.LanKiemTraBaiTapKhoaHoc.AsNoTracking().Where(x => x.MaLuot == attemptId && x.MaCauHoi == questionId &&
            db.LuotBaiTapKhoaHoc.Any(a => a.MaLuot == x.MaLuot && a.MaDangKy == enrollment && a.MaBuoc == stepId &&
                db.BuocLoTrinh.Any(s => s.MaBuoc == stepId && s.MaBaiTap == a.MaBaiTap && s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)))
            .OrderByDescending(x => x.LanKiemTra).Select(x => new { correctChoice = x.PhuongAnDungLucKiemTra,
                explanation = x.GiaiThichLucKiemTra }).FirstOrDefaultAsync(ct);
        return answer is null ? NotFound() : Ok(answer);
    }

    [HttpPost("{stepId:int}/exercise/attempts/{attemptId:long}/finish")]
    public async Task<IActionResult> FinishExercise(int courseId, int stepId, long attemptId, CancellationToken ct)
    {
        var enrollment = await ActiveEnrollment(courseId, ct); if (enrollment == 0) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await ProgressOwnershipConflict(enrollment, stepId, ct) is { } ownershipConflict) return ownershipConflict;
        var attempt = await db.LuotBaiTapKhoaHoc.FirstOrDefaultAsync(x => x.MaLuot == attemptId && x.MaDangKy == enrollment &&
            x.MaBuoc == stepId && db.BuocLoTrinh.Any(s => s.MaBuoc == stepId && s.MaBaiTap == x.MaBaiTap &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId), ct);
        if (attempt is null) return NotFound();
        var count = await db.CauHoiBaiTapKhoaHoc.CountAsync(q => q.MaBaiTap == attempt.MaBaiTap && q.MaPhienBan == attempt.MaPhienBan, ct);
        var checkedCount = await db.LanKiemTraBaiTapKhoaHoc.Where(x => x.MaLuot == attemptId).Select(x => x.MaCauHoi).Distinct().CountAsync(ct);
        if (count == 0 || checkedCount != count) return Conflict(new { code = "QUESTIONS_INCOMPLETE" });
        if (attempt.TrangThai != "HOAN_THANH") { attempt.TrangThai = "HOAN_THANH"; attempt.HoanThanhLuc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($@"IF EXISTS(SELECT 1 FROM dbo.TienDoBuocLoTrinh WITH(UPDLOCK,HOLDLOCK) WHERE MaDangKy={enrollment} AND MaBuoc={stepId})
            UPDATE dbo.TienDoBuocLoTrinh SET TrangThai='HOAN_THANH',TyLeHoanThanh=100,CapNhatLuc={now} WHERE MaDangKy={enrollment} AND MaBuoc={stepId} AND MaNguoiDung={LearnerId};
            ELSE INSERT dbo.TienDoBuocLoTrinh(MaNguoiDung,MaDangKy,MaBuoc,TrangThai,TyLeHoanThanh,CapNhatLuc)
            VALUES({LearnerId},{enrollment},{stepId},'HOAN_THANH',100,{now});", ct);
        await tx.CommitAsync(ct); return Ok(new { status = "HOAN_THANH" });
    }

    [HttpPut("{stepId:int}/completion")]
    public async Task<IActionResult> Complete(int courseId, int stepId, CancellationToken ct)
    {
        if (LearnerId <= 0 || !await HasAccess(courseId, ct)) return Forbid();
        var step = await db.BuocLoTrinh.AsNoTracking().Where(x => x.MaBuoc == stepId && !x.IsArchived &&
            !x.MaLessonNavigation.IsArchived && !x.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId &&
            x.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO")
            .Select(x => new { x.MaBaiGiang, x.MaChuDe }).FirstOrDefaultAsync(ct);
        if (step is null) return NotFound();
        if (step.MaBaiGiang is null && step.MaChuDe is null)
            return Conflict(new { code = "ACTIVITY_NOT_SUPPORTED", message = "Hoạt động này chưa hỗ trợ hoàn thành trực tiếp." });
        if (step.MaBaiGiang is { } lectureId && !await db.BaiGiang.AsNoTracking().AnyAsync(x =>
            x.MaBaiGiang == lectureId && x.TrangThai == "DANG_MO" &&
            (x.MaDanhMuc == null || x.MaDanhMucNavigation!.TrangThai == "DANG_MO"), ct)) return NotFound();
        if (step.MaChuDe is { } topicId && !await db.ChuDe.AsNoTracking().AnyAsync(x =>
            x.MaChuDe == topicId && x.TrangThai == "DANG_MO", ct)) return NotFound();
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var enrollment = await db.DangKyGoiHoc.AsNoTracking().Where(e => e.MaNguoiDung == LearnerId &&
            e.TrangThai == "DANG_SU_DUNG" && e.NgayBatDau <= now && e.NgayKetThuc > now &&
            e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG") &&
            (e.MaGoiHoc != null && e.MaGoiHocNavigation.MaKhoaHoc.Any(c => c.MaKhoaHoc == courseId) ||
             e.MaGoiHoc == null && e.ChiTietDangKyKhoaHoc.Any(c => c.MaKhoaHoc == courseId)))
            .OrderBy(e => e.MaDangKy).Select(e => e.MaDangKy).FirstOrDefaultAsync(ct);
        if (enrollment == 0) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await ProgressOwnershipConflict(enrollment, stepId, ct) is { } ownershipConflict) return ownershipConflict;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            IF EXISTS (SELECT 1 FROM dbo.TienDoBuocLoTrinh WITH (UPDLOCK, HOLDLOCK)
                       WHERE MaDangKy = {enrollment} AND MaBuoc = {stepId})
                UPDATE dbo.TienDoBuocLoTrinh SET TrangThai = 'HOAN_THANH', TyLeHoanThanh = 100,
                    CapNhatLuc = {now} WHERE MaDangKy = {enrollment} AND MaBuoc = {stepId} AND MaNguoiDung = {LearnerId}
                    AND (TrangThai <> 'HOAN_THANH' OR TyLeHoanThanh IS NULL OR TyLeHoanThanh <> 100);
            ELSE
                INSERT dbo.TienDoBuocLoTrinh (MaNguoiDung, MaDangKy, MaBuoc, TrangThai, TyLeHoanThanh, CapNhatLuc)
                VALUES ({LearnerId}, {enrollment}, {stepId}, 'HOAN_THANH', 100, {now});", ct);
        await tx.CommitAsync(ct);
        return Ok(new { stepId, status = "HOAN_THANH", progress = 100 });
    }

    private sealed record ResourceInfo(string Path, string Name);
    private Task<ResourceInfo?> FindResource(int courseId, int stepId, int resourceId, CancellationToken ct) =>
        db.TaiNguyenBaiGiang.AsNoTracking().Where(r => r.MaTaiNguyen == resourceId && !r.IsArchived &&
            r.MaBaiGiangNavigation.TrangThai == "DANG_MO" &&
            (r.MaBaiGiangNavigation.MaDanhMuc == null || r.MaBaiGiangNavigation.MaDanhMucNavigation!.TrangThai == "DANG_MO") &&
            r.MaBaiGiangNavigation.BuocLoTrinh.Any(s => s.MaBuoc == stepId && !s.IsArchived &&
                !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO"))
            .Select(r => new ResourceInfo(r.DuongDanTep, r.TenTaiNguyen)).FirstOrDefaultAsync(ct);

    [HttpPost("{stepId:int}/resources/{resourceId:int}/ticket")]
    public async Task<IActionResult> ResourceTicket(int courseId, int stepId, int resourceId, CancellationToken ct)
    {
        if (!await HasAccess(courseId, ct)) return Forbid();
        var row = await FindResource(courseId, stepId, resourceId, ct);
        if (row is null) return NotFound();
        DateTimeOffset? sessionExpiry = long.TryParse(User.FindFirstValue("exp"), out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp) : null;
        if (sessionExpiry is { } expiry && expiry <= clock.GetUtcNow()) return Unauthorized();
        var (ticket, expires) = tickets.Issue(LearnerId, courseId, stepId, resourceId, sessionExpiry);
        return Ok(new { url = $"/api/learning/courses/{courseId}/steps/{stepId}/resources/{resourceId}/stream?ticket={ticket}",
            expiresAtUtc = expires });
    }

    [AllowAnonymous]
    [HttpGet("{stepId:int}/resources/{resourceId:int}/stream")]
    public async Task<IActionResult> StreamResource(int courseId, int stepId, int resourceId,
        [FromQuery] string? ticket, [FromQuery] bool download, CancellationToken ct)
    {
        if (!tickets.TryRead(ticket, out var grant) || grant!.CourseId != courseId ||
            grant.StepId != stepId || grant.ResourceId != resourceId)
            return Unauthorized();
        if (!await HasAccess(courseId, grant.LearnerId, ct)) return Forbid();
        var row = await FindResource(courseId, stepId, resourceId, ct);
        if (row is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return await Open(row, download, ct);
    }

    [HttpGet("{stepId:int}/resources/{resourceId:int}")]
    public async Task<IActionResult> Resource(int courseId, int stepId, int resourceId, CancellationToken ct)
    {
        if (!await HasAccess(courseId, ct)) return Forbid();
        var row = await FindResource(courseId, stepId, resourceId, ct);
        if (row is null) return NotFound();
        return await Open(row, false, ct);
    }

    private async Task<IActionResult> Open(ResourceInfo row, bool download, CancellationToken ct)
    {
        try
        {
            var file = await storage.OpenAsync(row.Path, ct);
            return file is null ? NotFound() : File(file.Value.Stream, file.Value.ContentType,
                download ? LectureResourceStorage.DownloadName(row.Name, row.Path) : null,
                enableRangeProcessing: true);
        }
        catch (Exception e) when (e is RequestFailedException or AuthenticationFailedException)
        { return StatusCode(503, new { code = "MEDIA_UNAVAILABLE" }); }
    }
}
