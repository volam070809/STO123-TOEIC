using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Admin;

public sealed record CourseBlobJournal(int MaTaiNguyen, string DuongDanNguon, string DuongDanDich);
public sealed record CourseBlobMarker(long TheHe, int SoThaoTacDangChay, DateTime? LeaseUntilUtc);

public sealed class CourseBlobSynchronizer(ToeicDbContext db, LectureResourceStorage storage)
{
    private sealed record Resource(int Id, int CourseId, int UnitOrder, int LessonOrder,
        int StepId, string Title, string Name, string Path)
    {
        public string Target => LectureResourceStorage.CoursePath(CourseId, UnitOrder, LessonOrder,
            Title, StepId, Id, Name);
    }

    private async Task<List<Resource>> Resources(int courseId, CancellationToken ct)
    {
        var rows = await db.BuocLoTrinh.AsNoTracking()
            .Where(s => s.MaBaiGiang != null && s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)
            .SelectMany(s => s.MaBaiGiangNavigation!.TaiNguyenBaiGiang.Select(r =>
                new { r.MaTaiNguyen, UnitOrder = s.MaLessonNavigation.MaUnitNavigation.ThuTu,
                    LessonOrder = s.MaLessonNavigation.ThuTu, s.MaBuoc, s.TieuDe,
                    r.TenTaiNguyen, r.DuongDanTep }))
            .ToListAsync(ct);
        return rows.Select(r => new Resource(r.MaTaiNguyen, courseId, r.UnitOrder, r.LessonOrder,
            r.MaBuoc, r.TieuDe, LectureResourceStorage.DownloadName(r.TenTaiNguyen, r.DuongDanTep),
            r.DuongDanTep)).ToList();
    }

