using System.Text.Json;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.Models;

namespace STO123.Tests;

public sealed class AdminCurriculumIntegrationTests
{
    [Fact]
    public async Task Unit_lesson_and_lecture_step_persist_only_in_the_selected_course()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL, TrangThai TEXT NOT NULL,
                MoTa TEXT, DuongDanAnhDaiDien TEXT, GiaiDoan INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE UnitKhoaHoc (MaUnit INTEGER PRIMARY KEY AUTOINCREMENT, MaKhoaHoc INTEGER NOT NULL,
                TenUnit TEXT NOT NULL, MoTa TEXT, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(MaKhoaHoc) REFERENCES KhoaHoc(MaKhoaHoc), UNIQUE(MaKhoaHoc,ThuTu));
            CREATE TABLE LessonKhoaHoc (MaLesson INTEGER PRIMARY KEY AUTOINCREMENT, MaUnit INTEGER NOT NULL,
                TenLesson TEXT NOT NULL, MoTa TEXT, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(MaUnit) REFERENCES UnitKhoaHoc(MaUnit), UNIQUE(MaUnit,ThuTu));
            CREATE TABLE DanhMucBaiGiang (MaDanhMuc INTEGER PRIMARY KEY, TrangThai TEXT NOT NULL);
            CREATE TABLE BaiGiang (MaBaiGiang INTEGER PRIMARY KEY, MaDanhMuc INTEGER, TrangThai TEXT NOT NULL,
                TieuDe TEXT NOT NULL, NoiDungTomTat TEXT, NoiDung TEXT);
            CREATE TABLE TaiNguyenBaiGiang (MaTaiNguyen INTEGER PRIMARY KEY, MaBaiGiang INTEGER NOT NULL,
                LoaiTaiNguyen TEXT NOT NULL, TenTaiNguyen TEXT NOT NULL, DuongDanTep TEXT NOT NULL, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE BuocLoTrinh (MaBuoc INTEGER PRIMARY KEY AUTOINCREMENT, MaLesson INTEGER NOT NULL,
                TieuDe TEXT NOT NULL, ThuTu INTEGER NOT NULL, MaBaiGiang INTEGER, MaDeThi INTEGER, MaChuDe INTEGER, MaBaiTap INTEGER, IsArchived INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(MaLesson) REFERENCES LessonKhoaHoc(MaLesson), UNIQUE(MaLesson,ThuTu),
                CHECK ((MaBaiGiang IS NOT NULL) + (MaDeThi IS NOT NULL) + (MaChuDe IS NOT NULL) + (MaBaiTap IS NOT NULL) = 1));
            CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL, MaGoiHoc INTEGER,
                TrangThai TEXT NOT NULL, NgayBatDau TEXT NOT NULL, NgayKetThuc TEXT NOT NULL);
            CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY, MaDangKy INTEGER NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY(MaDangKy,MaKhoaHoc));
            CREATE TABLE GoiHoc (MaGoiHoc INTEGER PRIMARY KEY);
            CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY(MaGoiHoc,MaKhoaHoc));
            CREATE TABLE TienDoBuocLoTrinh (MaBuoc INTEGER NOT NULL, MaNguoiDung INTEGER NOT NULL,
                MaDangKy INTEGER NOT NULL DEFAULT 20);
            CREATE TABLE BaiTapKhoaHoc (MaBaiTap INTEGER PRIMARY KEY AUTOINCREMENT, TieuDe TEXT NOT NULL);
            CREATE TABLE PhienBanBaiTapKhoaHoc (MaPhienBan INTEGER PRIMARY KEY AUTOINCREMENT,
                MaBaiTap INTEGER NOT NULL, SoPhienBan INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                TaoLuc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE CauHoiBaiTapKhoaHoc (MaCauHoi INTEGER PRIMARY KEY AUTOINCREMENT,
                MaBaiTap INTEGER NOT NULL, MaPhienBan INTEGER NOT NULL, ThuTu INTEGER NOT NULL,
                NoiDung TEXT NOT NULL, NguLieu TEXT, PhuongAnA TEXT NOT NULL, PhuongAnB TEXT NOT NULL,
                PhuongAnC TEXT NOT NULL, PhuongAnD TEXT NOT NULL, PhuongAnDung TEXT NOT NULL, GiaiThich TEXT);
            CREATE TABLE LuotBaiTapKhoaHoc (MaLuot INTEGER PRIMARY KEY AUTOINCREMENT,
                MaDangKy INTEGER NOT NULL, MaBuoc INTEGER NOT NULL, MaBaiTap INTEGER NOT NULL,
                MaPhienBan INTEGER NOT NULL, LanLam INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                BatDauLuc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, HoanThanhLuc TEXT);
            CREATE TABLE LanKiemTraBaiTapKhoaHoc (MaLan INTEGER PRIMARY KEY AUTOINCREMENT,
                MaLuot INTEGER NOT NULL, MaBaiTap INTEGER NOT NULL, MaPhienBan INTEGER NOT NULL,
                MaCauHoi INTEGER NOT NULL, LanKiemTra INTEGER NOT NULL, KhoaYeuCau TEXT NOT NULL,
                PhuongAnChon TEXT NOT NULL, PhuongAnDungLucKiemTra TEXT NOT NULL,
                Dung INTEGER NOT NULL, GiaiThichLucKiemTra TEXT, KiemTraLuc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
            INSERT INTO KhoaHoc (MaKhoaHoc,TenKhoaHoc,TrangThai) VALUES (1,'Khóa A','DANG_MO'),(2,'Khóa B','DANG_MO');
            INSERT INTO BaiGiang VALUES (10,NULL,'DANG_MO','Bài mở đầu',NULL,'Nội dung học thật');
            INSERT INTO DangKyGoiHoc VALUES (20,99,NULL,'DANG_SU_DUNG','2000-01-01','2100-01-01');
            INSERT INTO GiaoDichThanhToan VALUES (30,20,'THANH_CONG');
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (20,1);
            """);
        var api = new AdminCurriculumController(db);

        Assert.IsType<CreatedResult>(await api.AddUnit(1, new ContentNameRequest("Unit đầu", null), default));
        var unitId = await db.UnitKhoaHoc.Select(x => x.MaUnit).SingleAsync();
        Assert.IsType<CreatedResult>(await api.AddLesson(1, unitId, new ContentNameRequest("Lesson đầu", null), default));
        var lessonId = await db.LessonKhoaHoc.Select(x => x.MaLesson).SingleAsync();
        Assert.IsType<CreatedResult>(await api.AddStep(1, unitId, lessonId,
            new ContentStepRequest("Đọc bài giảng", "LECTURE", 10), default));
        Assert.IsType<BadRequestObjectResult>(await api.AddStep(1, unitId, lessonId,
            new ContentStepRequest("Đề không được hỗ trợ", "EXAM", 1), default));

        Assert.IsType<CreatedResult>(await api.AddUnit(1, new ContentNameRequest("Unit sau", null), default));
        var secondUnitId = await db.UnitKhoaHoc.Where(x => x.MaUnit != unitId).Select(x => x.MaUnit).SingleAsync();
        Assert.IsType<OkResult>(await api.OrderUnits(1, new ContentOrderRequest([secondUnitId, unitId]), default));
        Assert.Equal([secondUnitId, unitId], await db.UnitKhoaHoc.AsNoTracking()
            .OrderBy(x => x.ThuTu).Select(x => x.MaUnit).ToArrayAsync());

        var first = Assert.IsType<OkObjectResult>(await api.Outline(1, default));
        var second = Assert.IsType<OkObjectResult>(await api.Outline(2, default));
        var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var firstJson = JsonSerializer.Serialize(first.Value, jsonOptions);
        var secondJson = JsonSerializer.Serialize(second.Value, jsonOptions);
        Assert.Contains("Đọc bài giảng", firstJson);
        Assert.Contains("LECTURE", firstJson);
        Assert.DoesNotContain("Đọc bài giảng", secondJson);
        Assert.Equal(1, await db.BuocLoTrinh.CountAsync());

        var stepId = await db.BuocLoTrinh.Select(x => x.MaBuoc).SingleAsync();
        var previewResult = Assert.IsType<OkObjectResult>(await api.PreviewStep(1, stepId, default));
        Assert.Contains("Nội dung học thật", JsonSerializer.Serialize(previewResult.Value, jsonOptions));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["LectureResources:ContainerName"] = "isolated-test",
            ["Jwt:Key"] = "isolated-step1-test-signing-key-2026"
        }).Build();
        var blobs = new BlobServiceClient(new Uri("https://isolated.invalid.blob.core.windows.net"), new DefaultAzureCredential());
        var learner = new CourseLearningController(db, new STO123.Services.Admin.LectureResourceStorage(blobs, configuration),
            new STO123.Services.Admin.LectureMediaTicket(configuration, TimeProvider.System), TimeProvider.System) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        learner.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "HOC_VIEN")], "test"));
        var learningResult = Assert.IsType<OkObjectResult>(await learner.Detail(1, stepId, default));
        Assert.Contains("Nội dung học thật", JsonSerializer.Serialize(learningResult.Value, jsonOptions));
        Assert.IsType<ForbidResult>(await learner.Detail(2, stepId, default));
        Assert.IsType<ForbidResult>(await learner.Complete(2, stepId, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TienDoBuocLoTrinh (MaNguoiDung,MaBuoc) VALUES ({98},{stepId})");
        Assert.IsType<ConflictObjectResult>(await learner.Complete(1, stepId, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM TienDoBuocLoTrinh WHERE MaBuoc = {stepId}");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO TaiNguyenBaiGiang (MaTaiNguyen,MaBaiGiang,LoaiTaiNguyen,TenTaiNguyen,DuongDanTep,ThuTu)
            VALUES (81,10,'PDF','Bài học','lecture-resources/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.pdf',1);
            """);
        var issued = Assert.IsType<OkObjectResult>(await learner.ResourceTicket(1, stepId, 81, default));
        var ticketUrl = JsonDocument.Parse(JsonSerializer.Serialize(issued.Value)).RootElement.GetProperty("url").GetString()!;
        var ticket = ticketUrl.Split("ticket=", 2)[1];
        Assert.IsType<UnauthorizedResult>(await learner.StreamResource(2, stepId, 81, ticket, false, default));
        Assert.IsType<UnauthorizedResult>(await learner.StreamResource(1, stepId, 81, ticket + "x", false, default));
        Assert.IsType<ForbidResult>(await learner.ResourceTicket(2, stepId, 81, default));
        Assert.IsType<NotFoundResult>(await learner.ResourceTicket(1, stepId, 82, default));
        Assert.IsType<OkObjectResult>(await api.ArchiveStep(1, unitId, lessonId, stepId,
            new ContentArchiveRequest(true), default));
        Assert.IsType<NotFoundResult>(await learner.Detail(1, stepId, default));
        Assert.IsType<OkObjectResult>(await api.ArchiveStep(1, unitId, lessonId, stepId,
            new ContentArchiveRequest(false), default));
        Assert.IsType<OkObjectResult>(await learner.Detail(1, stepId, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TienDoBuocLoTrinh (MaNguoiDung,MaBuoc) VALUES ({99},{stepId})");
        Assert.IsType<OkResult>(await api.EditStep(1, unitId, lessonId, stepId,
            new ContentStepRequest("Đọc lại bài giảng", "LECTURE", 10), default));
        Assert.Equal("Đọc lại bài giảng", await db.BuocLoTrinh.Where(x => x.MaBuoc == stepId).Select(x => x.TieuDe).SingleAsync());
        Assert.IsType<ConflictObjectResult>(await api.DeleteStep(1, unitId, lessonId, stepId, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM TienDoBuocLoTrinh WHERE MaBuoc = {stepId}");
        Assert.IsType<OkResult>(await api.DeleteStep(1, unitId, lessonId, stepId, default));

        Assert.IsType<NotFoundResult>(await api.EditLesson(2, unitId, lessonId,
            new ContentNameRequest("Sai khóa", null), default));
        var extraLesson = Assert.IsType<CreatedResult>(await api.AddLesson(1, secondUnitId,
            new ContentNameRequest("Lesson có thể xóa", null), default));
        var extraLessonId = JsonDocument.Parse(JsonSerializer.Serialize(extraLesson.Value)).RootElement.GetProperty("id").GetInt32();
        Assert.IsType<CreatedResult>(await api.AddStep(1, secondUnitId, extraLessonId,
            new ContentStepRequest("Bài chưa học", "LECTURE", 10), default));
        Assert.IsType<OkResult>(await api.DeleteUnit(1, secondUnitId, default));
        Assert.False(await db.UnitKhoaHoc.AnyAsync(x => x.MaUnit == secondUnitId));
        Assert.IsType<BadRequestObjectResult>(await api.OrderUnits(1, new ContentOrderRequest([unitId, unitId]), default));
        var questions = new[] { new CourseQuestionRequest("Câu hỏi", null, "Một", "Hai", "Ba", "Bốn", "B", "Giải thích") };
        Assert.IsType<CreatedResult>(await api.AddExercise(1, unitId, lessonId,
            new CourseExerciseRequest("Bài tập riêng", questions, true), default));
        Assert.Equal(1, await db.BaiTapKhoaHoc.CountAsync());
        Assert.Equal(1, await db.CauHoiBaiTapKhoaHoc.CountAsync());
        var exerciseStep = await db.BuocLoTrinh.SingleAsync(x => x.MaBaiTap != null);
        Assert.Equal(lessonId, exerciseStep.MaLesson);
        Assert.IsType<OkObjectResult>(await api.ReviseExercise(1, unitId, lessonId, exerciseStep.MaBuoc,
            new CourseExerciseRequest("Bài tập riêng sửa", questions, true), default));
        Assert.Equal(2, await db.PhienBanBaiTapKhoaHoc.CountAsync());
        var exerciseDetail = Assert.IsType<OkObjectResult>(await learner.Detail(1, exerciseStep.MaBuoc, default));
        Assert.Contains("EXERCISE", JsonSerializer.Serialize(exerciseDetail.Value));
        Assert.IsType<OkObjectResult>(await learner.Exercise(1, exerciseStep.MaBuoc, default));
        Assert.IsType<ForbidResult>(await learner.Exercise(2, exerciseStep.MaBuoc, default));
        var started = Assert.IsType<CreatedResult>(await learner.StartExercise(1, exerciseStep.MaBuoc, new StartExerciseRequest(null), default));
        var attemptId = JsonDocument.Parse(JsonSerializer.Serialize(started.Value)).RootElement.GetProperty("id").GetInt64();
        Assert.Equal(1, await db.LuotBaiTapKhoaHoc.CountAsync());
        Assert.Equal(attemptId, await db.LuotBaiTapKhoaHoc.Select(x => x.MaLuot).SingleAsync());
        Assert.Equal(exerciseStep.MaBuoc, await db.LuotBaiTapKhoaHoc.Select(x => x.MaBuoc).SingleAsync());
        Assert.True(await db.LuotBaiTapKhoaHoc.AnyAsync(x => x.MaLuot == attemptId && x.MaDangKy == 20 &&
            x.MaBuoc == exerciseStep.MaBuoc && db.BuocLoTrinh.Any(s => s.MaBuoc == exerciseStep.MaBuoc &&
                s.MaBaiTap == x.MaBaiTap && s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == 1)));
        var attemptVersion = await db.LuotBaiTapKhoaHoc.Select(x => x.MaPhienBan).SingleAsync();
        var questionId = await db.CauHoiBaiTapKhoaHoc.Where(x => x.MaPhienBan == attemptVersion)
            .Select(x => x.MaCauHoi).FirstAsync();
        var key = Guid.NewGuid();
        var checkedAnswer = Assert.IsType<OkObjectResult>(await learner.CheckExercise(1, exerciseStep.MaBuoc, attemptId,
            new ExerciseCheckRequest(questionId, "A", key), default));
        Assert.Contains("false", JsonSerializer.Serialize(checkedAnswer.Value));
        var repeatedCheck = Assert.IsType<OkObjectResult>(await learner.CheckExercise(1, exerciseStep.MaBuoc, attemptId,
            new ExerciseCheckRequest(questionId, "A", key), default));
        var repeatedJson = JsonDocument.Parse(JsonSerializer.Serialize(repeatedCheck.Value)).RootElement;
        Assert.Equal(1, repeatedJson.GetProperty("checkedCount").GetInt32());
        Assert.True(repeatedJson.GetProperty("completed").GetBoolean());
        Assert.Equal(1, await db.LanKiemTraBaiTapKhoaHoc.CountAsync());
        Assert.IsType<ConflictObjectResult>(await learner.CheckExercise(1, exerciseStep.MaBuoc, attemptId,
            new ExerciseCheckRequest(questionId, "B", key), default));
        Assert.IsType<NotFoundResult>(await learner.CheckExercise(1, exerciseStep.MaBuoc, attemptId,
            new ExerciseCheckRequest(questionId + 999, "B", Guid.NewGuid()), default));
        Assert.IsType<OkObjectResult>(await learner.ExerciseAnswer(1, exerciseStep.MaBuoc, attemptId, questionId, default));
        Assert.IsType<ConflictObjectResult>(await api.DeleteUnit(1, unitId, default));
        api.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        api.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "ADMIN_QUAN_LY")], "test"));
        var duplicated = Assert.IsType<CreatedResult>(await api.DuplicateStep(1, unitId, lessonId, exerciseStep.MaBuoc, default));
        var duplicatedStepId = JsonDocument.Parse(JsonSerializer.Serialize(duplicated.Value)).RootElement.GetProperty("id").GetInt32();
        var duplicatedExerciseId = await db.BuocLoTrinh.Where(x => x.MaBuoc == duplicatedStepId).Select(x => x.MaBaiTap).SingleAsync();
        Assert.NotEqual(exerciseStep.MaBaiTap, duplicatedExerciseId);
        Assert.Equal(1, await db.CauHoiBaiTapKhoaHoc.CountAsync(x => x.MaBaiTap == duplicatedExerciseId));
        Assert.IsType<OkResult>(await api.DeleteStep(1, unitId, lessonId, duplicatedStepId, default));
        var unused = Assert.IsType<CreatedResult>(await api.AddExercise(1, unitId, lessonId,
            new CourseExerciseRequest("Bài tập chưa học", questions, false), default));
        var unusedStepId = JsonDocument.Parse(JsonSerializer.Serialize(unused.Value)).RootElement.GetProperty("id").GetInt32();
        var unusedExerciseId = JsonDocument.Parse(JsonSerializer.Serialize(unused.Value)).RootElement.GetProperty("exerciseId").GetInt32();
        Assert.IsType<OkResult>(await api.DeleteStep(1, unitId, lessonId, unusedStepId, default));
        Assert.False(await db.BaiTapKhoaHoc.AsNoTracking().AnyAsync(x => x.MaBaiTap == unusedExerciseId));
        Assert.False(await db.PhienBanBaiTapKhoaHoc.AsNoTracking().AnyAsync(x => x.MaBaiTap == unusedExerciseId));
        Assert.False(await db.CauHoiBaiTapKhoaHoc.AsNoTracking().AnyAsync(x => x.MaBaiTap == unusedExerciseId));
        var impactResult = Assert.IsType<OkObjectResult>(await api.StepDeleteImpact(1, unitId, lessonId, exerciseStep.MaBuoc, default));
        var impact = JsonDocument.Parse(JsonSerializer.Serialize(impactResult.Value)).RootElement;
        var progressCount = impact.GetProperty(nameof(ContentDeleteImpact.ProgressRecords)).GetInt32();
        var attemptCount = impact.GetProperty(nameof(ContentDeleteImpact.Attempts)).GetInt32();
        var checkCount = impact.GetProperty(nameof(ContentDeleteImpact.Checks)).GetInt32();
        Assert.Equal(1, attemptCount);
        Assert.Equal(1, checkCount);
        Assert.IsType<ConflictObjectResult>(await api.DeleteStep(1, unitId, lessonId, exerciseStep.MaBuoc, default,
            true, progressCount, attemptCount + 1, checkCount));
        Assert.True(await db.BuocLoTrinh.AnyAsync(x => x.MaBuoc == exerciseStep.MaBuoc));
        Assert.IsType<OkResult>(await api.DeleteStep(1, unitId, lessonId, exerciseStep.MaBuoc, default,
            true, progressCount, attemptCount, checkCount));
        Assert.False(await db.BuocLoTrinh.AnyAsync(x => x.MaBuoc == exerciseStep.MaBuoc));
        Assert.False(await db.LuotBaiTapKhoaHoc.AnyAsync(x => x.MaBuoc == exerciseStep.MaBuoc));
        Assert.False(await db.LanKiemTraBaiTapKhoaHoc.AnyAsync());
        Assert.True(await db.DangKyGoiHoc.AnyAsync(x => x.MaDangKy == 20));
        Assert.True(await db.KhoaHoc.AnyAsync(x => x.MaKhoaHoc == 2));
    }
}
