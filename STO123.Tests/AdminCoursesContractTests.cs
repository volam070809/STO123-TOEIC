using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.DTOs.Admin;
using STO123.Services.Admin;
using STO123.Services.Auth;

namespace STO123.Tests;

public sealed class AdminCoursesContractTests
{
    [Fact]
    public void Read_controller_uses_course_read_policy()
    {
        var authorize = Assert.Single(typeof(AdminCoursesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAuthorization.CourseReadPolicy, authorize.Policy);
        Assert.Equal("ADMIN_QUAN_LY", AdminAuthorization.ManagementRole);
        Assert.Equal("ADMIN_NOI_DUNG", AdminAuthorization.ContentRole);
    }

    [Fact]
    public void Mutation_controller_requires_management_policy()
    {
        var authorize = Assert.Single(typeof(AdminCourseMutationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAuthorization.ManagementPolicy, authorize.Policy);
    }

    [Fact]
    public void Write_contract_rejects_relationship_and_owner_fields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string valid = """{"name":"TOEIC 1","stage":1,"maxTargetScore":450,"retailPrice":99000,"description":null,"status":"DANG_MO","coverImageUrl":null""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminCourseWriteRequest>(valid + ",\"packageIds\":[1]}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminCourseWriteRequest>(valid + ",\"contentAdminId\":10}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminCourseWriteRequest>(valid + ",\"units\":[]}", options));
    }

    [Fact]
    public void Image_mutations_are_multipart_and_keep_management_authorization()
    {
        foreach (var actionName in new[] { "CreateWithImage", "UpdateWithImage" })
        {
            var action = typeof(AdminCourseMutationsController).GetMethod(actionName);
            Assert.NotNull(action);
            var consumes = Assert.Single(action!.GetCustomAttributes(typeof(ConsumesAttribute), true)
                .Cast<ConsumesAttribute>());
            Assert.Contains("multipart/form-data", consumes.ContentTypes);
        }
    }

    [Theory]
    [InlineData("/api/public/course-images/0123456789abcdef0123456789abcdef.jpg", "course-images/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("/api/public/course-images/ABCDEF0123456789ABCDEF0123456789.webp", "course-images/ABCDEF0123456789ABCDEF0123456789.webp")]
    public void Course_image_reference_accepts_only_owned_guid_paths(string path, string expected)
    {
        Assert.True(CourseImageStorage.TryReference(path, out var reference));
        Assert.Equal(expected, reference);
    }

    [Theory]
    [InlineData("/images/home/foundation.jpg")]
    [InlineData("https://cdn.example.test/course.jpg")]
    [InlineData("/api/public/course-images/../../avatars/user.jpg")]
    [InlineData("/api/public/course-images/not-a-guid.jpg")]
    public void Course_image_reference_rejects_library_external_and_unsafe_paths(string path) =>
        Assert.False(CourseImageStorage.TryReference(path, out _));

    [Fact]
    public void Course_stage_is_not_unique_in_the_application_model()
    {
        var options = new DbContextOptionsBuilder<STO123.Models.ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        using var db = new STO123.Models.ToeicDbContext(options);
        var stage = db.Model.FindEntityType(typeof(STO123.Models.KhoaHoc))!
            .FindProperty(nameof(STO123.Models.KhoaHoc.GiaiDoan))!;

        Assert.DoesNotContain(stage.GetContainingIndexes(), index => index.IsUnique);
    }

    [Fact]
    public async Task Application_model_allows_create_and_edit_with_a_shared_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<STO123.Models.ToeicDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new STO123.Models.ToeicDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE KhoaHoc (
                MaKhoaHoc INTEGER PRIMARY KEY AUTOINCREMENT,
                TenKhoaHoc TEXT NOT NULL,
                GiaiDoan INTEGER NOT NULL,
                DiemMucTieuToiDa INTEGER NOT NULL,
                GiaBanLe TEXT NULL,
                MoTa TEXT NULL,
                TrangThai TEXT NOT NULL,
                MaAdminNoiDung INTEGER NULL,
                NgayTao TEXT NOT NULL,
                DuongDanAnhDaiDien TEXT NULL
            );
            """);
        var first = Course("Khóa nền tảng", 1);
        var second = Course("Khóa tăng cường", 1);

        db.KhoaHoc.AddRange(first, second);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.KhoaHoc.CountAsync(course => course.GiaiDoan == 1));

        second.GiaiDoan = 2;
        await db.SaveChangesAsync();
        first.GiaiDoan = 2;
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.KhoaHoc.CountAsync(course => course.GiaiDoan == 2));
    }

    private static STO123.Models.KhoaHoc Course(string name, byte stage) => new()
    {
        TenKhoaHoc = name,
        GiaiDoan = stage,
        DiemMucTieuToiDa = 450,
        GiaBanLe = 99_000,
        TrangThai = "DANG_MO",
        NgayTao = DateTime.Now
    };
}
