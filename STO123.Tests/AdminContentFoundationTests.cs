using System.Reflection;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using STO123.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using STO123.Controllers;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminContentFoundationTests
{
    [Fact]
    public void Authoring_routes_require_admin_and_learning_requires_learner()
    {
        var curriculum = Assert.Single(typeof(AdminCurriculumController).GetCustomAttributes<AuthorizeAttribute>());
        var lectures = Assert.Single(typeof(AdminLecturesController).GetCustomAttributes<AuthorizeAttribute>());
        var learning = Assert.Single(typeof(CourseLearningController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("ADMIN_NOI_DUNG,ADMIN_QUAN_LY", curriculum.Roles);
        Assert.Equal(curriculum.Roles, lectures.Roles);
        Assert.Equal("HOC_VIEN", learning.Roles);
    }

    [Fact]
    public void All_six_creation_actions_activate_idempotency_filter()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["LectureResources:ContainerName"] = "isolated-filter-check"
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new BlobServiceClient("UseDevelopmentStorage=true"))
            .AddSingleton<LectureResourceStorage>()
            .AddDbContext<ToeicDbContext>(options => options.UseSqlServer(
                "Server=.\\SQLEXPRESS;Database=unused;Integrated Security=True;TrustServerCertificate=True"))
            .BuildServiceProvider();
        foreach (var (controller, action) in new[] {
            (typeof(AdminCurriculumController), nameof(AdminCurriculumController.AddUnit)),
            (typeof(AdminCurriculumController), nameof(AdminCurriculumController.AddLesson)),
            (typeof(AdminCurriculumController), nameof(AdminCurriculumController.AddStep)),
            (typeof(AdminLecturesController), nameof(AdminLecturesController.AddCategory)),
            (typeof(AdminLecturesController), nameof(AdminLecturesController.Add)),
            (typeof(AdminLecturesController), nameof(AdminLecturesController.Upload))
        })
        {
            var attribute = Assert.Single(controller.GetMethod(action)!.GetCustomAttributes<TypeFilterAttribute>());
            using var scope = services.CreateScope();
            Assert.IsType<AdminContentIdempotencyFilter>(attribute.CreateInstance(scope.ServiceProvider));
        }
    }

    [Fact]
    public void Resource_validator_rejects_mismatched_signature_and_media_type()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 73, 69, 78, 68, 0, 0, 0, 0 };
        Assert.True(LectureResourceStorage.TryValidate("HINH_ANH", "image.png", "image/png", png,
            out _, out _));
        Assert.False(LectureResourceStorage.TryValidate("PDF", "fake.pdf", "application/pdf", png,
            out _, out _));
        Assert.False(LectureResourceStorage.TryValidate("HINH_ANH", "image.png", "image/jpeg", png,
            out _, out _));
        Assert.False(LectureResourceStorage.TryValidate("AUDIO", "short.wav", "audio/wav", "RIFF1234"u8.ToArray(),
            out _, out _));
        Assert.False(LectureResourceStorage.Owned("../words/shared.mp3"));
    }

    [Theory]
    [InlineData("WORD", ".docx", "word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("POWERPOINT", ".pptx", "ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("EXCEL", ".xlsx", "xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Office_package_must_match_kind_and_mime(string kind, string extension, string entry, string mime)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) writer.Write("<Types/>");
            using (var writer = new StreamWriter(zip.CreateEntry(entry).Open())) writer.Write("<document/>");
        }
        var bytes = output.ToArray();
        Assert.True(LectureResourceStorage.TryValidate(kind, "lesson" + extension, mime, bytes, out _, out _));
        Assert.False(LectureResourceStorage.TryValidate(kind, "lesson" + extension, "application/octet-stream", bytes, out _, out _));
        Assert.False(LectureResourceStorage.TryValidate(kind, "lesson" + extension, "application/x-msdownload", "MZ"u8.ToArray(), out _, out _));
    }

    [Theory]
    [InlineData("WORD", "WordDocument", ".doc", "application/msword")]
    [InlineData("POWERPOINT", "PowerPoint Document", ".ppt", "application/vnd.ms-powerpoint")]
    [InlineData("EXCEL", "Workbook", ".xls", "application/vnd.ms-excel")]
    public void Legacy_office_container_must_have_expected_document_stream(string kind, string streamName,
        string extension, string mime)
    {
        var bytes = new byte[1024];
        new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), 0xfffe);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30, 2), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(576, 2), (ushort)((streamName.Length + 1) * 2));
        bytes[578] = 2;
        Encoding.Unicode.GetBytes(streamName).CopyTo(bytes, 512);
        Assert.True(LectureResourceStorage.TryValidate(kind, "study" + extension, mime, bytes, out _, out _));
        Assert.False(LectureResourceStorage.TryValidate(kind, "study" + extension, "application/octet-stream", bytes, out _, out _));
    }

    [Fact]
    public void Pdf_audio_and_image_signatures_are_checked()
    {
        Assert.True(LectureResourceStorage.TryValidate("PDF", "study.pdf", "application/pdf",
            "%PDF-1.7\nbody\n%%EOF"u8.ToArray(), out _, out _));
        Assert.False(LectureResourceStorage.TryValidate("PDF", "study.pdf", "application/pdf",
            "%PDF-1.7\nbody\n"u8.ToArray(), out _, out _));
        Assert.True(LectureResourceStorage.TryValidate("AUDIO", "voice.mp3", "audio/mpeg",
            "ID3abcdefghijk"u8.ToArray(), out _, out _));
        Assert.True(LectureResourceStorage.TryValidate("AUDIO", "voice.wav", "audio/wav",
            "RIFFxxxxWAVEdata"u8.ToArray(), out _, out _));
        Assert.True(LectureResourceStorage.TryValidate("HINH_ANH", "slide.webp", "image/webp",
            "RIFF\b\0\0\0WEBPdata"u8.ToArray(), out _, out _));
    }

    [Fact]
    public void Video_requires_movie_and_data_boxes_and_download_keeps_extension()
    {
        var mp4 = new byte[] {
            0,0,0,16, 102,116,121,112, 105,115,111,109, 0,0,0,0,
            0,0,0,8, 109,111,111,118,
            0,0,0,8, 109,100,97,116 };
        Assert.True(LectureResourceStorage.TryValidate("VIDEO", "study.mp4", "video/mp4", mp4, out _, out _));
        Assert.False(LectureResourceStorage.TryValidate("VIDEO", "study.mp4", "video/mp4", mp4[..24], out _, out _));
        Assert.Equal("Bài học.mp4", LectureResourceStorage.DownloadName("Bài học", "lecture-resources/abc.mp4"));
        Assert.Equal("Bài học.mp4", LectureResourceStorage.DownloadName("Bài học.mp4", "lecture-resources/abc.mp4"));
        Assert.False(LectureResourceStorage.SafeFileName("../study.mp4"));
    }

    [Fact]
    public void Media_ticket_rejects_tampering_and_expiry()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Key"] = "isolated-media-ticket-signing-key-2026"
        }).Build();
        var time = new MutableTimeProvider();
        var service = new LectureMediaTicket(config, time);
        var (token, _) = service.Issue(21, 7, 8, 9);
        Assert.True(service.TryRead(token, out var grant));
        Assert.Equal(21, grant!.LearnerId);
        Assert.False(service.TryRead(token[..^1] + (token[^1] == 'A' ? 'B' : 'A'), out _));
        var (shortToken, _) = service.Issue(21, 7, 8, 9, time.GetUtcNow().AddSeconds(30));
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.False(service.TryRead(shortToken, out _));
        time.Advance(LectureMediaTicket.Lifetime.Add(TimeSpan.FromSeconds(1)));
        Assert.False(service.TryRead(token, out _));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now = now.Add(span);
    }
}
