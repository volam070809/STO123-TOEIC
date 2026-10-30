using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Models;
using STO123.Services.Auth;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminLearnersControllerTests
{
    [Fact]
    public void Controller_requires_management_admin_policy()
    {
        var authorize = Assert.Single(typeof(AdminLearnersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
        Assert.Equal("ADMIN_QUAN_LY", AdminAuthorization.ManagementRole);
    }

    [Fact]
    public void Mutation_controller_requires_management_admin_policy()
    {
        var authorize = Assert.Single(typeof(AdminLearnerMutationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
    }

    [Fact]
    public void Analytics_controller_requires_management_admin_policy()
    {
        var authorize = Assert.Single(typeof(AdminLearnerAnalyticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
    }

    [Theory]
    [InlineData(1, 3, 33.3)]
    [InlineData(0, 0, null)]
    [InlineData(3, 3, 100d)]
    public void Learner_analytics_accuracy_uses_total_question_occurrences(
        int correct, int total, double? expected)
    {
        var actual = AdminLearnerAnalyticsMath.Accuracy(correct, total);
        Assert.Equal(expected.HasValue ? (decimal?)expected.Value : null, actual);
    }

    [Fact]
    public async Task Practice_analytics_keeps_wrong_unanswered_and_mixed_statuses_distinct()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, VaiTro TEXT NOT NULL);
            CREATE TABLE KetQuaLamBai (
                MaKetQua INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                NgayLamBai TEXT NOT NULL, NgayNopBai TEXT NULL, LoaiBaiLam TEXT NOT NULL);
            CREATE TABLE PartTOEIC (MaPart INTEGER PRIMARY KEY, SoPart INTEGER NOT NULL, SoCauChuan INTEGER NOT NULL);
            CREATE TABLE CauHoiLuotLam (
                MaCauHoiLuotLam INTEGER PRIMARY KEY, MaKetQua INTEGER NOT NULL, MaPart INTEGER NOT NULL,
                ThuTu INTEGER NOT NULL, PhuongAnDung TEXT NOT NULL);
            CREATE TABLE ChiTietKetQua (
                MaCauHoiLuotLam INTEGER PRIMARY KEY, DapAnChon TEXT NULL, DanhDau INTEGER NOT NULL DEFAULT 0);
            INSERT INTO NguoiDung VALUES (1, 'HOC_VIEN');
            INSERT INTO PartTOEIC VALUES (1, 1, 6), (2, 2, 25);
            INSERT INTO KetQuaLamBai VALUES
                (10, 1, 'DANG_LAM', '2026-10-20', NULL, 'PRACTICE'),
                (11, 1, 'DA_NOP', '2026-10-21', '2026-10-21', 'PRACTICE');
            INSERT INTO CauHoiLuotLam VALUES
                (101, 10, 1, 1, 'A'), (102, 10, 1, 2, 'A'), (103, 10, 1, 3, 'C'),
                (104, 11, 2, 1, 'B');
            INSERT INTO ChiTietKetQua VALUES (101, 'A', 0), (102, 'B', 0), (104, 'B', 0);
            """);

        var controller = new AdminLearnerAnalyticsController(db, TimeProvider.System);
        var action = await controller.Practice(1, 1, 10, null, CancellationToken.None);
        var payload = Assert.IsType<AdminLearnerPracticeAnalyticsDto>(
            Assert.IsType<OkObjectResult>(action).Value);

        var part1 = payload.Parts.Single(item => item.Part == 1);
        Assert.Equal(1, part1.TotalAttempts);
        Assert.Equal(0, part1.CompletedAttempts);
        Assert.Equal(1, part1.Correct);
        Assert.Equal(1, part1.Incorrect);
        Assert.Equal(1, part1.Unanswered);
        Assert.Equal(33.3m, part1.Accuracy);
        Assert.Equal(2, payload.TotalItems);
    }

    [Fact]
    public async Task Overview_and_vocabulary_return_truthful_empty_states()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (
                MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL, Email TEXT NOT NULL,
                SoDienThoai TEXT NULL, VaiTro TEXT NOT NULL, TrangThai TEXT NOT NULL,
                NgayTao TEXT NOT NULL, LanDangNhapCuoi TEXT NULL);
            CREATE TABLE ChuDe (MaChuDe INTEGER PRIMARY KEY, TenChuDe TEXT NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE TuVung (MaTuVung INTEGER PRIMARY KEY);
            CREATE TABLE TuVungChuDe (MaTuVung INTEGER NOT NULL, MaChuDe INTEGER NOT NULL,
                PRIMARY KEY (MaChuDe, MaTuVung));
            CREATE TABLE TienDoTuVung (
                MaNguoiDung INTEGER NOT NULL, MaTuVung INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                CapNhatLuc TEXT NOT NULL, PRIMARY KEY (MaNguoiDung, MaTuVung));
            CREATE TABLE KetQuaLamBai (
                MaKetQua INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                NgayLamBai TEXT NOT NULL, NgayNopBai TEXT NULL, LoaiBaiLam TEXT NOT NULL);
            CREATE TABLE LanLuyenTuVung (
                MaLanLuyen INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL, MaChuDe INTEGER NOT NULL,
                TrangThai TEXT NOT NULL, NgayBatDau TEXT NOT NULL, NgayNopBai TEXT NULL);
            CREATE TABLE ChiTietLuyenTuVung (
                MaLanLuyen INTEGER NOT NULL, MaChuDe INTEGER NOT NULL, MaTuVung INTEGER NOT NULL, LaDung INTEGER NULL,
                PRIMARY KEY (MaLanLuyen, MaTuVung));
            CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL);
            CREATE TABLE TienDoBuocLoTrinh (
                MaTienDoBLT INTEGER NOT NULL, MaNguoiDung INTEGER NOT NULL, MaDangKy INTEGER NOT NULL, TrangThai TEXT NOT NULL,
                CapNhatLuc TEXT NOT NULL);
            INSERT INTO NguoiDung VALUES
                (1, 'Learner Empty', 'empty@example.test', NULL, 'HOC_VIEN', 'HOAT_DONG', '2026-01-01', NULL);
            """);

        var controller = new AdminLearnerAnalyticsController(db, TimeProvider.System);
        var overviewAction = await controller.Overview(1, CancellationToken.None);
        var overview = Assert.IsType<AdminLearnerOverviewDto>(Assert.IsType<OkObjectResult>(overviewAction).Value);
        Assert.Equal(0, overview.UniqueRememberedWords);
        Assert.Equal(0, overview.PracticeAttemptCount);
        Assert.Null(overview.LastLearningAt);
        Assert.Empty(overview.RecentActivity);

        var vocabularyAction = await controller.Vocabulary(1, CancellationToken.None);
        var vocabulary = Assert.IsType<AdminLearnerVocabularyAnalyticsDto>(
            Assert.IsType<OkObjectResult>(vocabularyAction).Value);
        Assert.Equal(0, vocabulary.UniqueStartedWords);
        Assert.Empty(vocabulary.Topics);
    }

    [Fact]
    public async Task Exam_analytics_separates_mock_results_from_empty_placement_history()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, VaiTro TEXT NOT NULL);
            CREATE TABLE DeThi (MaDeThi INTEGER PRIMARY KEY, TenDe TEXT NOT NULL);
            CREATE TABLE KetQuaLamBai (
                MaKetQua INTEGER PRIMARY KEY, MaDeThi INTEGER NULL, MaHocVien INTEGER NOT NULL,
                TrangThai TEXT NOT NULL, DiemNghe INTEGER NULL, DiemDoc INTEGER NULL,
                DiemTong INTEGER NULL, NgayLamBai TEXT NOT NULL, NgayNopBai TEXT NULL,
                LoaiBaiLam TEXT NOT NULL);
            CREATE TABLE PartTOEIC (MaPart INTEGER PRIMARY KEY, SoPart INTEGER NOT NULL, SoCauChuan INTEGER NOT NULL);
            CREATE TABLE CauHoiLuotLam (
                MaCauHoiLuotLam INTEGER PRIMARY KEY, MaKetQua INTEGER NOT NULL, MaPart INTEGER NOT NULL,
                ThuTu INTEGER NOT NULL, PhuongAnDung TEXT NOT NULL);
            CREATE TABLE ChiTietKetQua (
                MaCauHoiLuotLam INTEGER PRIMARY KEY, DapAnChon TEXT NULL, DanhDau INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE KetQuaPhanLopKNN (
                MaPhanLop INTEGER PRIMARY KEY, MaKetQua INTEGER NOT NULL, GiaiDoanDeXuat INTEGER NOT NULL,
                DiemMucTieu INTEGER NULL, NgayPhanLop TEXT NOT NULL);
            INSERT INTO NguoiDung VALUES (1, 'HOC_VIEN');
            INSERT INTO DeThi VALUES (5, 'Mock số 1');
            INSERT INTO PartTOEIC VALUES (1, 1, 6);
            INSERT INTO KetQuaLamBai VALUES
                (20, 5, 1, 'DA_NOP', 330, 320, 650, '2026-10-20', '2026-10-20', 'MOCK');
            INSERT INTO CauHoiLuotLam VALUES (201, 20, 1, 1, 'A'), (202, 20, 1, 2, 'B');
            INSERT INTO ChiTietKetQua VALUES (201, 'A', 0), (202, 'C', 0);
            """);

        var controller = new AdminLearnerAnalyticsController(db, TimeProvider.System);
        var action = await controller.Exams(1, 1, 1, 10, CancellationToken.None);
        var payload = Assert.IsType<AdminLearnerExamAnalyticsDto>(Assert.IsType<OkObjectResult>(action).Value);

        Assert.Equal(1, payload.MockTests.Summary.CompletedAttempts);
        Assert.Equal(1, payload.MockTests.Summary.Correct);
        Assert.Equal(1, payload.MockTests.Summary.Incorrect);
        Assert.Equal(50m, payload.MockTests.Summary.Accuracy);
        Assert.Equal(650, payload.MockTests.Summary.LatestEstimatedScore);
        Assert.Empty(payload.PlacementTests.Items);
    }

    [Fact]
    public void Update_contract_rejects_role_email_and_status_fields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminUpdateLearnerRequest>(
            """{"fullName":"Learner","phoneNumber":null,"role":"ADMIN_QUAN_LY"}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminUpdateLearnerRequest>(
            """{"fullName":"Learner","phoneNumber":null,"email":"other@example.test"}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminUpdateLearnerRequest>(
            """{"fullName":"Learner","phoneNumber":null,"status":"HOAT_DONG"}""", options));
    }

    [Fact]
    public void Create_contract_rejects_caller_supplied_role_or_status()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminCreateLearnerRequest>(
            """{"fullName":"Learner","email":"new@example.test","password":"Password1","role":"ADMIN_QUAN_LY"}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminCreateLearnerRequest>(
            """{"fullName":"Learner","email":"new@example.test","password":"Password1","status":"HOAT_DONG"}""", options));
    }

    [Fact]
    public async Task List_filters_learners_and_excludes_admin_accounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NguoiDung (
                MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL, Email TEXT NOT NULL,
                SoDienThoai TEXT NULL, AnhDaiDien TEXT NULL, VaiTro TEXT NOT NULL,
                TrangThai TEXT NOT NULL, NgayTao TEXT NOT NULL, LanDangNhapCuoi TEXT NULL);
            CREATE TABLE DangKyGoiHoc (
                MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL, MaGoiHoc INTEGER NULL,
                GiaDangKy TEXT NOT NULL, SoNgaySuDung INTEGER NOT NULL, NgayDangKy TEXT NOT NULL,
                NgayBatDau TEXT NULL, NgayKetThuc TEXT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE KetQuaLamBai (
                MaKetQua INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL, NgayLamBai TEXT NOT NULL);
            INSERT INTO NguoiDung VALUES
                (1, 'Nguyen An', 'an@example.test', '0901000001', NULL, 'HOC_VIEN', 'HOAT_DONG', '2026-01-01 00:00:00', NULL),
                (2, 'Tran Binh', 'binh@example.test', NULL, NULL, 'HOC_VIEN', 'BI_KHOA', '2026-01-02 00:00:00', NULL),
                (3, 'Admin', 'admin@example.test', NULL, NULL, 'ADMIN_QUAN_LY', 'HOAT_DONG', '2026-01-03 00:00:00', NULL);
            INSERT INTO DangKyGoiHoc VALUES
                (10, 1, NULL, '100000', 90, '2026-02-01', '2026-02-01', '2026-05-01', 'DANG_SU_DUNG');
            INSERT INTO KetQuaLamBai VALUES (20, 1, '2026-02-03');
            """);

        var action = await new AdminLearnersController(db).List(new AdminLearnerListQuery
        {
            Search = "an@example",
            Status = "HOAT_DONG",
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 1),
            Page = 1,
            PageSize = 20
        }, CancellationToken.None);

        var response = Assert.IsType<AdminPagedLearnersDto>(Assert.IsType<OkObjectResult>(action).Value);
        var learner = Assert.Single(response.Items);
        Assert.Equal(1, response.TotalItems);
        Assert.Equal(1, learner.LearnerId);
        Assert.Equal(1, learner.ActiveEnrollmentCount);
        Assert.Equal(1, learner.AttemptCount);
    }

    [Fact]
    public async Task List_rejects_reversed_date_range_before_database_access()
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var action = await new AdminLearnersController(db).List(new AdminLearnerListQuery
        {
            DateFrom = new DateTime(2026, 10, 18),
            DateTo = new DateTime(2026, 10, 17)
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Theory]
    [InlineData(0, 20, null)]
    [InlineData(1000001, 20, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 20, "UNKNOWN")]
    public async Task List_rejects_invalid_filters(int page, int pageSize, string? status)
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var action = await new AdminLearnersController(db).List(new AdminLearnerListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
    }
}
