using System.Buffers.Binary;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class LectureBlobIsolatedTests
{
    [Theory]
    [InlineData("Bài giảng TOEIC.pdf")]
    [InlineData("Đề thi.TOEIC.Final.PDF")]
    [InlineData("bài giảng TOEIC.pdf")]
    public void Original_unicode_and_multi_dot_filenames_are_accepted(string name)
    {
        Assert.True(LectureResourceStorage.SafeFileName(name));
    }

    [Theory]
    [InlineData("../Bài giảng.pdf")]
    [InlineData("thư mục\\Bài giảng.pdf")]
    [InlineData("Bài giảng.pdf.")]
    public void Traversal_or_unsafe_filenames_are_rejected(string name)
    {
        Assert.False(LectureResourceStorage.SafeFileName(name));
    }

    [Fact]
    public void Blob_path_preserves_original_name_and_isolates_duplicate_uploads()
    {
        var storage = new LectureResourceStorage(new BlobServiceClient("UseDevelopmentStorage=true"),
            new ConfigurationBuilder().Build());
        const string name = "Bài giảng TOEIC.Final.PDF";
        var first = storage.NewPath(name);
        var second = storage.NewPath(name);
        Assert.NotEqual(first, second);
        Assert.EndsWith("/" + name, first, StringComparison.Ordinal);
        Assert.True(LectureResourceStorage.Owned(first));
        Assert.Equal(name, LectureResourceStorage.DownloadName(name, first));
        Assert.True(LectureResourceStorage.Owned("lecture-resources/0123456789abcdef0123456789abcdef.pdf"));
    }

    [Fact]
    public void Course_resource_path_uses_stable_ids_and_original_unicode_name()
    {
        const string name = "Bài giảng TOEIC.pdf";
        var path = LectureResourceStorage.CoursePath(1, 2, 5, 12, 101, name);
        Assert.Equal("courses/course-1/unit-2/lesson-5/activity-12/resource-101/" + name, path);
        Assert.True(LectureResourceStorage.Owned(path));
        Assert.Equal(path, LectureResourceStorage.FromReservationPath(LectureResourceStorage.ReservationPath(path)));
        Assert.StartsWith("lecture-resources/courses/course-1/", LectureResourceStorage.ReservationPath(path), StringComparison.Ordinal);
        Assert.Equal(name, LectureResourceStorage.DownloadName(name, path));
        Assert.False(LectureResourceStorage.Owned("courses/course-1/unit-2/lesson-5/activity-12/resource-0/" + name));
        Assert.False(LectureResourceStorage.Owned("courses/course-1/unit-2/lesson-5/activity-12/resource-101/../x.pdf"));
    }

    [Fact]
    public async Task Large_mp4_upload_and_seek_use_private_isolated_blob()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_AZURITE_TESTS") != "1") return;
        var containerName = "sto123-step1-" + Guid.NewGuid().ToString("N");
        var blobs = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = blobs.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        var path = Path.GetTempFileName();
        try
        {
            const int length = 32 * 1024 * 1024;
            await using (var media = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            {
                media.SetLength(length);
                var header = new byte[32];
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 16);
                "ftyp"u8.CopyTo(header.AsSpan(4, 4));
                "isom"u8.CopyTo(header.AsSpan(8, 4));
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), 8);
                "moov"u8.CopyTo(header.AsSpan(20, 4));
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24, 4), length - 24);
                "mdat"u8.CopyTo(header.AsSpan(28, 4));
                await media.WriteAsync(header);
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["LectureResources:ContainerName"] = containerName
            }).Build();
            var storage = new LectureResourceStorage(blobs, config);
            await using var upload = new FileStream(path, FileMode.Open, FileAccess.Read);
            Assert.True(LectureResourceStorage.TryValidate("VIDEO", "lesson.mp4", "video/mp4", upload,
                out var extension, out var contentType));
            var blobPath = await storage.UploadAsync(upload, extension, contentType, "lesson.mp4", default);
            Assert.True(LectureResourceStorage.Owned(blobPath));
            var opened = await storage.OpenAsync(blobPath, default);
            Assert.NotNull(opened);
            Assert.Equal("video/mp4", opened.Value.ContentType);
            await using (var content = opened.Value.Stream)
            {
                Assert.True(content.CanSeek);
                content.Seek(length - 16, SeekOrigin.Begin);
                var last = new byte[16];
                await content.ReadExactlyAsync(last);
                Assert.Equal(new byte[16], last);
            }
            await storage.DeleteAsync(blobPath, default);
            Assert.False(await container.GetBlobClient(blobPath).ExistsAsync());
        }
        finally
        {
            File.Delete(path);
            await container.DeleteIfExistsAsync();
        }
    }
}