    public async Task MarkCourse(int courseId, CancellationToken ct)
    {
        if (courseId <= 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            BEGIN TRAN;
            IF EXISTS (SELECT 1 FROM dbo.CourseBlobSyncJournal WITH (UPDLOCK,HOLDLOCK)
                       WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0)
                UPDATE dbo.CourseBlobSyncJournal SET TheHe=TheHe+1,
                    SoThaoTacDangChay=SoThaoTacDangChay+1,
                    LeaseUntilUtc=DATEADD(hour,2,SYSUTCDATETIME()),CapNhatLucUtc=SYSUTCDATETIME()
                WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0;
            ELSE INSERT dbo.CourseBlobSyncJournal(MaKhoaHoc,MaTaiNguyen,TheHe,SoThaoTacDangChay,LeaseUntilUtc)
                VALUES({courseId},0,1,1,DATEADD(hour,2,SYSUTCDATETIME()));
            COMMIT;
            """, ct);
    }

    public Task ReleaseCourse(int courseId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.CourseBlobSyncJournal
            SET SoThaoTacDangChay=CASE WHEN SoThaoTacDangChay>0 THEN SoThaoTacDangChay-1 ELSE 0 END,
                CapNhatLucUtc=SYSUTCDATETIME()
            WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0
            """, ct);

    public Task TrackRemoval(int courseId, int resourceId, string path, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (SELECT 1 FROM dbo.CourseBlobSyncJournal WITH (UPDLOCK,HOLDLOCK)
                           WHERE MaKhoaHoc={courseId} AND MaTaiNguyen={resourceId})
                INSERT dbo.CourseBlobSyncJournal
                    (MaKhoaHoc,MaTaiNguyen,DuongDanNguon,DuongDanDich,TheHe)
                VALUES({courseId},{resourceId},{path},{path},0)
            """, ct);

    public async Task SyncCourse(int courseId, CancellationToken ct)
    {
        if (courseId <= 0) return;
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        async Task<int> AppLock(string procedure)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @result int; EXEC @result = " + procedure +
                " @Resource=@resource," + (procedure == "sp_getapplock" ? "@LockMode='Exclusive'," : "") +
                "@LockOwner='Session'; SELECT @result;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@resource"; parameter.Value = "CourseBlobSync:" + courseId;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
        var acquired = false;
        try
        {
            acquired = await AppLock("sp_getapplock") >= 0;
            if (!acquired) throw new InvalidOperationException("Course resource sync lock unavailable.");
            for (var pass = 0; pass < 10; pass++)
            {
                var marker = await db.Database.SqlQuery<CourseBlobMarker>($"""
                    SELECT TheHe,SoThaoTacDangChay,LeaseUntilUtc FROM dbo.CourseBlobSyncJournal
                    WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0
                    """).FirstOrDefaultAsync(ct);
                if (marker is null) return;
                if (marker.SoThaoTacDangChay > 0 && marker.LeaseUntilUtc > DateTime.UtcNow)
                    throw new InvalidOperationException("Course synchronization is waiting for an active Admin operation.");
                if (marker.SoThaoTacDangChay > 0)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE dbo.CourseBlobSyncJournal SET SoThaoTacDangChay=0
                        WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0 AND TheHe={marker.TheHe}
                        """, ct);
                await SeparateSharedLectures(courseId, ct);
                var rows = await Resources(courseId, ct);
                foreach (var group in rows.GroupBy(r => r.Id))
                {
                    var paths = group.Select(r => r.Target).Distinct(StringComparer.Ordinal).ToArray();
                    if (paths.Length != 1)
                        throw new InvalidOperationException("A shared lecture resource has more than one curriculum location.");
                    await SyncResource(courseId, group.First(), ct);
                }
                var pending = await db.Database.SqlQuery<int>($"""
                    SELECT MaTaiNguyen AS Value FROM dbo.CourseBlobSyncJournal
                    WHERE MaKhoaHoc={courseId} AND MaTaiNguyen>0
                    """).ToListAsync(ct);
                foreach (var id in pending.Where(id => rows.All(r => r.Id != id)))
                    await FinishJournal(courseId, id, ct);
                var removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE dbo.CourseBlobSyncJournal
                    WHERE MaKhoaHoc={courseId} AND MaTaiNguyen=0 AND TheHe={marker.TheHe}
                        AND SoThaoTacDangChay=0
                    """, ct);
                if (removed == 1) return;
            }
            throw new InvalidOperationException("Course curriculum changed repeatedly during resource synchronization.");
        }
        finally
        {
            try { if (acquired) await AppLock("sp_releaseapplock"); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private async Task SeparateSharedLectures(int courseId, CancellationToken ct)
    {
        var steps = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBaiGiang != null &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId)
            .Select(s => s.MaBuoc).ToListAsync(ct);
        foreach (var stepId in steps)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var step = await db.BuocLoTrinh.FirstOrDefaultAsync(s => s.MaBuoc == stepId, ct);
            if (step?.MaBaiGiang is not { } lectureId ||
                await db.BuocLoTrinh.CountAsync(s => s.MaBaiGiang == lectureId, ct) <= 1)
            {
                await tx.CommitAsync(ct); continue;
            }
            var original = await db.BaiGiang.AsNoTracking().FirstAsync(x => x.MaBaiGiang == lectureId, ct);
            var copy = new BaiGiang { MaDanhMuc = original.MaDanhMuc, CapDo = original.CapDo,
                TieuDe = original.TieuDe, MaPart = original.MaPart, NoiDungTomTat = original.NoiDungTomTat,
                NoiDung = original.NoiDung, MaAdminNoiDung = original.MaAdminNoiDung,
                NgayTao = original.NgayTao, TrangThai = original.TrangThai };
            db.BaiGiang.Add(copy);
            await db.SaveChangesAsync(ct);
            var resources = await db.TaiNguyenBaiGiang.AsNoTracking()
                .Where(r => r.MaBaiGiang == lectureId).ToListAsync(ct);
            foreach (var resource in resources)
                db.TaiNguyenBaiGiang.Add(new TaiNguyenBaiGiang {
                    MaBaiGiang = copy.MaBaiGiang, TenTaiNguyen = resource.TenTaiNguyen,
                    LoaiTaiNguyen = resource.LoaiTaiNguyen, DuongDanTep = resource.DuongDanTep,
                    ThuTu = resource.ThuTu, IsArchived = resource.IsArchived });
            step.MaBaiGiang = copy.MaBaiGiang;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task SyncResource(int courseId, Resource resource, CancellationToken ct)
    {
        var pending = await Journal(courseId, resource.Id, ct);
        if (pending is null && resource.Path == resource.Target) return;
        if (pending is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.CourseBlobSyncJournal(MaKhoaHoc,MaTaiNguyen,DuongDanNguon,DuongDanDich,TheHe)
                VALUES({courseId},{resource.Id},{resource.Path},{resource.Target},0)
                """, ct);
            pending = new(resource.Id, resource.Path, resource.Target);
        }
        await FinishJournal(courseId, resource.Id, ct);
        var latest = (await Resources(courseId, ct)).FirstOrDefault(r => r.Id == resource.Id);
        if (latest is not null && latest.Path != latest.Target)
            await SyncResource(courseId, latest, ct);
    }

    private Task<CourseBlobJournal?> Journal(int courseId, int id, CancellationToken ct) =>
        db.Database.SqlQuery<CourseBlobJournal>($"""
            SELECT MaTaiNguyen,DuongDanNguon,DuongDanDich FROM dbo.CourseBlobSyncJournal
            WHERE MaKhoaHoc={courseId} AND MaTaiNguyen={id}
            """).FirstOrDefaultAsync(ct);

    private async Task FinishJournal(int courseId, int id, CancellationToken ct)
    {
        var job = await Journal(courseId, id, ct);
        if (job is null) return;
        var row = await db.TaiNguyenBaiGiang.FirstOrDefaultAsync(r => r.MaTaiNguyen == id, ct);
        if (row?.DuongDanTep == job.DuongDanNguon)
        {
            var location = (await Resources(courseId, ct)).FirstOrDefault(r => r.Id == id);
            if (location?.Target == job.DuongDanDich)
            {
                await storage.CopyVerifiedAsync(job.DuongDanNguon, job.DuongDanDich, ct);
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.TaiNguyenBaiGiang SET DuongDanTep={job.DuongDanDich}
                    WHERE MaTaiNguyen={id} AND DuongDanTep={job.DuongDanNguon}
                    """, ct);
                await tx.CommitAsync(ct);
                db.Entry(row).State = EntityState.Detached;
            }
        }
        foreach (var path in new[] { job.DuongDanNguon, job.DuongDanDich }.Distinct())
            await DeleteIfUnreferenced(path, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE dbo.CourseBlobSyncJournal WHERE MaKhoaHoc={courseId} AND MaTaiNguyen={id}
            """, ct);
    }

    private async Task DeleteIfUnreferenced(string path, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var referenced = await db.TaiNguyenBaiGiang
            .FromSqlInterpolated($"SELECT * FROM dbo.TaiNguyenBaiGiang WITH (UPDLOCK,HOLDLOCK) WHERE DuongDanTep={path}")
            .AsNoTracking()
            .AnyAsync(ct);
        if (!referenced) await storage.DeleteAsync(path, ct);
        await tx.CommitAsync(ct);
    }
}

