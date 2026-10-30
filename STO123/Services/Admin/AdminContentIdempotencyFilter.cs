using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using STO123.Controllers;
using STO123.Models;

namespace STO123.Services.Admin;

/// <summary>Database-backed idempotency for the six STEP 1 creation routes.</summary>
public sealed class AdminContentIdempotencyFilter(string operation, ToeicDbContext db,
    LectureResourceStorage storage, IConfiguration configuration, ILogger<AdminContentIdempotencyFilter> logger)
    : IAsyncActionFilter
{
    private const string Table = "dbo.AdminContentIdempotency";
    private HttpContext? requestContext;
    private sealed record StoredResponse(string? Location, JsonElement Body);
    private sealed record Existing(byte[] Fingerprint, string Status, short? ResponseStatus,
        string? ResponseBody, string? BlobPath, Guid? LeaseToken, DateTime? LeaseUntilUtc);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!configuration.GetValue<bool>("AdminContent:EnableIdempotency") ||
            !HttpMethods.IsPost(context.HttpContext.Request.Method)) { await next(); return; }
        requestContext = context.HttpContext;
        var ct = context.HttpContext.RequestAborted;
        var key = context.HttpContext.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
        {
            context.Result = new BadRequestObjectResult(new { code = "IDEMPOTENCY_KEY_REQUIRED",
                message = "Cần Idempotency-Key gồm 8–128 ký tự chữ, số, dấu gạch ngang hoặc gạch dưới." });
            return;
        }
        if (!int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0)
        { context.Result = new UnauthorizedResult(); return; }
        byte[] fingerprint;
        try { fingerprint = await Fingerprint(context, ct); }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        { context.Result = new BadRequestObjectResult(new { code = "INVALID_REQUEST_FINGERPRINT" }); return; }

        context.Result = operation == "CREATE_LECTURE_RESOURCE"
            ? await Resource(context, next, adminId, key, fingerprint, ct)
            : await SqlOnly(context, next, adminId, key, fingerprint, ct);
    }

    private static async Task<byte[]> Fingerprint(ActionExecutingContext context, CancellationToken ct)
    {
        var request = context.HttpContext.Request;
        object payload;
        if (context.ActionArguments.Values.OfType<LectureUploadRequest>().FirstOrDefault() is { } upload)
        {
            if (upload.File is null) payload = new { upload.Name, upload.Kind, file = (string?)null };
            else
            {
                await using var stream = upload.File.OpenReadStream();
                var hash = await SHA256.HashDataAsync(stream, ct);
                payload = new { name = upload.Name?.Trim(), upload.Kind, fileName = upload.File.FileName,
                    mime = upload.File.ContentType?.ToLowerInvariant(), length = upload.File.Length,
                    sha256 = Convert.ToHexString(hash) };
            }
        }
        else if (context.ActionArguments.Values.OfType<ContentNameRequest>().FirstOrDefault() is { } name)
            payload = new { name = name.Name?.Trim(), description = name.Description?.Trim() };
        else if (context.ActionArguments.Values.OfType<ContentStepRequest>().FirstOrDefault() is { } step)
            payload = new { title = step.Title?.Trim(), step.ActivityType, step.ActivityId };
        else if (context.ActionArguments.Values.OfType<CourseExerciseRequest>().FirstOrDefault() is { } exercise)
            payload = exercise;
        else if (context.ActionArguments.Values.OfType<CourseLectureRequest>().FirstOrDefault() is { } courseLecture)
            payload = new { title = courseLecture.Title?.Trim(), summary = courseLecture.Summary?.Trim(),
                content = courseLecture.Content?.Trim(), courseLecture.CategoryId, courseLecture.Level, courseLecture.PartId };
        else if (context.ActionArguments.Values.OfType<LectureRequest>().FirstOrDefault() is { } lecture)
            payload = new { title = lecture.Title?.Trim(), summary = lecture.Summary?.Trim(),
                content = lecture.Content?.Trim(), lecture.CategoryId, lecture.Level, lecture.PartId, lecture.Status };
        else if (context.ActionArguments.Values.OfType<LectureCategoryRequest>().FirstOrDefault() is { } category)
            payload = new { name = category.Name?.Trim(), description = category.Description?.Trim(), category.Status };
        else payload = new { };
        var canonical = JsonSerializer.Serialize(new { method = request.Method, path = request.Path.Value?.ToLowerInvariant(), payload });
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private async Task<IActionResult?> SqlOnly(ActionExecutingContext context, ActionExecutionDelegate next,
        int adminId, string key, byte[] fingerprint, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await Insert(adminId, key, fingerprint, null, null, ct);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1222 or 1205)
        {
            try { await tx.RollbackAsync(ct); } catch (Exception) when (e.Number == 1205) { }
            await tx.DisposeAsync();
            return e.Number == 1222 ? InProgress() : await Replay(adminId, key, fingerprint, ct);
        }
        try
        {
            var executed = await next();
            if (executed.Exception is not null) { await tx.RollbackAsync(ct); return null; }
            if (!TrySuccess(executed.Result, out var status, out var body))
            { await tx.RollbackAsync(ct); return executed.Result; }
            if (await Complete(adminId, key, fingerprint, null, status, body, ct) != 1)
                throw new InvalidOperationException("The idempotency row changed before completion.");
            await tx.CommitAsync(ct);
            return executed.Result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "SQL creation outcome uncertain for {Operation} and admin {AdminId}", operation, adminId);
            return new ObjectResult(new { code = "IDEMPOTENCY_OUTCOME_UNCERTAIN" }) { StatusCode = 503 };
        }
    }

    private async Task<IActionResult?> Resource(ActionExecutingContext context, ActionExecutionDelegate next,
        int adminId, string key, byte[] fingerprint, CancellationToken ct)
    {
        var fileName = context.ActionArguments.Values.OfType<LectureUploadRequest>().FirstOrDefault()?.File?.FileName;
        if (!LectureResourceStorage.SafeFileName(fileName))
            return new BadRequestObjectResult(new { code = "INVALID_FILENAME", message = "Tên tệp không hợp lệ. Hãy giữ nguyên tên tệp an toàn, không dùng ký tự đặc biệt hoặc đường dẫn." });
        var lease = Guid.NewGuid();
        var path = storage.NewPath(fileName);
        try { await Insert(adminId, key, fingerprint, path, lease, ct); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1222 or 1205)
        {
            if (e.Number == 1222) return InProgress();
            var existing = await Read(adminId, key, ct);
            if (existing is null) return InProgress();
            if (!existing.Fingerprint.SequenceEqual(fingerprint)) return KeyReused();
            if (existing.Status == "COMPLETED") return Restore(existing);
            if (existing.LeaseUntilUtc > DateTime.UtcNow) return InProgress();
            if (existing.LeaseToken is null || existing.BlobPath is null) return InProgress();
            var previousPath = LectureResourceStorage.FromReservationPath(existing.BlobPath);
            var changed = await db.Database.ExecuteSqlRawAsync($"""
                UPDATE {Table} SET DuongDanBlob = @p0, LeaseToken = @p1,
                    LeaseUntilUtc = DATEADD(minute, 30, SYSUTCDATETIME()), CapNhatLucUtc = SYSUTCDATETIME()
                WHERE MaAdmin = @p2 AND ThaoTac = @p3 AND KhoaYeuCau = @p4
                    AND TrangThai = 'PENDING' AND LeaseToken = @p5 AND LeaseUntilUtc < SYSUTCDATETIME()
                """, [path, lease, adminId, operation, key, existing.LeaseToken.Value], ct);
            if (changed != 1) return InProgress();
            if (!await db.TaiNguyenBaiGiang.AsNoTracking().AnyAsync(r => r.DuongDanTep == previousPath, ct))
            {
                try { await storage.DeleteAsync(previousPath, CancellationToken.None); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Stale lecture Blob needs reconciliation: {Path}", previousPath); }
            }
        }
        context.HttpContext.Items["LectureResourceReservedPath"] = path;
        context.HttpContext.Items["LectureResourceLease"] = lease;
        context.HttpContext.Items["LectureResourceSetPath"] = (Func<string, CancellationToken, Task>)(async (finalPath, token) =>
        {
            await using var connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE {Table} SET DuongDanBlob=@path WHERE MaAdmin=@admin AND ThaoTac=@operation AND KhoaYeuCau=@key AND TrangThai='PENDING' AND LeaseToken=@lease";
            command.Parameters.Add(new SqlParameter("@path", SqlDbType.NVarChar, 512) { Value = LectureResourceStorage.ReservationPath(finalPath) });
            command.Parameters.Add(new SqlParameter("@admin", adminId));
            command.Parameters.Add(new SqlParameter("@operation", operation));
            command.Parameters.Add(new SqlParameter("@key", key));
            command.Parameters.Add(new SqlParameter("@lease", lease));
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Resource reservation was lost.");
            context.HttpContext.Items["LectureResourceReservedPath"] = finalPath;
        });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var executed = await next();
            path = context.HttpContext.Items["LectureResourceReservedPath"] as string ?? path;
            if (executed.Exception is not null) { await tx.RollbackAsync(ct); return null; }
            if (!TrySuccess(executed.Result, out var status, out var body))
            {
                await tx.RollbackAsync(ct);
                await Release(adminId, key, lease, ct);
                return executed.Result;
            }
            var completed = await Complete(adminId, key, fingerprint, lease, status, body, ct);
            if (completed != 1)
            {
                await tx.RollbackAsync(ct);
                if (!await db.TaiNguyenBaiGiang.AsNoTracking().AnyAsync(r => r.DuongDanTep == path, ct))
                    await storage.DeleteAsync(path, CancellationToken.None);
                return InProgress();
            }
            await tx.CommitAsync(ct);
            return executed.Result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Resource creation outcome uncertain for admin {AdminId}, Blob {Path}", adminId, path);
            return new ObjectResult(new { code = "IDEMPOTENCY_OUTCOME_UNCERTAIN" }) { StatusCode = 503 };
        }
    }

    private async Task Insert(int adminId, string key, byte[] fingerprint, string? path, Guid? lease, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            SET LOCK_TIMEOUT 3000;
            INSERT INTO {Table} (MaAdmin, ThaoTac, KhoaYeuCau, DauVanTay, TrangThai,
                DuongDanBlob, LeaseToken, LeaseUntilUtc)
            VALUES (@p0, @p1, @p2, @p3, 'PENDING', @p4, @p5,
                CASE WHEN @p5 IS NULL THEN NULL ELSE DATEADD(minute, 30, SYSUTCDATETIME()) END);
            """, [adminId, operation, key, fingerprint,
                new SqlParameter("@p4", SqlDbType.NVarChar, 512) { Value = (object?)path ?? DBNull.Value },
                new SqlParameter("@p5", SqlDbType.UniqueIdentifier) { Value = (object?)lease ?? DBNull.Value }], ct);
    }

    private async Task<int> Complete(int adminId, string key, byte[] fingerprint, Guid? lease,
        short status, string body, CancellationToken ct) => await db.Database.ExecuteSqlRawAsync($"""
            UPDATE {Table} SET TrangThai = 'COMPLETED', MaPhanHoi = @p0,
                NoiDungPhanHoi = @p1, CapNhatLucUtc = SYSUTCDATETIME(),
                LeaseToken = NULL, LeaseUntilUtc = NULL
            WHERE MaAdmin = @p2 AND ThaoTac = @p3 AND KhoaYeuCau = @p4
                AND DauVanTay = @p5 AND TrangThai = 'PENDING'
                AND ((@p6 IS NULL AND LeaseToken IS NULL) OR LeaseToken = @p6)
            """, [status, body, adminId, operation, key, fingerprint,
                new SqlParameter("@p6", SqlDbType.UniqueIdentifier) { Value = (object?)lease ?? DBNull.Value }], ct);

    private async Task Release(int adminId, string key, Guid lease, CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync($"""
            DELETE FROM {Table} WHERE MaAdmin = @p0 AND ThaoTac = @p1 AND KhoaYeuCau = @p2
                AND TrangThai = 'PENDING' AND LeaseToken = @p3
            """, [adminId, operation, key, lease], ct);

    private async Task<Existing?> Read(int adminId, string key, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT DauVanTay, TrangThai, MaPhanHoi, NoiDungPhanHoi, DuongDanBlob, LeaseToken, LeaseUntilUtc FROM {Table} WHERE MaAdmin=@admin AND ThaoTac=@operation AND KhoaYeuCau=@key";
        command.Parameters.Add(new SqlParameter("@admin", adminId));
        command.Parameters.Add(new SqlParameter("@operation", operation));
        command.Parameters.Add(new SqlParameter("@key", key));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new((byte[])reader.GetValue(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt16(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6));
    }

    private async Task<IActionResult> Replay(int adminId, string key, byte[] fingerprint, CancellationToken ct)
    {
        var existing = await Read(adminId, key, ct);
        if (existing is null) return InProgress();
        return existing.Fingerprint.SequenceEqual(fingerprint)
            ? existing.Status == "COMPLETED" ? Restore(existing) : InProgress()
            : KeyReused();
    }

    private static bool TrySuccess(IActionResult? result, out short status, out string body)
    {
        status = 0; body = "";
        object? value; string? location = null;
        if (result is CreatedResult created) { status = 201; value = created.Value; location = created.Location; }
        else if (result is OkObjectResult ok) { status = 200; value = ok.Value; }
        else return false;
        body = JsonSerializer.Serialize(new StoredResponse(location, JsonSerializer.SerializeToElement(value)));
        return true;
    }

    private static IActionResult Restore(Existing existing)
    {
        var saved = JsonSerializer.Deserialize<StoredResponse>(existing.ResponseBody!);
        return existing.ResponseStatus == 201
            ? new CreatedResult(saved!.Location, saved.Body)
            : new OkObjectResult(saved!.Body);
    }

    private IActionResult InProgress()
    {
        if (requestContext is not null) requestContext.Response.Headers.RetryAfter = "3";
        return new ConflictObjectResult(new { code = "REQUEST_IN_PROGRESS",
            message = "Yêu cầu cùng khóa đang được xử lý. Hãy thử lại sau." });
    }
    private static IActionResult KeyReused() => new ConflictObjectResult(new { code = "IDEMPOTENCY_KEY_REUSED",
        message = "Idempotency-Key đã được dùng cho nội dung khác." });
}
