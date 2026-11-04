using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Exam;

/// <summary>Reconciles uploaded snapshots that never became a committed SQL version.</summary>
public sealed class ExamSnapshotCleanupWorker(IServiceScopeFactory scopes, BlobServiceClient blobs,
    IConfiguration configuration, IHostEnvironment environment,
    ILogger<ExamSnapshotCleanupWorker> logger) : BackgroundService
{
    private readonly string containerName = configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Development commonly uses an isolated or incomplete SQL fixture while
        // Azure Blob credentials still point to the shared account.
        if (environment.IsDevelopment()) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Exam snapshot cleanup deferred"); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
        // A failed SQL read must abort cleanup. Never infer that versions are absent.
        var referenced = (await db.PhienBanDeThi.AsNoTracking()
            .Select(v => v.BlobPath).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        // An empty or wrongly selected database must never cause the shared Blob
        // container to be treated as entirely orphaned.
        if (referenced.Count == 0)
        {
            logger.LogWarning("Skipping exam Blob cleanup: no exam snapshot references were found in SQL.");
            return;
        }
        var container = blobs.GetBlobContainerClient(containerName);
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None,
            prefix: "exam-snapshots/", cancellationToken: ct))
        {
            if (item.Properties.CreatedOn is not { } created ||
                created > DateTimeOffset.UtcNow.AddDays(-1)) continue;
            var snapshotPath = item.Name.EndsWith(".json", StringComparison.Ordinal) ? item.Name :
                item.Name.Contains("/media/", StringComparison.Ordinal) ?
                    item.Name[..item.Name.IndexOf("/media/", StringComparison.Ordinal)] + ".json" : null;
            if (snapshotPath is null || referenced.Contains(snapshotPath)) continue;
            try
            {
                await container.GetBlobClient(item.Name).DeleteIfExistsAsync(cancellationToken: ct);
                logger.LogInformation("Removed unreferenced exam snapshot Blob {Path}", item.Name);
            }
            catch (Exception e) { logger.LogWarning(e, "Cannot remove orphaned exam Blob {Path}", item.Name); }
        }
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None,
            prefix: "exam-history-media/", cancellationToken: ct))
        {
            if (item.Properties.CreatedOn is not { } created ||
                created > DateTimeOffset.UtcNow.AddDays(-1)) continue;
            var path = item.Name;
            var used = await db.NhomLuotLam.AsNoTracking().AnyAsync(g =>
                g.DuongDanAudio == path || g.DuongDanAnh == path ||
                g.TaiLieuJson != null && g.TaiLieuJson.Contains(path), ct) ||
                await db.NguLieu.AsNoTracking().AnyAsync(g =>
                    g.DuongDanAudio == path || g.DuongDanAnh == path, ct) ||
                await db.NguLieuTaiLieu.AsNoTracking().AnyAsync(d => d.DuongDanAnh == path, ct);
            if (used) continue;
            try { await container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct); }
            catch (Exception e) { logger.LogWarning(e, "Cannot remove orphaned history media {Path}", path); }
        }
    }
}
