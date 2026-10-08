using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/exams")]
public sealed class ExamSourceValidationController(ToeicDbContext db, BlobServiceClient blobs,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("{examId:int}/validate")]
    public async Task<IActionResult> Validate(int examId, CancellationToken ct)
    {
        var exam = await db.DeThi.AsNoTracking().FirstOrDefaultAsync(x => x.MaDeThi == examId, ct);
        if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND", message = "Không tìm thấy đề thi." });
        var links = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == examId)
            .OrderBy(x => x.ThuTu).ToListAsync(ct);
        var questions = await db.CauHoi.AsNoTracking().ToDictionaryAsync(x => x.MaCauHoi, ct);
        var parts = await db.PartTOEIC.AsNoTracking().ToDictionaryAsync(x => x.MaPart, x => x.SoPart, ct);
        var memberships = await db.NhomCauHoi.AsNoTracking().ToListAsync(ct);
        var resources = await db.NguLieu.AsNoTracking().ToDictionaryAsync(x => x.MaNguLieu, ct);
        var documents = await db.NguLieuTaiLieu.AsNoTracking().ToListAsync(ct);
        var linkedQuestionIds = links.Select(l => l.MaCauHoi).ToHashSet();
        var linkedResources = memberships.Where(m => linkedQuestionIds.Contains(m.MaCauHoi))
            .Select(m => m.MaNguLieu).ToHashSet();
        var paths = resources.Values.Where(r => linkedResources.Contains(r.MaNguLieu))
            .SelectMany(r => new[] { r.DuongDanAudio, r.DuongDanAnh })
            .Concat(documents.Where(d => linkedResources.Contains(d.MaNguLieu)).Select(d => d.DuongDanAnh))
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct().ToArray();
        var existing = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        if (paths.Length > 0)
        {
            var containerName = configuration["ExamMedia:ContainerName"] ?? configuration["AzureBlob:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName))
                return StatusCode(503, new { code = "MEDIA_NOT_AVAILABLE", message = "Media bài thi chưa được cấu hình." });
            var container = blobs.GetBlobContainerClient(containerName);
            using var gate = new SemaphoreSlim(8);
            try
            {
                await Task.WhenAll(paths.Select(async path =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        if ((await container.GetBlobClient(path).ExistsAsync(ct)).Value)
                            existing.TryAdd(path, 0);
                    }
                    finally { gate.Release(); }
                }));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return StatusCode(503, new { code = "MEDIA_NOT_AVAILABLE", message = "Không thể kiểm tra Blob của đề thi." });
            }
        }
        var report = ExamSourceValidator.ValidateFixed(exam, links, questions, parts,
            memberships, resources, documents, existing.Keys.ToHashSet());
        return Ok(new { examId, examCode = MockExamCode.FromId(examId),
            examStatus = exam.TrangThai, report.Valid, report.Issues });
    }
}
