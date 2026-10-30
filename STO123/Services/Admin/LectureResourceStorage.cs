using System.Buffers.Binary;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace STO123.Services.Admin;

public sealed class LectureResourceStorage(BlobServiceClient blobs, IConfiguration configuration)
{
    private const string Prefix = "lecture-resources/";
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(
        configuration["LectureResources:ContainerName"] ?? "course-resources");
    public long MaxFileBytes { get; } = Math.Clamp(configuration.GetValue<long?>("LectureResources:MaxFileBytes") ?? 250_000_000L,
        12L, 500_000_000L);
    public bool ExcelEnabled => configuration.GetValue<bool>("LectureResources:EnableExcel");

    public static bool SafeFileName(string? fileName) => fileName is { Length: > 0 and <= 128 } &&
        fileName == Path.GetFileName(fileName) && fileName.IndexOfAny(['/', '\\', ':']) < 0 &&
        !fileName.EndsWith('.') && !fileName.EndsWith(' ') && !fileName.StartsWith(' ') &&
        !fileName.Trim().Equals(string.Empty, StringComparison.Ordinal) &&
        !fileName.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c)) &&
        !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(Path.GetFileNameWithoutExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static bool TryValidate(string kind, string fileName, string? mime, byte[] bytes,
        out string extension, out string contentType)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return TryValidate(kind, fileName, mime, stream, out extension, out contentType);
    }

    public static bool TryValidate(string kind, string fileName, string? mime, Stream stream,
        out string extension, out string contentType)
    {
        extension = Path.GetExtension(fileName).ToLowerInvariant(); contentType = string.Empty;
        if (!SafeFileName(fileName) || !stream.CanSeek || stream.Length < 12) return false;
        stream.Position = 0;
        Span<byte> head = stackalloc byte[64]; var headLength = stream.Read(head);
        stream.Position = Math.Max(0, stream.Length - 1024);
        var tail = new byte[Math.Min(1024, (int)stream.Length)]; stream.ReadExactly(tail);
        stream.Position = 0;
        var h = head[..headLength];
        var jpg = h.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) && tail.AsSpan().EndsWith(new byte[] { 0xff, 0xd9 });
        var png = h.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            tail.Length >= 12 && tail.AsSpan()[^8..^4].SequenceEqual("IEND"u8);
        var webp = h.StartsWith("RIFF"u8) && h.Length >= 12 && h[8..12].SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(h[4..8]) == stream.Length - 8;
        var pdf = h.StartsWith("%PDF-"u8) && Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal);
        var mp3 = h.StartsWith("ID3"u8) || (h[0] == 0xff && (h[1] & 0xe0) == 0xe0);
        var wav = h.StartsWith("RIFF"u8) && h.Length >= 12 && h[8..12].SequenceEqual("WAVE"u8);
        var mp4 = ValidMp4(stream);
        var openXml = h.StartsWith(new byte[] { 0x50, 0x4b, 0x03, 0x04 }) && ValidOpenXml(kind, stream);
        var legacy = h.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }) &&
            ValidLegacyOffice(kind, stream);
        contentType = (kind, extension) switch
        {
            ("HINH_ANH", ".jpg" or ".jpeg") when jpg => "image/jpeg",
            ("HINH_ANH", ".png") when png => "image/png",
            ("HINH_ANH", ".webp") when webp => "image/webp",
            ("PDF", ".pdf") when pdf => "application/pdf",
            ("AUDIO", ".mp3") when mp3 => "audio/mpeg",
            ("AUDIO", ".wav") when wav => "audio/wav",
            ("VIDEO", ".mp4") when mp4 => "video/mp4",
            ("WORD", ".doc") when legacy => "application/msword",
            ("WORD", ".docx") when openXml => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ("POWERPOINT", ".ppt") when legacy => "application/vnd.ms-powerpoint",
            ("POWERPOINT", ".pptx") when openXml => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ("EXCEL", ".xls") when legacy => "application/vnd.ms-excel",
            ("EXCEL", ".xlsx") when openXml => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => string.Empty
        };
        stream.Position = 0;
        return contentType.Length > 0 &&
            (string.Equals(mime, contentType, StringComparison.OrdinalIgnoreCase) ||
             (contentType == "audio/mpeg" && string.Equals(mime, "audio/mp3", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ValidOpenXml(string kind, Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return zip.GetEntry("[Content_Types].xml") is not null && zip.GetEntry(kind switch {
                "WORD" => "word/document.xml", "POWERPOINT" => "ppt/presentation.xml",
                "EXCEL" => "xl/workbook.xml", _ => "__unsupported__"
            }) is not null;
        }
        catch (InvalidDataException) { return false; }
        finally { stream.Position = 0; }
    }

    private static bool ValidLegacyOffice(string kind, Stream stream)
    {
        if (stream.Length < 1024) return false;
        Span<byte> header = stackalloc byte[512]; stream.Position = 0; stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]) != 0xfffe) return false;
        var shift = BinaryPrimitives.ReadUInt16LittleEndian(header[30..32]);
        if (shift is not (9 or 12)) return false;
        var sectorSize = 1L << shift;
        var directorySector = BinaryPrimitives.ReadUInt32LittleEndian(header[48..52]);
        var offset = (directorySector + 1L) * sectorSize;
        if (offset < 512 || offset + sectorSize > stream.Length) return false;
        var entries = new byte[sectorSize]; stream.Position = offset; stream.ReadExactly(entries);
        var expected = kind switch { "WORD" => "WordDocument", "POWERPOINT" => "PowerPoint Document",
            "EXCEL" => "Workbook", _ => string.Empty };
        for (var i = 0; i < entries.Length; i += 128)
        {
            var nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(i + 64, 2));
            if (entries[i + 66] != 2 || nameBytes is < 4 or > 64 || nameBytes % 2 != 0) continue;
            var name = Encoding.Unicode.GetString(entries, i, nameBytes - 2);
            if (name == expected || (kind == "EXCEL" && name == "Book")) { stream.Position = 0; return true; }
        }
        stream.Position = 0; return false;
    }

    private static bool ValidMp4(Stream stream)
    {
        if (stream.Length < 24) return false;
        Span<byte> box = stackalloc byte[16];
        stream.Position = 0; stream.ReadExactly(box[..8]);
        var size = BinaryPrimitives.ReadUInt32BigEndian(box[..4]);
        if (!box[4..8].SequenceEqual("ftyp"u8) || size is < 16 or > 4096 || size > stream.Length)
            return false;
        stream.Position = size;
        var seenMovie = false; var seenData = false;
        for (var count = 0; count < 1000 && stream.Position + 8 <= stream.Length; count++)
        {
            stream.ReadExactly(box[..8]);
            var boxStart = stream.Position - 8;
            var movieBox = box[4..8].SequenceEqual("moov"u8);
            var dataBox = box[4..8].SequenceEqual("mdat"u8);
            var boxSize = (long)BinaryPrimitives.ReadUInt32BigEndian(box[..4]);
            if (boxSize == 1) { stream.ReadExactly(box[..8]); boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(box[..8]); }
            else if (boxSize == 0) boxSize = stream.Length - boxStart;
            if (boxSize < stream.Position - boxStart || boxStart + boxSize > stream.Length) break;
            if (movieBox) seenMovie = true;
            if (dataBox) seenData = true;
            stream.Position = boxStart + boxSize;
        }
        stream.Position = 0;
        return seenMovie && seenData;
    }

    public string NewPath(string? fileName)
    {
        if (!SafeFileName(fileName)) throw new ArgumentException("Invalid original file name.", nameof(fileName));
        return Prefix + Guid.NewGuid().ToString("N") + "/" + fileName;
    }

    public static string ActivitySlug(string title)
    {
        var source = title.Replace('\u0111', 'd').Replace('\u0110', 'D').Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder();
        foreach (var c in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            if (slug.Length >= 48) break;
        }
        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? "activity" : result;
    }

    public static string CoursePath(int courseId, int unitOrder, int lessonOrder, string activityTitle,
        int stepId, int resourceId, string originalName)
    {
        if (courseId <= 0 || unitOrder <= 0 || lessonOrder <= 0 || stepId <= 0 || resourceId <= 0 || !SafeFileName(originalName))
            throw new ArgumentException("Invalid course resource path.");
        return $"course-{courseId}/unit-{unitOrder}/lesson-{lessonOrder}/{ActivitySlug(activityTitle)}-a{stepId}-r{resourceId}/{originalName}";
    }

    public static string ReservationPath(string path) => path.StartsWith("courses/course-", StringComparison.Ordinal) ||
        path.StartsWith("course-", StringComparison.Ordinal)
        ? Prefix + path : path;

    public static string FromReservationPath(string path) => path.StartsWith(Prefix + "courses/course-", StringComparison.Ordinal) ||
        path.StartsWith(Prefix + "course-", StringComparison.Ordinal)
        ? path[Prefix.Length..] : path;

    public async Task<string> UploadAsync(Stream source, string extension, string contentType, string originalName,
        CancellationToken ct, string? reservedPath = null)
    {
        var path = reservedPath ?? NewPath(originalName);
        if (!SafeFileName(originalName) || !Owned(path) ||
            !string.Equals(path[(path.LastIndexOf('/') + 1)..], originalName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid reserved lecture Blob path.");
        source.Position = 0;
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(source,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["originalname"] =
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(originalName)) },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, ct);
        return path;
    }

    public async Task<(Stream Stream, string ContentType)?> OpenAsync(string path, CancellationToken ct)
    {
        if (!Owned(path)) return null;
        var blob = container.GetBlobClient(path);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return (await blob.OpenReadAsync(cancellationToken: ct), properties.Value.ContentType);
        }
        catch (RequestFailedException e) when (e.Status == 404) { return null; }
    }

    public Task DeleteAsync(string path, CancellationToken ct) => Owned(path)
        ? container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct)
        : Task.CompletedTask;

    public async Task CopyVerifiedAsync(string sourcePath, string targetPath, CancellationToken ct)
    {
        if (!Owned(sourcePath) || !Owned(targetPath)) throw new InvalidOperationException("Unowned course resource path.");
        if (sourcePath == targetPath) return;
        var source = container.GetBlobClient(sourcePath);
        var target = container.GetBlobClient(targetPath);
        var properties = await source.GetPropertiesAsync(cancellationToken: ct);
        byte[] expected;
        await using (var read = await source.OpenReadAsync(cancellationToken: ct))
            expected = await SHA256.HashDataAsync(read, ct);
        try
        {
            await using var read = await source.OpenReadAsync(cancellationToken: ct);
            await target.UploadAsync(read, new BlobUploadOptions {
                HttpHeaders = new BlobHttpHeaders { ContentType = properties.Value.ContentType },
                Metadata = properties.Value.Metadata,
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
        }
        catch (RequestFailedException e) when (e.Status is 409 or 412) { }
        var copied = await target.GetPropertiesAsync(cancellationToken: ct);
        if (copied.Value.ContentLength != properties.Value.ContentLength)
            throw new InvalidOperationException("Course resource copy length mismatch.");
        await using var verify = await target.OpenReadAsync(cancellationToken: ct);
        var actual = await SHA256.HashDataAsync(verify, ct);
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Course resource copy hash mismatch.");
    }

    public static bool Owned(string? path)
    {
        if (path?.StartsWith("course-", StringComparison.Ordinal) == true)
        {
            var parts = path.Split('/');
            if (parts.Length != 5 || !PositiveSegment(parts[0], "course-") ||
                !PositiveSegment(parts[1], "unit-") || !PositiveSegment(parts[2], "lesson-") ||
                !SafeFileName(parts[4])) return false;
            var activity = parts[3];
            var a = activity.LastIndexOf("-a", StringComparison.Ordinal);
            var r = activity.LastIndexOf("-r", StringComparison.Ordinal);
            return a > 0 && r > a + 2 && ActivitySlug(activity[..a]) == activity[..a] &&
                PositiveSegment(activity[(a + 1)..r], "a") &&
                PositiveSegment(activity[(r + 1)..], "r");
        }
        if (path?.StartsWith("courses/course-", StringComparison.Ordinal) == true)
        {
            var parts = path.Split('/');
            return parts.Length == 7 && parts[0] == "courses" &&
                PositiveSegment(parts[1], "course-") && PositiveSegment(parts[2], "unit-") &&
                PositiveSegment(parts[3], "lesson-") && PositiveSegment(parts[4], "activity-") &&
                PositiveSegment(parts[5], "resource-") && SafeFileName(parts[6]);
        }
        if (path?.StartsWith(Prefix, StringComparison.Ordinal) != true) return false;
        var file = path[Prefix.Length..];
        var slash = file.IndexOf('/');
        if (slash == 32 && file[..slash].All(char.IsAsciiHexDigit) &&
            SafeFileName(file[(slash + 1)..]) && file[(slash + 1)..].IndexOf('/') < 0)
            return true;
        var stem = Path.GetFileNameWithoutExtension(file);
        return file == Path.GetFileName(file) && stem.Length == 32 && stem.All(char.IsAsciiHexDigit) &&
            Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or
                ".pdf" or ".mp3" or ".wav" or ".mp4" or ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx";
    }

    private static bool PositiveSegment(string segment, string prefix) => segment.StartsWith(prefix, StringComparison.Ordinal) &&
        segment[prefix.Length..].Length > 0 && segment[prefix.Length..].All(char.IsAsciiDigit) &&
        int.TryParse(segment[prefix.Length..], out var id) && id > 0;

    public static string DownloadName(string displayName, string path)
    {
        if (path.StartsWith("course-", StringComparison.Ordinal)) return path[(path.LastIndexOf('/') + 1)..];
        if (path[Prefix.Length..].Contains('/')) return path[(path.LastIndexOf('/') + 1)..];
        var extension = Path.GetExtension(path);
        return displayName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? displayName : displayName + extension;
    }
}
