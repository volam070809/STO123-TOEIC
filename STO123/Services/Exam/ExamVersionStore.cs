using System.Security.Cryptography;
using System.Data;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STO123.Models;

namespace STO123.Services.Exam;

public sealed record VersionQuestion(int Id, int Part, int Order, int PartOrder, int? GroupId,
    string? Text, string A, string B, string C, string? D, string Correct, string? Explanation,
    byte Difficulty = 2);
public sealed record VersionGroup(int Id, int Part, int Order, string? Text, string? Translation,
    string? Audio, string? Image, List<ExamDocumentSnapshot> Documents);
public sealed record VersionedExam(int SchemaVersion, int ExamId, int Version, string Name,
    int Duration, List<VersionQuestion> Questions, List<VersionGroup> Groups);

/// <summary>Immutable exam content. A published version never reads question-bank rows.</summary>
public sealed class ExamVersionStore(ToeicDbContext db, BlobServiceClient blobs,
    IConfiguration configuration, ExamGenerationService generation, IMemoryCache cache)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string containerName = configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media";

    public static VersionedExam FromPlan(int examId, int number, string name, IReadOnlyList<PlannedUnit> plan)
    {
        var questions = new List<VersionQuestion>(200);
        var groups = new List<VersionGroup>();
        var partOrders = new Dictionary<int, int>();
        foreach (var unit in plan)
        {
            if (unit.Resource is { } resource)
                groups.Add(new VersionGroup(resource.MaNguLieu, unit.Part, groups.Count + 1,
                    resource.NoiDungNguLieu, resource.NoiDungDich, resource.DuongDanAudio,
                    resource.DuongDanAnh, unit.Documents.Select(d => new ExamDocumentSnapshot(
                        d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh)).ToList()));
            foreach (var q in unit.Questions)
            {
                partOrders[unit.Part] = partOrders.GetValueOrDefault(unit.Part) + 1;
                questions.Add(new VersionQuestion(q.MaCauHoi, unit.Part, questions.Count + 1,
                    partOrders[unit.Part], unit.Resource?.MaNguLieu, q.NoiDung,
                    q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD,
                    q.PhuongAnDung, q.GiaiThich, q.DoKho));
            }
        }
        if (questions.Count != 200 || questions.Select(q => q.Id).Distinct().Count() != 200 ||
            ExamCore.PartCounts.Any(p => questions.Count(q => q.Part == p.Key) != p.Value))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề thi phải đủ 200 câu đúng cấu trúc TOEIC.", 409);
        return new VersionedExam(1, examId, number, name, ExamCore.DurationMinutes, questions, groups);
    }

    public async Task<VersionedExam> ReadAsync(PhienBanDeThi version, CancellationToken ct)
    {
        var cacheKey = $"exam-version:{version.BlobContainer}:{version.BlobPath}:{version.ContentHash}";
        if (cache.TryGetValue<VersionedExam>(cacheKey, out var cached) && cached is not null)
            return cached;
        var blob = blobs.GetBlobContainerClient(version.BlobContainer).GetBlobClient(version.BlobPath);
        byte[] bytes;
        try { bytes = (await blob.DownloadContentAsync(ct)).Value.Content.ToArray(); }
        catch (RequestFailedException e) when (e.Status == 404)
        { throw new ExamProblem("EXAM_VERSION_MISSING", "Không tìm thấy phiên bản đề đã xuất bản.", 503); }
        if (!StringComparer.OrdinalIgnoreCase.Equals(Hash(bytes), version.ContentHash))
            throw new ExamProblem("EXAM_VERSION_CORRUPT", "Dữ liệu phiên bản đề không khớp mã kiểm tra.", 503);
        VersionedExam? snapshot;
        try { snapshot = JsonSerializer.Deserialize<VersionedExam>(bytes, Json); }
        catch (JsonException) { snapshot = null; }
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.ExamId != version.MaDeThi ||
            snapshot.Version != version.SoPhienBan || snapshot.Questions.Count != 200 ||
            snapshot.Duration != ExamCore.DurationMinutes)
            throw new ExamProblem("EXAM_VERSION_CORRUPT", "Phiên bản đề đã xuất bản không hợp lệ.", 503);
        cache.Set(cacheKey, snapshot, TimeSpan.FromHours(2));
        return snapshot;
    }

    public async Task<PhienBanDeThi> PublishAsync(int examId, int? expectedCurrentId,
        Guid requestKey, int publisherId, CancellationToken ct,
        IReadOnlyList<int>? orderedQuestionIds = null)
    {
        if (requestKey == Guid.Empty)
            throw new ExamProblem("INVALID_REQUEST_KEY", "Thiếu mã yêu cầu xuất bản.");
        var previous = await db.PhienBanDeThi.AsNoTracking().FirstOrDefaultAsync(
            v => v.MaDeThi == examId && v.KhoaYeuCau == requestKey, ct);
        if (previous is not null) return previous;
        var exam = await db.DeThi.AsNoTracking().FirstOrDefaultAsync(e => e.MaDeThi == examId, ct)
            ?? throw new ExamProblem("EXAM_NOT_FOUND", "Không tìm thấy đề thi.", 404);
        if (exam.MaPhienBanHienHanh != expectedCurrentId)
            throw new ExamProblem("VERSION_CONFLICT", "Đề đã có phiên bản mới. Vui lòng tải lại trước khi xuất bản.", 409);
        var next = await db.PhienBanDeThi.AsNoTracking().Where(v => v.MaDeThi == examId)
            .MaxAsync(v => (int?)v.SoPhienBan, ct) is { } last ? last + 1 : 1;
        var source = orderedQuestionIds is null ? FromPlan(examId, next, exam.TenDe,
            await generation.ValidateForPublishAsync(examId, ct)) :
            await FromSelectionAsync(exam, next, orderedQuestionIds, ct);
        var sourceHash = Hash(JsonSerializer.SerializeToUtf8Bytes(source, Json));
        var prefix = $"exam-snapshots/{examId}/v{next}/{requestKey:N}-{Guid.NewGuid():N}";
        var archived = await ArchiveMediaAsync(source, prefix, ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(archived, Json);
        var blobPath = $"{prefix}.json";
        var snapshotBlob = blobs.GetBlobContainerClient(containerName).GetBlobClient(blobPath);
        await snapshotBlob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, ct);
        var downloaded = (await snapshotBlob.DownloadContentAsync(ct)).Value.Content.ToArray();
        var contentHash = Hash(bytes);
        if (Hash(downloaded) != contentHash)
            throw new ExamProblem("EXAM_SNAPSHOT_VERIFY_FAILED", "Không xác minh được tệp đề trên Azure Blob.", 503);

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // This row is also read with HOLDLOCK by learner Start. The pointer changes atomically.
        var locked = await db.DeThi.FromSqlInterpolated(
            $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
            .SingleAsync(ct);
        previous = await db.PhienBanDeThi.AsNoTracking().FirstOrDefaultAsync(
            v => v.MaDeThi == examId && v.KhoaYeuCau == requestKey, ct);
        if (previous is not null) { await tx.CommitAsync(ct); return previous; }
        if (locked.MaPhienBanHienHanh != expectedCurrentId ||
            await db.PhienBanDeThi.AnyAsync(v => v.MaDeThi == examId && v.SoPhienBan == next, ct))
            throw new ExamProblem("VERSION_CONFLICT", "Đề đã có phiên bản mới. Vui lòng tải lại trước khi xuất bản.", 409);
        var currentSource = orderedQuestionIds is null ? FromPlan(examId, next, locked.TenDe,
            await generation.ValidateForPublishAsync(examId, ct)) :
            await FromSelectionAsync(locked, next, orderedQuestionIds, ct);
        if (Hash(JsonSerializer.SerializeToUtf8Bytes(currentSource, Json)) != sourceHash)
            throw new ExamProblem("SOURCE_CHANGED", "Ngân hàng câu hỏi đã thay đổi trong lúc xuất bản. Vui lòng thử lại.", 409);
        var version = new PhienBanDeThi {
            MaDeThi = examId, SoPhienBan = next, BlobContainer = containerName,
            BlobPath = blobPath, ContentHash = contentHash, SchemaVersion = 1,
            SoCau = 200, ThoiGianLamBai = ExamCore.DurationMinutes, TenDe = locked.TenDe,
            NgayXuatBan = DateTime.UtcNow, MaNguoiXuatBan = publisherId, KhoaYeuCau = requestKey
        };
        db.PhienBanDeThi.Add(version);
        await db.SaveChangesAsync(ct);
        if (orderedQuestionIds is not null)
        {
            var links = await db.CauHoiDeThi.Where(l => l.MaDeThi == examId).ToListAsync(ct);
            db.CauHoiDeThi.RemoveRange(links);
            await db.SaveChangesAsync(ct);
            db.CauHoiDeThi.AddRange(orderedQuestionIds.Select((id, index) => new CauHoiDeThi {
                MaDeThi = examId, MaCauHoi = id, ThuTu = index + 1
            }));
            await db.SaveChangesAsync(ct);
        }
        locked.MaPhienBanHienHanh = version.MaPhienBanDeThi;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return version;
    }

    /// <summary>
    /// Changes a source unit and every published exam that currently links it in one SQL
    /// transaction. Version blobs are immutable; attempts keep their original version ID.
    /// Blob uploads are verified before any current pointer can be committed.
    /// An upload left unreferenced by a failed SQL transaction is retained: deleting it
    /// after an uncertain commit could break an exam that did commit.
    /// </summary>
    public async Task<int[]> ApplySourceEditAsync(IReadOnlyCollection<int> questionIds,
        Func<CancellationToken, Task> edit, int editorId, CancellationToken ct)
    {
        if (questionIds.Count == 0) throw new ExamProblem("EMPTY_SOURCE_EDIT", "Thiếu câu nguồn.");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var ids = questionIds.Distinct().ToArray();
            foreach (var id in ids.OrderBy(id => id))
            {
                var resourceName = $"sto123:source-edit:{id}";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource = {resourceName},
                        @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
                    IF @result < 0 THROW 51000, 'Cannot lock exam source edit', 1;
                    """, ct);
            }
            var linked = await db.CauHoiDeThi.AsNoTracking()
                .Where(link => ids.Contains(link.MaCauHoi))
                .Select(link => link.MaDeThi).Distinct().OrderBy(id => id).ToArrayAsync(ct);
            var exams = new List<DeThi>(linked.Length);
            foreach (var id in linked)
            {
                var exam = await db.DeThi.FromSqlInterpolated(
                    $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {id}")
                    .SingleAsync(ct);
                if (exam.MaPhienBanHienHanh is not null) exams.Add(exam);
            }
            var affected = exams.Select(e => e.MaDeThi).ToArray();
            foreach (var id in ids.OrderBy(id => id))
                if (await db.CauHoi.FromSqlInterpolated(
                    $"SELECT * FROM dbo.CauHoi WITH (UPDLOCK, HOLDLOCK) WHERE MaCauHoi = {id}")
                    .SingleOrDefaultAsync(ct) is null)
                    throw new ExamProblem("QUESTION_NOT_FOUND", $"Không tìm thấy câu {id}.", 404);
            await edit(ct);
            await db.SaveChangesAsync(ct);
            foreach (var exam in exams)
            {
                var next = (await db.PhienBanDeThi.AsNoTracking()
                    .Where(v => v.MaDeThi == exam.MaDeThi)
                    .MaxAsync(v => (int?)v.SoPhienBan, ct) ?? 0) + 1;
                var source = FromPlan(exam.MaDeThi, next, exam.TenDe,
                    await generation.ValidateForPublishAsync(exam.MaDeThi, ct));
                var prefix = $"exam-snapshots/{exam.MaDeThi}/v{next}/{Guid.NewGuid():N}";
                var archived = await ArchiveMediaAsync(source, prefix, ct);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(archived, Json);
                var blobPath = $"{prefix}.json";
                var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(blobPath);
                await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                }, ct);
                var stored = (await blob.DownloadContentAsync(ct)).Value.Content.ToArray();
                var hash = Hash(bytes);
                if (Hash(stored) != hash)
                    throw new ExamProblem("EXAM_SNAPSHOT_VERIFY_FAILED",
                        "Không xác minh được snapshot mới.", 503);
                var version = new PhienBanDeThi {
                    MaDeThi = exam.MaDeThi, SoPhienBan = next, BlobContainer = containerName,
                    BlobPath = blobPath, ContentHash = hash, SchemaVersion = 1,
                    SoCau = 200, ThoiGianLamBai = ExamCore.DurationMinutes,
                    TenDe = exam.TenDe, NgayXuatBan = DateTime.UtcNow,
                    MaNguoiXuatBan = editorId, KhoaYeuCau = Guid.NewGuid()
                };
                db.PhienBanDeThi.Add(version);
                await db.SaveChangesAsync(ct);
                exam.MaPhienBanHienHanh = version.MaPhienBanDeThi;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return affected;
    }

    public async Task<PhienBanDeThi> RestoreAsync(int examId, int sourceVersionId,
        int expectedCurrentId, Guid requestKey, int publisherId, CancellationToken ct)
    {
        if (requestKey == Guid.Empty)
            throw new ExamProblem("INVALID_REQUEST_KEY", "Thiếu mã yêu cầu khôi phục.");
        var previous = await db.PhienBanDeThi.AsNoTracking().SingleOrDefaultAsync(
            v => v.MaDeThi == examId && v.KhoaYeuCau == requestKey, ct);
        if (previous is not null) return previous;
        var exam = await db.DeThi.AsNoTracking().SingleOrDefaultAsync(e => e.MaDeThi == examId, ct)
            ?? throw new ExamProblem("EXAM_NOT_FOUND", "Không tìm thấy đề thi.", 404);
        if (exam.MaPhienBanHienHanh != expectedCurrentId)
            throw new ExamProblem("VERSION_CONFLICT", "Đề đã đổi phiên bản. Vui lòng tải lại.", 409);
        var old = await db.PhienBanDeThi.AsNoTracking().SingleOrDefaultAsync(
            v => v.MaDeThi == examId && v.MaPhienBanDeThi == sourceVersionId, ct)
            ?? throw new ExamProblem("VERSION_NOT_FOUND", "Không tìm thấy phiên bản cần khôi phục.", 404);
        var last = await db.PhienBanDeThi.AsNoTracking().Where(v => v.MaDeThi == examId)
            .MaxAsync(v => v.SoPhienBan, ct);
        var restored = (await ReadAsync(old, ct)) with { Version = last + 1 };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(restored, Json);
        var path = $"exam-snapshots/{examId}/v{restored.Version}/{requestKey:N}-{Guid.NewGuid():N}.json";
        var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(path);
        await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, ct);
        var hash = Hash(bytes);
        if (Hash((await blob.DownloadContentAsync(ct)).Value.Content.ToArray()) != hash)
            throw new ExamProblem("EXAM_SNAPSHOT_VERIFY_FAILED", "Không xác minh được phiên bản khôi phục.", 503);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var locked = await db.DeThi.FromSqlInterpolated(
            $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId}")
            .SingleAsync(ct);
        previous = await db.PhienBanDeThi.AsNoTracking().SingleOrDefaultAsync(
            v => v.MaDeThi == examId && v.KhoaYeuCau == requestKey, ct);
        if (previous is not null) { await tx.CommitAsync(ct); return previous; }
        if (locked.MaPhienBanHienHanh != expectedCurrentId ||
            await db.PhienBanDeThi.AnyAsync(v => v.MaDeThi == examId && v.SoPhienBan == restored.Version, ct))
            throw new ExamProblem("VERSION_CONFLICT", "Đề đã đổi phiên bản. Vui lòng tải lại.", 409);
        var row = new PhienBanDeThi {
            MaDeThi = examId, SoPhienBan = restored.Version, BlobContainer = containerName,
            BlobPath = path, ContentHash = hash, SchemaVersion = 1, SoCau = 200,
            ThoiGianLamBai = ExamCore.DurationMinutes, TenDe = restored.Name,
            NgayXuatBan = DateTime.UtcNow, MaNguoiXuatBan = publisherId, KhoaYeuCau = requestKey
        };
        db.PhienBanDeThi.Add(row);
        await db.SaveChangesAsync(ct);
        locked.MaPhienBanHienHanh = row.MaPhienBanDeThi;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    private async Task<VersionedExam> FromSelectionAsync(DeThi exam, int number,
        IReadOnlyList<int> selectedIds, CancellationToken ct)
    {
        if (exam.MaPhienBanHienHanh is not { } currentId || selectedIds.Count != 200 ||
            selectedIds.Distinct().Count() != 200)
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Cần chọn đúng 200 câu hỏi khác nhau.", 409);
        var currentVersion = await db.PhienBanDeThi.AsNoTracking().SingleAsync(
            v => v.MaDeThi == exam.MaDeThi && v.MaPhienBanDeThi == currentId, ct);
        var current = await ReadAsync(currentVersion, ct);
        var candidates = await generation.BuildAdminCandidatesAsync(ct);
        var candidateByQuestion = candidates.SelectMany(unit => unit.Questions.Select(q => (q.MaCauHoi, unit)))
            .ToDictionary(x => x.MaCauHoi, x => x.unit);
        var oldById = current.Questions.ToDictionary(q => q.Id);
        var oldGroups = current.Groups.ToDictionary(g => g.Id);
        var sourceIds = await db.CauHoi.AsNoTracking().Where(q => selectedIds.Contains(q.MaCauHoi))
            .Select(q => q.MaCauHoi).ToArrayAsync(ct);
        if (sourceIds.Length != 200)
            throw new ExamProblem("DELETED_QUESTION_SELECTED",
                "Đề đang soạn còn câu đã xóa khỏi ngân hàng. Hãy thay câu/nhóm đó trước khi xuất bản.", 409);
        var questions = new List<VersionQuestion>(200);
        var groups = new List<VersionGroup>();
        var partOrders = new Dictionary<int, int>();
        for (var index = 0; index < selectedIds.Count;)
        {
            var id = selectedIds[index];
            List<VersionQuestion> members;
            VersionGroup? group;
            if (oldById.TryGetValue(id, out var old))
            {
                group = old.GroupId is { } gid ? oldGroups[gid] : null;
                members = group is null ? [old] : current.Questions.Where(q => q.GroupId == group.Id)
                    .OrderBy(q => q.Order).ToList();
            }
            else if (candidateByQuestion.TryGetValue(id, out var unit))
            {
                group = unit.Resource is { } resource ? new VersionGroup(resource.MaNguLieu,
                    unit.Part, 0, resource.NoiDungNguLieu, resource.NoiDungDich,
                    resource.DuongDanAudio, resource.DuongDanAnh,
                    unit.Documents.Select(d => new ExamDocumentSnapshot(d.ThuTu,
                        d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh)).ToList()) : null;
                members = unit.Questions.Select(q => new VersionQuestion(q.MaCauHoi, unit.Part,
                    0, 0, unit.Resource?.MaNguLieu, q.NoiDung, q.PhuongAnA,
                    q.PhuongAnB, q.PhuongAnC, q.PhuongAnD, q.PhuongAnDung, q.GiaiThich,
                    q.DoKho)).ToList();
            }
            else throw new ExamProblem("QUESTION_NOT_AVAILABLE",
                $"Câu {id} không còn hợp lệ trong ngân hàng nguồn.", 409);
            if (index + members.Count > selectedIds.Count ||
                !selectedIds.Skip(index).Take(members.Count).SequenceEqual(members.Select(q => q.Id)))
                throw new ExamProblem("GROUP_SPLIT",
                    "Các câu trong cùng nhóm phải liền nhau và giữ đúng thứ tự.", 409);
            if (group is not null)
            {
                if (groups.Any(g => g.Id == group.Id))
                    throw new ExamProblem("DUPLICATE_GROUP", "Nhóm câu hỏi bị chọn trùng.", 409);
                groups.Add(group with { Order = groups.Count + 1 });
            }
            foreach (var q in members)
            {
                partOrders[q.Part] = partOrders.GetValueOrDefault(q.Part) + 1;
                questions.Add(q with { Order = questions.Count + 1, PartOrder = partOrders[q.Part] });
            }
            index += members.Count;
        }
        if (ExamCore.PartCounts.Any(p => questions.Count(q => q.Part == p.Key) != p.Value))
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Số câu từng Part không đúng cấu trúc TOEIC.", 409);
        return new VersionedExam(1, exam.MaDeThi, number, exam.TenDe,
            ExamCore.DurationMinutes, questions, groups);
    }

    private async Task<VersionedExam> ArchiveMediaAsync(VersionedExam source, string prefix, CancellationToken ct)
    {
        var paths = source.Groups.SelectMany(g => new[] { g.Audio, g.Image }
            .Concat(g.Documents.Select(d => d.ImagePath)))
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!)
            .Distinct(StringComparer.Ordinal).ToArray();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var container = blobs.GetBlobContainerClient(containerName);
        foreach (var path in paths)
        {
            var input = container.GetBlobClient(path);
            var props = await input.GetPropertiesAsync(cancellationToken: ct);
            if (path.StartsWith("exam-snapshots/", StringComparison.Ordinal))
            {
                // Existing published media is already immutable and independently retained.
                mapped.Add(path, path);
                continue;
            }
            var outputPath = $"{prefix}/media/{Guid.NewGuid():N}{Path.GetExtension(path).ToLowerInvariant()}";
            await using var read = await input.OpenReadAsync(cancellationToken: ct);
            using var memory = new MemoryStream();
            await read.CopyToAsync(memory, ct);
            var originalHash = Hash(memory.ToArray());
            memory.Position = 0;
            var output = container.GetBlobClient(outputPath);
            await output.UploadAsync(memory, new BlobUploadOptions {
                HttpHeaders = new BlobHttpHeaders { ContentType = props.Value.ContentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, ct);
            var copy = (await output.DownloadContentAsync(ct)).Value.Content.ToArray();
            if (Hash(copy) != originalHash)
                throw new ExamProblem("MEDIA_ARCHIVE_FAILED", "Không xác minh được bản media lịch sử trên Azure Blob.", 503);
            mapped.Add(path, outputPath);
        }
        string? Archive(string? path) => path is null ? null : mapped[path];
        return source with { Groups = source.Groups.Select(g => g with {
            Audio = Archive(g.Audio), Image = Archive(g.Image),
            Documents = g.Documents.Select(d => d with { ImagePath = Archive(d.ImagePath) }).ToList()
        }).ToList() };
    }

    public static ExamSnapshot ToAttempt(VersionedExam version, KetQuaLamBai attempt,
        CompactExamData data, SnapshotView view)
    {
        var groups = view == SnapshotView.Result ? [] : version.Groups.Select(g => new NhomLuotLam {
            MaNhomLuotLam = g.Id, ThuTu = g.Order,
            MaPartNavigation = new PartTOEIC { SoPart = g.Part },
            NoiDungNguLieu = g.Text, NoiDungDich = g.Translation,
            DuongDanAudio = g.Audio, DuongDanAnh = g.Image,
            TaiLieuJson = g.Documents.Count == 0 ? null : ExamDocumentCodec.Encode(g.Documents)
        }).ToList();
        var questions = version.Questions.Select(q => new CauHoiLuotLam {
            MaCauHoiLuotLam = q.Id, MaCauHoiGoc = q.Id, MaNhomLuotLam = q.GroupId,
            ThuTu = q.Order, ThuTuTrongPart = q.PartOrder,
            MaPartNavigation = new PartTOEIC { SoPart = q.Part },
            NoiDung = view == SnapshotView.Active && q.Part is 1 or 2 ? null : q.Text,
            PhuongAnA = view == SnapshotView.Active && q.Part is 1 or 2 ? null : q.A,
            PhuongAnB = view == SnapshotView.Active && q.Part is 1 or 2 ? null : q.B,
            PhuongAnC = view == SnapshotView.Active && q.Part is 1 or 2 ? null : q.C,
            PhuongAnD = view == SnapshotView.Active && q.Part is 1 or 2 ? null : q.D,
            PhuongAnDung = q.Correct, GiaiThich = q.Explanation
        }).ToList();
        var answers = data.Answers.ToDictionary(p => p.Key, p => new ChiTietKetQua {
            MaCauHoiLuotLam = p.Key, DapAnChon = p.Value.Selected, DanhDau = p.Value.Flagged
        });
        return new ExamSnapshot(attempt, version.Name, groups, questions, answers);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
