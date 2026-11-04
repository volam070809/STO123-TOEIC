using System.Text;
using System.Diagnostics;
using Azure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STO123.Models;
using STO123.Services;
using STO123.Services.Admin;
using STO123.Services.Exam;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace STO123.Services.Ai;

public sealed class AiPdfTextService(ToeicDbContext db, LectureResourceStorage storage,
    IMemoryCache cache, TimeProvider clock, ContentRevisionStore revisions,
    ILogger<AiPdfTextService> logger)
{
    private const long MaxPdfBytes = 5_000_000;
    private const int MaxPages = 8;
    private const int MaxTextLength = 3500;

    public async Task<string?> ReadFromAuthorizedStepAsync(int learnerId, int courseId,
        int stepId, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var resource = await db.TaiNguyenBaiGiang.AsNoTracking()
            .Where(r => r.LoaiTaiNguyen == "PDF" && !r.IsArchived &&
                r.MaBaiGiangNavigation.TrangThai == "DANG_MO" &&
                r.MaBaiGiangNavigation.BuocLoTrinh.Any(s => s.MaBuoc == stepId &&
                    s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
                    CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                        .Contains(s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc) &&
                    !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                    s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO"))
            .OrderBy(r => r.ThuTu).Select(r => new { r.DuongDanTep, r.TenTaiNguyen })
            .FirstOrDefaultAsync(ct);
        if (resource is null || !LectureResourceStorage.Owned(resource.DuongDanTep)) return null;
        var authorizationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        try
        {
            var phase = Stopwatch.GetTimestamp();
            var blob = storage.Container.GetBlobClient(resource.DuongDanTep);
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            var metadataMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
            if (properties.Value.ContentLength is < 100 or > MaxPdfBytes) return null;
            // A new content revision invalidates extracted text even when an editor changes
            // publication state; the ETag also catches an in-place Blob replacement.
            long revision = 0;
            phase = Stopwatch.GetTimestamp();
            try { revision = (await revisions.ReadAsync(null, false, ct)).Version; }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
            { logger.LogWarning(e, "AI PDF revision unavailable; Blob ETag remains the cache key."); }
            var revisionMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
            var key = "ai-pdf:" + resource.DuongDanTep + ":" + properties.Value.ETag + ":" + revision;
            if (cache.TryGetValue<string>(key, out var cached))
            {
                logger.LogInformation("AI PDF timing cache_hit=true authorization_ms={AuthorizationMs:F1} blob_metadata_ms={MetadataMs:F1} revision_ms={RevisionMs:F1} extraction_ms=0 total_ms={TotalMs:F1}",
                    authorizationMs, metadataMs, revisionMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return cached;
            }
            phase = Stopwatch.GetTimestamp();
            await using var stream = await blob.OpenReadAsync(cancellationToken: ct);
            using var buffer = new MemoryStream((int)properties.Value.ContentLength);
            await stream.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxPdfBytes) return null;
            using var document = PdfDocument.Open(buffer.ToArray());
            var text = new StringBuilder();
            for (var page = 1; page <= Math.Min(document.NumberOfPages, MaxPages) &&
                text.Length < MaxTextLength; page++)
            {
                var pageText = ContentOrderTextExtractor.GetText(document.GetPage(page));
                if (string.IsNullOrWhiteSpace(pageText)) continue;
                text.Append("[Trang ").Append(page).Append("] ")
                    .Append(pageText.AsSpan(0, Math.Min(pageText.Length,
                        MaxTextLength - text.Length))).AppendLine();
            }
            var result = text.ToString().Trim();
            if (result.Length == 0) return null;
            cache.Set(key, result, TimeSpan.FromMinutes(15));
            logger.LogInformation("AI PDF timing cache_hit=false authorization_ms={AuthorizationMs:F1} blob_metadata_ms={MetadataMs:F1} revision_ms={RevisionMs:F1} extraction_ms={ExtractionMs:F1} total_ms={TotalMs:F1}",
                authorizationMs, metadataMs, revisionMs, Stopwatch.GetElapsedTime(phase).TotalMilliseconds,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning(e, "AI could not extract PDF text from resource {Path}", resource.DuongDanTep);
            return null;
        }
    }
}