public sealed class CourseBlobSyncFilter(CourseBlobSynchronizer sync, ToeicDbContext db,
    ILogger<CourseBlobSyncFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -1000;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (method is not ("POST" or "PUT" or "DELETE")) { await next(); return; }
        var route = context.RouteData.Values;
        var courseIds = new List<int>();
        if (int.TryParse(route["courseId"]?.ToString(), out var courseId)) courseIds.Add(courseId);
        else if (context.Controller is STO123.Controllers.AdminLecturesController &&
                 !context.HttpContext.Request.Path.Value!.Contains("/categories/", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(route["id"]?.ToString(), out var lectureId))
            courseIds = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBaiGiang == lectureId)
                .Select(s => s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc).Distinct().ToListAsync();
        var marked = new List<int>();
        try
        {
            foreach (var id in courseIds)
            {
                await sync.MarkCourse(id, context.HttpContext.RequestAborted);
                marked.Add(id);
            }
        }
        catch
        {
            foreach (var id in marked) await sync.ReleaseCourse(id, CancellationToken.None);
            throw;
        }
        ActionExecutedContext executed;
        try { executed = await next(); }
        finally
        {
            foreach (var id in courseIds)
                await sync.ReleaseCourse(id, CancellationToken.None);
        }
        if (executed.Exception is not null || courseIds.Count == 0) return;
        try
        {
            foreach (var id in courseIds) await sync.SyncCourse(id, context.HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Course Blob synchronization pending for {Courses}", courseIds);
            executed.Result = new ObjectResult(new { code = "COURSE_BLOB_SYNC_PENDING",
                message = "Nội dung SQL đã lưu; tệp đang được đồng bộ. Vui lòng tải lại trước khi thao tác tiếp." })
                { StatusCode = 503 };
        }
    }
}

public sealed class CourseBlobSyncRecovery(IServiceScopeFactory scopes, ILogger<CourseBlobSyncRecovery> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
                var sync = scope.ServiceProvider.GetRequiredService<CourseBlobSynchronizer>();
                var courses = await db.Database.SqlQuery<int>($"""
                    SELECT MaKhoaHoc AS Value FROM dbo.CourseBlobSyncJournal
                    WHERE MaTaiNguyen=0 AND (SoThaoTacDangChay=0 OR LeaseUntilUtc<SYSUTCDATETIME())
                    """).ToListAsync(stoppingToken);
                foreach (var courseId in courses)
                    try { await sync.SyncCourse(courseId, stoppingToken); }
                    catch (Exception e) { logger.LogWarning(e, "Course resource recovery pending for {CourseId}", courseId); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Course resource recovery failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
