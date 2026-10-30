using System.Security.Claims;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminContentIdempotencySqlServerTests
{
    [Fact]
    public async Task Excel_resource_is_persisted_once_and_replayed_from_private_Azurite_blob()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_SQL_EXPRESS_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("STO123_RUN_AZURITE_TESTS") != "1") return;
        var dbName = "STO123_ExcelIdem_" + Guid.NewGuid().ToString("N")[..8];
        var master = "Server=.\\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True";
        var isolated = $"Server=.\\SQLEXPRESS;Database={dbName};Integrated Security=True;TrustServerCertificate=True";
        var containerName = "sto123-excel-" + Guid.NewGuid().ToString("N");
        var blobs = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = blobs.GetBlobContainerClient(containerName);
        await Execute(master, $"CREATE DATABASE [{dbName}]");
        await container.CreateIfNotExistsAsync();
        try
        {
            await Execute(isolated, """
                CREATE TABLE dbo.NguoiDung (MaNguoiDung int NOT NULL PRIMARY KEY);
                INSERT dbo.NguoiDung VALUES (1);
                CREATE TABLE dbo.KhoaHoc (MaKhoaHoc int NOT NULL PRIMARY KEY, TenKhoaHoc nvarchar(128) NOT NULL, TrangThai varchar(32) NOT NULL);
                CREATE TABLE dbo.GoiHoc (MaGoiHoc int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.GoiHocKhoaHoc (MaGoiHoc int NOT NULL, MaKhoaHoc int NOT NULL);
                CREATE TABLE dbo.DangKyGoiHoc (MaDangKy int NOT NULL PRIMARY KEY, MaNguoiDung int NOT NULL,
                    MaGoiHoc int NULL, TrangThai varchar(32) NOT NULL, NgayBatDau datetime NOT NULL, NgayKetThuc datetime NOT NULL);
                CREATE TABLE dbo.GiaoDichThanhToan (MaGiaoDich int NOT NULL PRIMARY KEY, MaDangKy int NOT NULL,
                    TrangThai varchar(32) NOT NULL);
                CREATE TABLE dbo.ChiTietDangKyKhoaHoc (MaDangKy int NOT NULL, MaKhoaHoc int NOT NULL);
                CREATE TABLE dbo.UnitKhoaHoc (MaUnit int NOT NULL PRIMARY KEY, MaKhoaHoc int NOT NULL, TenUnit nvarchar(128) NOT NULL,
                    MoTa nvarchar(512), ThuTu int NOT NULL);
                CREATE TABLE dbo.LessonKhoaHoc (MaLesson int NOT NULL PRIMARY KEY, MaUnit int NOT NULL, TenLesson nvarchar(128) NOT NULL,
                    MoTa nvarchar(512), ThuTu int NOT NULL);
                CREATE TABLE dbo.BuocLoTrinh (MaBuoc int NOT NULL PRIMARY KEY, MaLesson int NOT NULL, TieuDe nvarchar(128) NOT NULL,
                    ThuTu int NOT NULL, MaBaiGiang int NULL, MaDeThi int NULL, MaChuDe int NULL);
                CREATE TABLE dbo.TienDoBuocLoTrinh (MaBuoc int NOT NULL);
                CREATE TABLE dbo.DanhMucBaiGiang (MaDanhMuc int NOT NULL PRIMARY KEY,
                    TenDanhMuc nvarchar(64) NOT NULL, MoTa nvarchar(512) NULL, TrangThai varchar(32) NOT NULL);
                CREATE TABLE dbo.BaiGiang (MaBaiGiang int NOT NULL PRIMARY KEY, MaDanhMuc int NULL, CapDo int NULL,
                    TieuDe nvarchar(128) NOT NULL, MaPart int NULL, NoiDungTomTat nvarchar(512) NULL,
                    NoiDung nvarchar(max) NULL, MaAdminNoiDung int NULL, NgayTao datetime NOT NULL, TrangThai varchar(32) NOT NULL);
                INSERT dbo.BaiGiang VALUES (10,NULL,NULL,N'Excel isolated',NULL,NULL,NULL,1,GETDATE(),'DANG_MO');
                CREATE TABLE dbo.TaiNguyenBaiGiang (MaTaiNguyen int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    MaBaiGiang int NOT NULL, LoaiTaiNguyen varchar(32) NOT NULL, TenTaiNguyen nvarchar(128) NOT NULL,
                    DuongDanTep varchar(512) NOT NULL, ThuTu int NOT NULL,
                    CONSTRAINT CK_Resource_Old CHECK (LoaiTaiNguyen IN ('HINH_ANH','POWERPOINT','WORD','PDF','AUDIO','VIDEO')),
                    CONSTRAINT UQ_TaiNguyenBaiGiang_ThuTu UNIQUE (MaBaiGiang,ThuTu));
                """);
            var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "sql"));
            var excel = (await File.ReadAllTextAsync(Path.Combine(scripts, "lecture-resource-excel-check-proposal.sql")))
                .Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;");
            await Execute(isolated, excel);
            foreach (var batch in Regex.Split(await File.ReadAllTextAsync(Path.Combine(scripts,
                "admin-content-idempotency-proposal.sql")), @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) await Execute(isolated, batch);
            await Execute(isolated, """
                BEGIN TRANSACTION;
                INSERT dbo.TaiNguyenBaiGiang (MaBaiGiang,LoaiTaiNguyen,TenTaiNguyen,DuongDanTep,ThuTu)
                VALUES (10,'HINH_ANH',N'old image','legacy/image',10),
                       (10,'POWERPOINT',N'old slides','legacy/slides',11),
                       (10,'WORD',N'old word','legacy/word',12),
                       (10,'PDF',N'old pdf','legacy/pdf',13),
                       (10,'AUDIO',N'old audio','legacy/audio',14),
                       (10,'VIDEO',N'old video','legacy/video',15);
                ROLLBACK TRANSACTION;
                """);
            await Execute(isolated, await File.ReadAllTextAsync(Path.Combine(scripts, "admin-content-step1-verify.sql")));
            var bytes = ExcelBytes();
            var first = await CreateExcel(isolated, blobs, containerName, "excel-retry-2026", bytes);
            var replay = await CreateExcel(isolated, blobs, containerName, "excel-retry-2026", bytes);
            Assert.IsType<OkObjectResult>(first);
            Assert.IsType<OkObjectResult>(replay);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(first).Value),
                System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(replay).Value));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang WHERE LoaiTaiNguyen='EXCEL'"));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='COMPLETED'"));
            var path = await Text(isolated, "SELECT DuongDanTep FROM dbo.TaiNguyenBaiGiang");
            Assert.True(await container.GetBlobClient(path).ExistsAsync());
            Assert.IsType<ConflictObjectResult>(await CreateExcel(isolated, blobs, containerName,
                "excel-retry-2026", bytes.Concat(new byte[] { 0 }).ToArray()));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));

            var simultaneous = await Task.WhenAll(CreateExcel(isolated, blobs, containerName, "excel-concurrent-2026", bytes),
                CreateExcel(isolated, blobs, containerName, "excel-concurrent-2026", bytes));
            Assert.Contains(simultaneous, r => r is OkObjectResult);
            Assert.All(simultaneous, r => Assert.True(r is OkObjectResult or ConflictObjectResult));
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));
            Assert.Equal(2, await BlobCount(container));

            await Execute(isolated, "ALTER TABLE dbo.TaiNguyenBaiGiang WITH NOCHECK ADD CONSTRAINT CK_IsolatedFailure CHECK (TenTaiNguyen <> 'Study Excel')");
            var sqlFailure = await CreateExcel(isolated, blobs, containerName, "excel-sql-failure-2026", bytes);
            Assert.IsType<ConflictObjectResult>(sqlFailure);
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));
            Assert.Equal(2, await BlobCount(container));
            Assert.Equal(0, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='PENDING'"));
            await Execute(isolated, "ALTER TABLE dbo.TaiNguyenBaiGiang DROP CONSTRAINT CK_IsolatedFailure");
            Assert.IsType<OkObjectResult>(await CreateExcel(isolated, blobs, containerName, "excel-sql-failure-2026", bytes));

            var unavailableContainer = "sto123-missing-" + Guid.NewGuid().ToString("N");
            var blobFailure = await CreateExcel(isolated, blobs, unavailableContainer, "excel-blob-failure-2026", bytes);
            Assert.IsType<ObjectResult>(blobFailure);
            Assert.Equal(3, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));
            Assert.Equal(0, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='PENDING'"));
            Assert.IsType<OkObjectResult>(await CreateExcel(isolated, blobs, containerName, "excel-blob-failure-2026", bytes));
            Assert.Equal(4, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));
            var orphan = new LectureResourceStorage(blobs, new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["LectureResources:ContainerName"] = containerName }).Build()).NewPath("orphan.xlsx");
            await container.GetBlobClient(orphan).UploadAsync(new BinaryData(bytes));
            await Execute(isolated, $"""
                INSERT dbo.AdminContentIdempotency (MaAdmin,ThaoTac,KhoaYeuCau,DauVanTay,TrangThai,DuongDanBlob,LeaseToken,LeaseUntilUtc)
                VALUES (1,'CREATE_LECTURE_RESOURCE','stale-orphan-2026',CONVERT(binary(32),0x01),'PENDING',
                    '{orphan}',NEWID(),DATEADD(minute,-1,SYSUTCDATETIME()));
                """);
            var workerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["AdminContent:EnableIdempotency"] = "true", ["LectureResources:ContainerName"] = containerName
            }).Build();
            using var services = new ServiceCollection().AddLogging()
                .AddDbContext<ToeicDbContext>(options => options.UseSqlServer(isolated))
                .AddSingleton(new LectureResourceStorage(blobs, workerConfig)).BuildServiceProvider();
            var worker = new AdminContentPendingCleanupService(services.GetRequiredService<IServiceScopeFactory>(),
                workerConfig, NullLogger<AdminContentPendingCleanupService>.Instance);
            await worker.StartAsync(default);
            for (var attempt = 0; attempt < 20 && await Scalar(isolated,
                "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE KhoaYeuCau='stale-orphan-2026'") != 0; attempt++)
                await Task.Delay(200);
            await worker.StopAsync(default);
            Assert.Equal(0, await Scalar(isolated,
                "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE KhoaYeuCau='stale-orphan-2026'"));
            Assert.False(await container.GetBlobClient(orphan).ExistsAsync());
            var livePath = await Text(isolated,
                "SELECT TOP (1) DuongDanTep FROM dbo.TaiNguyenBaiGiang ORDER BY MaTaiNguyen DESC");
            await Execute(isolated, $"""
                INSERT dbo.AdminContentIdempotency (MaAdmin,ThaoTac,KhoaYeuCau,DauVanTay,TrangThai,DuongDanBlob,LeaseToken,LeaseUntilUtc)
                VALUES (1,'CREATE_LECTURE_RESOURCE','stale-live-2026',CONVERT(binary(32),0x02),'PENDING',
                    '{livePath}',NEWID(),DATEADD(minute,-1,SYSUTCDATETIME()));
                """);
            var liveWorker = new AdminContentPendingCleanupService(services.GetRequiredService<IServiceScopeFactory>(),
                workerConfig, NullLogger<AdminContentPendingCleanupService>.Instance);
            await liveWorker.StartAsync(default);
            for (var attempt = 0; attempt < 20 && await Scalar(isolated,
                "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE KhoaYeuCau='stale-live-2026' AND LeaseUntilUtc>SYSUTCDATETIME()") == 0; attempt++)
                await Task.Delay(200);
            await liveWorker.StopAsync(default);
            Assert.Equal(1, await Scalar(isolated,
                "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE KhoaYeuCau='stale-live-2026'"));
            Assert.True(await container.GetBlobClient(livePath).ExistsAsync());

            var crashed = await CreateExcel(isolated, blobs, containerName, "excel-crash-2026", bytes,
                simulateCrashAfterBlob: true);
            Assert.Equal(503, Assert.IsType<ObjectResult>(crashed).StatusCode);
            var stalePath = await Text(isolated,
                "SELECT DuongDanBlob FROM dbo.AdminContentIdempotency WHERE KhoaYeuCau='excel-crash-2026'");
            Assert.True(await container.GetBlobClient(stalePath).ExistsAsync());
            await Execute(isolated, """
                UPDATE dbo.AdminContentIdempotency SET LeaseUntilUtc=DATEADD(minute,-1,SYSUTCDATETIME())
                WHERE KhoaYeuCau='excel-crash-2026'
                """);
            Assert.IsType<OkObjectResult>(await CreateExcel(isolated, blobs, containerName, "excel-crash-2026", bytes));
            Assert.False(await container.GetBlobClient(stalePath).ExistsAsync());
            Assert.Equal(5, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));

            await Execute(isolated, """
                INSERT dbo.NguoiDung VALUES (99);
                INSERT dbo.KhoaHoc VALUES (1,N'Course', 'DANG_MO');
                INSERT dbo.UnitKhoaHoc VALUES (2,1,N'Unit',NULL,1);
                INSERT dbo.LessonKhoaHoc VALUES (3,2,N'Lesson',NULL,1);
                INSERT dbo.BuocLoTrinh VALUES (4,3,N'Excel lesson',1,10,NULL,NULL);
                INSERT dbo.DangKyGoiHoc VALUES (20,99,NULL,'DANG_SU_DUNG','2000-01-01','2100-01-01');
                INSERT dbo.GiaoDichThanhToan VALUES (30,20,'THANH_CONG');
                INSERT dbo.ChiTietDangKyKhoaHoc VALUES (20,1);
                """);
            var resourceId = await Scalar(isolated, "SELECT MAX(MaTaiNguyen) FROM dbo.TaiNguyenBaiGiang");
            await using (var readDb = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
                .UseSqlServer(isolated).Options))
            {
                var learnerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                    ["LectureResources:ContainerName"] = containerName,
                    ["Jwt:Key"] = "isolated-excel-download-signing-key-2026"
                }).Build();
                var learner = new CourseLearningController(readDb,
                    new LectureResourceStorage(blobs, learnerConfig),
                    new LectureMediaTicket(learnerConfig, TimeProvider.System), TimeProvider.System) {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
                };
                learner.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "HOC_VIEN")], "test"));
                var ticketResult = Assert.IsType<OkObjectResult>(await learner.ResourceTicket(1, 4, resourceId, default));
                var ticketUrl = System.Text.Json.JsonDocument.Parse(
                    System.Text.Json.JsonSerializer.Serialize(ticketResult.Value)).RootElement.GetProperty("url").GetString()!;
                var ticket = ticketUrl.Split("ticket=", 2)[1];
                var download = Assert.IsType<FileStreamResult>(await learner.StreamResource(1, 4, resourceId,
                    ticket, true, default));
                Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", download.ContentType);
                Assert.EndsWith(".xlsx", download.FileDownloadName);
                await using (download.FileStream)
                {
                    var signature = new byte[4];
                    await download.FileStream.ReadExactlyAsync(signature);
                    Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(signature[..2]));
                }
                learner.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "100"), new Claim(ClaimTypes.Role, "HOC_VIEN")], "test"));
                Assert.IsType<ForbidResult>(await learner.ResourceTicket(1, 4, resourceId, default));
                Assert.IsType<UnauthorizedResult>(await learner.StreamResource(1, 4, resourceId,
                    ticket + "x", true, default));
            }
            var rollback = await File.ReadAllTextAsync(Path.Combine(scripts, "admin-content-step1-rollback.sql"));
            await Assert.ThrowsAsync<SqlException>(() => Execute(isolated, rollback));
            Assert.Equal(5, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.TaiNguyenBaiGiang"));
            await Execute(isolated, "DELETE FROM dbo.TaiNguyenBaiGiang; DELETE FROM dbo.AdminContentIdempotency;");
            await Execute(isolated, rollback);
            Assert.Equal(0, await Scalar(isolated,
                "SELECT COUNT(*) FROM sys.tables WHERE name='AdminContentIdempotency'"));
            await Assert.ThrowsAsync<SqlException>(() => Execute(isolated, """
                INSERT dbo.TaiNguyenBaiGiang (MaBaiGiang,LoaiTaiNguyen,TenTaiNguyen,DuongDanTep,ThuTu)
                VALUES (10,'EXCEL',N'blocked again','test',1)
                """));
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            await Execute(master, $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]");
        }
    }

    [Fact]
    public async Task Unit_creation_replays_after_restart_rejects_changed_body_and_serializes_concurrent_retries()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_SQL_EXPRESS_TESTS") != "1") return;
        var dbName = "STO123_Idem_" + Guid.NewGuid().ToString("N")[..8];
        var master = "Server=.\\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True";
        var isolated = $"Server=.\\SQLEXPRESS;Database={dbName};Integrated Security=True;TrustServerCertificate=True";
        await Execute(master, $"CREATE DATABASE [{dbName}]");
        try
        {
            await Execute(isolated, """
                CREATE TABLE dbo.NguoiDung (MaNguoiDung int NOT NULL PRIMARY KEY);
                INSERT dbo.NguoiDung VALUES (1);
                CREATE TABLE dbo.KhoaHoc (MaKhoaHoc int NOT NULL PRIMARY KEY, TenKhoaHoc nvarchar(128) NOT NULL,
                    TrangThai varchar(32) NOT NULL);
                INSERT dbo.KhoaHoc VALUES (1,N'Isolated','DANG_MO');
                CREATE TABLE dbo.UnitKhoaHoc (MaUnit int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    MaKhoaHoc int NOT NULL, TenUnit nvarchar(128) NOT NULL, MoTa nvarchar(512) NULL, ThuTu int NOT NULL,
                    CONSTRAINT UQ_UnitKhoaHoc_ThuTu UNIQUE (MaKhoaHoc,ThuTu));
                CREATE TABLE dbo.LessonKhoaHoc (MaLesson int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    MaUnit int NOT NULL, TenLesson nvarchar(128) NOT NULL, MoTa nvarchar(512) NULL, ThuTu int NOT NULL,
                    CONSTRAINT UQ_LessonKhoaHoc_ThuTu UNIQUE (MaUnit,ThuTu));
                CREATE TABLE dbo.DanhMucBaiGiang (MaDanhMuc int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenDanhMuc nvarchar(64) NOT NULL, MoTa nvarchar(512) NULL, TrangThai varchar(32) NOT NULL);
                CREATE TABLE dbo.BaiGiang (MaBaiGiang int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    MaDanhMuc int NULL, CapDo int NULL, TieuDe nvarchar(128) NOT NULL, MaPart int NULL,
                    NoiDungTomTat nvarchar(512) NULL, NoiDung nvarchar(max) NULL, MaAdminNoiDung int NULL,
                    NgayTao datetime NOT NULL, TrangThai varchar(32) NOT NULL);
                CREATE TABLE dbo.BuocLoTrinh (MaBuoc int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    MaLesson int NOT NULL, TieuDe nvarchar(128) NOT NULL, ThuTu int NOT NULL,
                    MaBaiGiang int NULL, MaDeThi int NULL, MaChuDe int NULL,
                    CONSTRAINT UQ_BuocLoTrinh_ThuTu UNIQUE (MaLesson,ThuTu));
                """);
            var proposal = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "docs", "sql", "admin-content-idempotency-proposal.sql"));
            foreach (var batch in Regex.Split(await File.ReadAllTextAsync(proposal), @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) await Execute(isolated, batch);

            var first = await CreateUnit(isolated, "same-key-2026", "Unit A");
            var replay = await CreateUnit(isolated, "same-key-2026", "Unit A");
            Assert.Equal(201, Assert.IsType<CreatedResult>(first).StatusCode);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Assert.IsType<CreatedResult>(first).Value),
                System.Text.Json.JsonSerializer.Serialize(Assert.IsType<CreatedResult>(replay).Value));
            var mismatch = await CreateUnit(isolated, "same-key-2026", "Unit B");
            Assert.IsType<ConflictObjectResult>(mismatch);
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.UnitKhoaHoc"));

            var concurrent = await Task.WhenAll(CreateUnit(isolated, "concurrent-2026", "Unit C"),
                CreateUnit(isolated, "concurrent-2026", "Unit C"));
            Assert.All(concurrent, r => Assert.True(r is CreatedResult or ConflictObjectResult));
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.UnitKhoaHoc"));
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='COMPLETED'"));

            var lesson = new ContentNameRequest("Lesson A", null);
            var lessonResult = await CreateOther(isolated, "CREATE_LESSON", "/api/admin/content/courses/1/units/1/lessons",
                "lesson-key-2026", lesson, (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddLesson(1, 1, lesson, default));
            Assert.IsType<CreatedResult>(lessonResult);
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_LESSON", "/api/admin/content/courses/1/units/1/lessons",
                "lesson-key-2026", lesson, (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddLesson(1, 1, lesson, default)));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.LessonKhoaHoc"));

            var category = new LectureCategoryRequest("Category A", null, "DANG_MO");
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_LECTURE_CATEGORY", "/api/admin/content/lectures/categories",
                "category-key-2026", category, (db, ctx) => new AdminLecturesController(db,
                    IsolatedStorage(), NullLogger<AdminLecturesController>.Instance) { ControllerContext = new ControllerContext(ctx) }
                    .AddCategory(category, default)));
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_LECTURE_CATEGORY", "/api/admin/content/lectures/categories",
                "category-key-2026", category, (db, ctx) => new AdminLecturesController(db,
                    IsolatedStorage(), NullLogger<AdminLecturesController>.Instance) { ControllerContext = new ControllerContext(ctx) }
                    .AddCategory(category, default)));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.DanhMucBaiGiang"));

            var lecture = new LectureRequest("Lecture A", null, "Text", null, null, null, "DANG_MO");
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_LECTURE", "/api/admin/content/lectures",
                "lecture-key-2026", lecture, (db, ctx) => new AdminLecturesController(db,
                    IsolatedStorage(), NullLogger<AdminLecturesController>.Instance) { ControllerContext = new ControllerContext(ctx) }
                    .Add(lecture, default)));
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_LECTURE", "/api/admin/content/lectures",
                "lecture-key-2026", lecture, (db, ctx) => new AdminLecturesController(db,
                    IsolatedStorage(), NullLogger<AdminLecturesController>.Instance) { ControllerContext = new ControllerContext(ctx) }
                    .Add(lecture, default)));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.BaiGiang"));

            var step = new ContentStepRequest("Step A", "LECTURE", 1);
            var stepPath = "/api/admin/content/courses/1/units/1/lessons/1/steps";
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_STEP", stepPath, "step-key-2026", step,
                (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddStep(1, 1, 1, step, default)));
            Assert.IsType<CreatedResult>(await CreateOther(isolated, "CREATE_STEP", stepPath, "step-key-2026", step,
                (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddStep(1, 1, 1, step, default)));
            Assert.Equal(1, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.BuocLoTrinh"));
            Assert.Equal(6, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='COMPLETED'"));

            var lessonB = new ContentNameRequest("Lesson B", null);
            var parallelLessons = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => CreateOther(isolated,
                "CREATE_LESSON", "/api/admin/content/courses/1/units/1/lessons", "lesson-parallel-2026", lessonB,
                (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddLesson(1, 1, lessonB, default))));
            Assert.Contains(parallelLessons, x => x is CreatedResult);
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.LessonKhoaHoc"));

            var categoryB = new LectureCategoryRequest("Category B", null, "DANG_MO");
            var parallelCategories = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => CreateOther(isolated,
                "CREATE_LECTURE_CATEGORY", "/api/admin/content/lectures/categories", "category-parallel-2026", categoryB,
                (db, ctx) => new AdminLecturesController(db, IsolatedStorage(), NullLogger<AdminLecturesController>.Instance)
                    { ControllerContext = new ControllerContext(ctx) }.AddCategory(categoryB, default))));
            Assert.Contains(parallelCategories, x => x is CreatedResult);
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.DanhMucBaiGiang"));

            var lectureB = new LectureRequest("Lecture B", null, "Text", null, null, null, "DANG_MO");
            var parallelLectures = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => CreateOther(isolated,
                "CREATE_LECTURE", "/api/admin/content/lectures", "lecture-parallel-2026", lectureB,
                (db, ctx) => new AdminLecturesController(db, IsolatedStorage(), NullLogger<AdminLecturesController>.Instance)
                    { ControllerContext = new ControllerContext(ctx) }.Add(lectureB, default))));
            Assert.Contains(parallelLectures, x => x is CreatedResult);
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.BaiGiang"));

            var stepB = new ContentStepRequest("Step B", "LECTURE", 1);
            var parallelSteps = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => CreateOther(isolated,
                "CREATE_STEP", stepPath, "step-parallel-2026", stepB,
                (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                    .AddStep(1, 1, 1, stepB, default))));
            Assert.Contains(parallelSteps, x => x is CreatedResult);
            Assert.Equal(2, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.BuocLoTrinh"));
            Assert.Equal(10, await Scalar(isolated, "SELECT COUNT(*) FROM dbo.AdminContentIdempotency WHERE TrangThai='COMPLETED'"));
        }
        finally
        {
            await Execute(master, $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]");
        }
    }

    private static async Task<IActionResult?> CreateUnit(string connection, string key, string name)
    {
        var request = new ContentNameRequest(name, null);
        return await CreateOther(connection, "CREATE_UNIT", "/api/admin/content/courses/1/units", key,
            request, (db, ctx) => new AdminCurriculumController(db) { ControllerContext = new ControllerContext(ctx) }
                .AddUnit(1, request, default));
    }

    private static LectureResourceStorage IsolatedStorage() => new(new BlobServiceClient("UseDevelopmentStorage=true"),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["LectureResources:ContainerName"] = "isolated-idempotency"
        }).Build());

    private static async Task<IActionResult?> CreateOther(string connection, string operation, string path,
        string key, object request, Func<ToeicDbContext, ActionContext, Task<IActionResult>> execute)
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlServer(connection).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AdminContent:EnableIdempotency"] = "true",
            ["LectureResources:ContainerName"] = "isolated-idempotency"
        }).Build();
        var filter = new AdminContentIdempotencyFilter(operation, db, IsolatedStorage(), configuration,
            NullLogger<AdminContentIdempotencyFilter>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = path;
        http.Request.Headers["Idempotency-Key"] = key;
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "ADMIN_QUAN_LY")], "test"));
        var action = new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(action, filters,
            new Dictionary<string, object?> { ["request"] = request }, new object());
        await filter.OnActionExecutionAsync(executing, async () => new ActionExecutedContext(action, filters, new object()) {
            Result = await execute(db, action)
        });
        return executing.Result;
    }

    private static async Task<IActionResult?> CreateExcel(string connection, BlobServiceClient blobs,
        string containerName, string key, byte[] bytes, bool simulateCrashAfterBlob = false)
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>().UseSqlServer(connection).Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AdminContent:EnableIdempotency"] = "true", ["LectureResources:EnableExcel"] = "true",
            ["LectureResources:ContainerName"] = containerName
        }).Build();
        var storage = new LectureResourceStorage(blobs, config);
        var filter = new AdminContentIdempotencyFilter("CREATE_LECTURE_RESOURCE", db, storage, config,
            NullLogger<AdminContentIdempotencyFilter>.Instance);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "study.xlsx") {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
        var upload = new LectureUploadRequest { Name = "Study Excel", Kind = "EXCEL", File = file };
        var http = new DefaultHttpContext();
        http.Request.Method = "POST"; http.Request.Path = "/api/admin/content/lectures/10/resources";
        http.Request.Headers["Idempotency-Key"] = key;
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "ADMIN_QUAN_LY")], "test"));
        var action = new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        var controller = new AdminLecturesController(db, storage, NullLogger<AdminLecturesController>.Instance) {
            ControllerContext = new ControllerContext(action)
        };
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(action, filters,
            new Dictionary<string, object?> { ["id"] = 10, ["request"] = upload }, controller);
        await filter.OnActionExecutionAsync(executing, async () => {
            if (simulateCrashAfterBlob)
            {
                var reserved = (string)http.Items["LectureResourceReservedPath"]!;
                await storage.UploadAsync(new MemoryStream(bytes), ".xlsx", file.ContentType,
                    file.FileName, default, reserved);
                throw new IOException("Simulated process failure after Blob upload");
            }
            return new ActionExecutedContext(action, filters, controller) {
                Result = await controller.Upload(10, upload, default)
            };
        });
        return executing.Result;
    }

    private static byte[] ExcelBytes()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) writer.Write("<Types/>");
            using (var writer = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open())) writer.Write("<workbook/>");
        }
        return output.ToArray();
    }

    private static async Task Execute(string connection, string sql)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        await using var command = new SqlCommand(sql, db) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> Scalar(string connection, string sql)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        await using var command = new SqlCommand(sql, db);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> Text(string connection, string sql)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        await using var command = new SqlCommand(sql, db);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> BlobCount(Azure.Storage.Blobs.BlobContainerClient container)
    {
        var count = 0;
        await foreach (var _ in container.GetBlobsAsync()) count++;
        return count;
    }
}
