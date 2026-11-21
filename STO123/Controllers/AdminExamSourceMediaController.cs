using Azure.Storage.Blobs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG")]
[ApiController]
[Route("api/admin/content/groups")]
public sealed class AdminExamSourceMediaController(ToeicDbContext db, BlobServiceClient blobs,
    IConfiguration configuration, ExamVersionStore versions) : ControllerBase
{
    public sealed record MediaLinkRequest(string? Path);
    public sealed record GroupContentRequest(string? Text, string? Translation);
    public sealed record DocumentRequest(string Type, string? Content, string? ImagePath);
    public sealed record UnitResourceRequest(string? Text, string? Translation,
        string? AudioPath, string? ImagePath, List<DocumentRequest> Documents,
        List<AdminQuestionAuthoringController.QuestionInput>? Questions = null);

    private async Task<int[]> LegacyDependencies(int[] memberIds, CancellationToken ct)
    {
        var ids = memberIds.ToHashSet();
        var attempts = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaPhienBanDeThi == null &&
            a.DuLieuBaiLamJson != null && (a.LoaiBaiLam == ExamCore.Mock || a.LoaiBaiLam == ExamCore.Placement))
            .Select(a => new { a.MaKetQua, a.DuLieuBaiLamJson }).ToArrayAsync(ct);
        return attempts.Where(a => CompactExamStore.Decode(a.DuLieuBaiLamJson)
            .QuestionIds.Any(ids.Contains)).Select(a => a.MaKetQua).ToArray();
    }

    [HttpPut("{groupId:int}/unit")]
    [HttpPut("{groupId:int}/studio")]
    public async Task<IActionResult> UpdateUnit(int groupId, [FromBody] UnitResourceRequest request,
        CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var editorId)) return Forbid();
        if (request.Documents is null) return BadRequest(new { code = "INVALID_DOCUMENT" });
        var members = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaNguLieu == groupId)
            .OrderBy(x => x.ThuTu)
            .Select(x => new { x.MaCauHoi, part = x.MaCauHoiNavigation.MaPartNavigation.SoPart,
                bank = x.MaCauHoiNavigation.LoaiCauHoi }).ToArrayAsync(ct);
        if (members.Length == 0) return NotFound(new { code = "GROUP_NOT_FOUND" });
        if (members.Select(x => x.part).Distinct().Count() != 1 ||
            members.Select(x => x.bank).Distinct().Count() != 1)
            return Conflict(new { code = "INVALID_GROUP" });
        var part = members[0].part;
        var bank = members[0].bank;
        if (request.Questions is not null)
        {
            if (part != 7 || request.Questions.Count is < 2 or > 5)
                return BadRequest(new { code = "STUDIO_QUESTION_COUNT" });
            var suppliedIds = request.Questions.Where(q => q is not null && q.Id.HasValue)
                .Select(q => q.Id!.Value).ToArray();
            if (suppliedIds.Length != suppliedIds.Distinct().Count() ||
                suppliedIds.Any(id => !members.Any(member => member.MaCauHoi == id)) ||
                request.Questions.Count != members.Length && suppliedIds.Length == 0)
                return BadRequest(new { code = "STUDIO_QUESTION_IDS" });
            var memberIdsForLinks = members.Select(member => member.MaCauHoi).ToArray();
            var membershipChanged = suppliedIds.Length > 0 &&
                (suppliedIds.Length != members.Length || !suppliedIds.SequenceEqual(memberIdsForLinks)) ||
                request.Questions.Count != members.Length;
            if (membershipChanged &&
                await db.CauHoiDeThi.AsNoTracking().AnyAsync(link =>
                    memberIdsForLinks.Contains(link.MaCauHoi), ct))
                return Conflict(new { code = "GROUP_LINKED_TO_EXAM",
                    message = "Nhóm đang thuộc đề thi; không thể thay đổi số câu." });
            for (var index = 0; index < request.Questions.Count; index++)
            {
                var input = request.Questions[index];
                if (input is null || QuestionContentRules.InvalidField(7, input.Text,
                        input.Explanation, input.Translation) is not null ||
                    input.Difficulty is < 1 or > 3 ||
                    new[] { input.A, input.B, input.C, input.D }.Any(x =>
                        string.IsNullOrWhiteSpace(x) || x.Length > 512) ||
                    input.Correct?.Trim().ToUpperInvariant() is not ("A" or "B" or "C" or "D"))
                    return BadRequest(new { code = "INVALID_QUESTION", question = index + 1 });
            }
        }
        var resource = new NguLieu {
            NoiDungNguLieu = request.Text?.Trim(), NoiDungDich = request.Translation?.Trim(),
            DuongDanAudio = request.AudioPath?.Trim(), DuongDanAnh = request.ImagePath?.Trim()
        };
        var docs = request.Documents.Select((d, index) => new NguLieuTaiLieu {
            MaNguLieu = groupId, ThuTu = index + 1,
            LoaiTaiLieu = d.Type?.Trim().ToUpperInvariant(),
            NoiDung = d.Content, DuongDanAnh = d.ImagePath?.Trim()
        }).ToArray();
        if (!ExamSourceValidator.ValidMedia(resource, part) ||
            !ExamSourceValidator.ValidDocuments(part, docs) ||
            part != 7 && docs.Length > 0 ||
            part == 6 && !QuestionContentRules.ValidPart6(resource.NoiDungDich))
            return BadRequest(new { code = "INVALID_RESOURCE", message = "Ngữ liệu không đúng cấu trúc Part." });
        if (part == 7 && !Part7TranslationCodec.IsValid(resource.NoiDungDich, docs))
            return BadRequest(new { code = "INVALID_PART7_TRANSLATION",
                message = "Bản dịch Part 7 không khớp cấu trúc tài liệu gốc." });
        var paths = new[] { resource.DuongDanAudio, resource.DuongDanAnh }
            .Concat(docs.SelectMany(ExamDocumentCodec.ImagePaths)).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!).Distinct().ToArray();
        if (paths.Any(path => path.Length > 512 || path.StartsWith('/') ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')))
            return BadRequest(new { code = "INVALID_MEDIA_PATH" });
        var containerName = bank == "DE_THI" ?
            configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media" : "practice-question";
        var container = blobs.GetBlobContainerClient(containerName);
        await using var lease = await ExamMediaPathLease.AcquireManyAsync(
            db.Database.GetConnectionString()!, paths.Select(path => (containerName, path)), ct);
        var media = new[] { (Path: resource.DuongDanAudio, Kind: "AUDIO"),
            (Path: resource.DuongDanAnh, Kind: "IMAGE") }
            .Concat(docs.SelectMany(ExamDocumentCodec.ImagePaths)
                .Select(path => (Path: (string?)path, Kind: "IMAGE")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Path)).Distinct().ToArray();
        foreach (var item in media)
        {
            var blob = container.GetBlobClient(item.Path!);
            if (!(await blob.ExistsAsync(ct)).Value)
                return BadRequest(new { code = "MEDIA_NOT_FOUND", message = $"Không tìm thấy media {item.Path}." });
            var type = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentType;
            var extension = System.IO.Path.GetExtension(item.Path!).ToLowerInvariant();
            if (item.Kind == "AUDIO" && (extension != ".mp3" ||
                type is not ("audio/mpeg" or "application/octet-stream")) ||
                item.Kind == "IMAGE" && (extension is not (".jpg" or ".jpeg" or ".png" or ".webp") ||
                type is not ("image/jpeg" or "image/png" or "image/webp" or "application/octet-stream")))
                return BadRequest(new { code = "MEDIA_TYPE_MISMATCH",
                    message = $"Media không đúng loại: {item.Path}." });
        }
        try
        {
            var affected = await versions.ApplySourceEditAsync(
                members.Select(x => x.MaCauHoi).ToArray(), async token => {
                    var current = await db.NguLieu.FromSqlInterpolated(
                        $"SELECT * FROM dbo.NguLieu WITH (UPDLOCK, HOLDLOCK) WHERE MaNguLieu = {groupId}")
                        .SingleOrDefaultAsync(token) ?? throw new ExamProblem(
                            "GROUP_NOT_FOUND", "Không tìm thấy ngữ liệu.", 404);
                    if ((await LegacyDependencies(members.Select(x => x.MaCauHoi).ToArray(), token)).Length > 0)
                        throw new ExamProblem("LEGACY_ATTEMPT_DEPENDENCY",
                            "Lượt thi cũ còn đọc ngữ liệu nguồn; chưa thể sửa an toàn.", 409);
                    current.NoiDungNguLieu = resource.NoiDungNguLieu;
                    current.NoiDungDich = resource.NoiDungDich;
                    current.DuongDanAudio = resource.DuongDanAudio;
                    current.DuongDanAnh = resource.DuongDanAnh;
                    var old = await db.NguLieuTaiLieu.Where(x => x.MaNguLieu == groupId).ToListAsync(token);
                    db.NguLieuTaiLieu.RemoveRange(old);
                    await db.SaveChangesAsync(token);
                    db.NguLieuTaiLieu.AddRange(docs);
                    if (request.Questions is not null)
                    {
                        var oldIds = members.Select(member => member.MaCauHoi).ToArray();
                        var requestedIds = request.Questions.Select((input, index) =>
                            input.Id ?? (request.Questions.Count == oldIds.Length &&
                                request.Questions.All(q => !q.Id.HasValue) ? oldIds[index] : 0)).ToArray();
                        var changed = requestedIds.Length != oldIds.Length ||
                            !requestedIds.SequenceEqual(oldIds);
                        if (changed && await db.CauHoiDeThi.AsNoTracking()
                            .AnyAsync(link => oldIds.Contains(link.MaCauHoi), token))
                            throw new ExamProblem("GROUP_LINKED_TO_EXAM",
                                "Nhóm đang thuộc đề thi; không thể thay đổi số hoặc thứ tự câu.", 409);
                        var firstQuestion = await db.CauHoi.SingleAsync(q => q.MaCauHoi == oldIds[0], token);
                        var ordered = new List<CauHoi>();
                        for (var index = 0; index < request.Questions.Count; index++)
                        {
                            var id = requestedIds[index];
                            var question = id == 0 ? new CauHoi {
                                MaPart = firstQuestion.MaPart, LoaiCauHoi = bank,
                                TrangThai = ExamCore.Published
                            } : await db.CauHoi.FromSqlInterpolated(
                                $"SELECT * FROM dbo.CauHoi WITH (UPDLOCK, HOLDLOCK) WHERE MaCauHoi = {id}")
                                .SingleOrDefaultAsync(token) ?? throw new ExamProblem(
                                    "QUESTION_NOT_FOUND", "Câu hỏi trong nhóm không còn tồn tại.", 409);
                            var input = request.Questions[index];
                            question.NoiDung = input.Text?.Trim();
                            question.PhuongAnA = input.A?.Trim();
                            question.PhuongAnB = input.B?.Trim();
                            question.PhuongAnC = input.C?.Trim();
                            question.PhuongAnD = input.D?.Trim();
                            question.PhuongAnDung = input.Correct?.Trim().ToUpperInvariant();
                            question.DoKho = input.Difficulty;
                            question.GiaiThich = input.Explanation?.Trim();
                            if (!ExamSourceValidator.ValidQuestion(question, 7))
                                throw new ExamProblem("INVALID_QUESTION", $"Câu hỏi {index + 1} không hợp lệ.");
                            if (id == 0) db.CauHoi.Add(question);
                            ordered.Add(question);
                        }
                        if (changed)
                        {
                            var oldMemberships = await db.NhomCauHoi
                                .Where(item => item.MaNguLieu == groupId).ToListAsync(token);
                            db.NhomCauHoi.RemoveRange(oldMemberships);
                            await db.SaveChangesAsync(token);
                            var removed = oldIds.Except(requestedIds).ToArray();
                            if (removed.Length > 0)
                            {
                                await db.CauHoiLuotLam.Where(q => q.MaCauHoiGoc != null &&
                                    removed.Contains(q.MaCauHoiGoc.Value))
                                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.MaCauHoiGoc, (int?)null), token);
                                foreach (var id in removed)
                                    await db.Database.ExecuteSqlInterpolatedAsync(
                                        $"DELETE FROM dbo.ChiTietPhanLoai WHERE MaCauHoi = {id}", token);
                                db.CauHoi.RemoveRange(await db.CauHoi.Where(q =>
                                    removed.Contains(q.MaCauHoi)).ToArrayAsync(token));
                            }
                            await db.SaveChangesAsync(token);
                            db.NhomCauHoi.AddRange(ordered.Select((question, index) => new NhomCauHoi {
                                MaCauHoi = question.MaCauHoi, MaNguLieu = groupId, ThuTu = index + 1
                            }));
                        }
                    }
                }, editorId, ct);
            var questionIds = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaNguLieu == groupId)
                .OrderBy(x => x.ThuTu).Select(x => x.MaCauHoi).ToArrayAsync(ct);
            return Ok(new { groupId, questionIds, affectedExamIds = affected });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPut("{groupId:int}/documents")]
    public async Task<IActionResult> UpdateDocuments(int groupId,
        [FromBody] List<DocumentRequest> request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var editorId)) return Forbid();
        if (request is null || request.Count is < 1 or > 3)
            return BadRequest(new { code = "INVALID_DOCUMENT_COUNT" });
        var memberIds = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaNguLieu == groupId)
            .Select(x => x.MaCauHoi).ToArrayAsync(ct);
        if (memberIds.Length == 0) return NotFound(new { code = "GROUP_NOT_FOUND" });
        var part = await db.CauHoi.AsNoTracking().Where(x => memberIds.Contains(x.MaCauHoi))
            .Select(x => x.MaPartNavigation.SoPart).Distinct().ToArrayAsync(ct);
        if (part.Length != 1 || part[0] != 7)
            return BadRequest(new { code = "PART_7_REQUIRED" });
        var documents = request.Select((item, index) => new NguLieuTaiLieu {
            MaNguLieu = groupId, ThuTu = index + 1,
            LoaiTaiLieu = item.Type?.Trim().ToUpperInvariant(),
            NoiDung = item.Content, DuongDanAnh = item.ImagePath?.Trim()
        }).ToArray();
        if (!ExamSourceValidator.ValidDocuments(7, documents))
            return BadRequest(new { code = "INVALID_DOCUMENT", message = "Tài liệu Part 7 không hợp lệ." });
        var currentTranslation = await db.NguLieu.AsNoTracking().Where(x => x.MaNguLieu == groupId)
            .Select(x => x.NoiDungDich).SingleOrDefaultAsync(ct);
        if (!Part7TranslationCodec.IsValid(currentTranslation, documents))
            return BadRequest(new { code = "TRANSLATION_SHAPE_MISMATCH",
                message = "Cấu trúc tài liệu đã đổi. Hãy sửa bản dịch tương ứng trước khi lưu." });
        var banks = await db.CauHoi.AsNoTracking().Where(x => memberIds.Contains(x.MaCauHoi))
            .Select(x => x.LoaiCauHoi).Distinct().ToArrayAsync(ct);
        if (banks.Length != 1 || banks[0] is not ("DE_THI" or "LUYEN_TAP"))
            return Conflict(new { code = "GROUP_BANK_MISMATCH" });
        var containerName = banks[0] == "DE_THI" ?
            configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media" : "practice-question";
        var paths = documents.SelectMany(ExamDocumentCodec.ImagePaths).Distinct().ToArray();
        await using var lease = await ExamMediaPathLease.AcquireManyAsync(
            db.Database.GetConnectionString()!, paths.Select(path => (containerName, path)), ct);
        var container = blobs.GetBlobContainerClient(containerName);
        foreach (var path in paths)
        {
            if (path.Length > 512 || path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) ||
                path.Contains('\\') || !(await container.GetBlobClient(path).ExistsAsync(ct)).Value)
                return BadRequest(new { code = "MEDIA_NOT_FOUND" });
            var contentType = (await container.GetBlobClient(path).GetPropertiesAsync(cancellationToken: ct)).Value.ContentType;
            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp") ||
                contentType is not ("image/jpeg" or "image/png" or "image/webp" or "application/octet-stream"))
                return BadRequest(new { code = "MEDIA_TYPE_MISMATCH" });
        }
        try
        {
            var affected = await versions.ApplySourceEditAsync(memberIds, async token => {
                if ((await LegacyDependencies(memberIds, token)).Length > 0)
                    throw new ExamProblem("LEGACY_ATTEMPT_DEPENDENCY",
                        "Lượt thi cũ còn đọc tài liệu nguồn; chưa thể sửa an toàn.", 409);
                var old = await db.NguLieuTaiLieu.Where(x => x.MaNguLieu == groupId).ToListAsync(token);
                db.NguLieuTaiLieu.RemoveRange(old);
                await db.SaveChangesAsync(token);
                db.NguLieuTaiLieu.AddRange(documents);
            }, editorId, ct);
            return Ok(new { groupId, documentCount = documents.Length, affectedExamIds = affected });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPut("{groupId:int}/content")]
    public async Task<IActionResult> UpdateContent(int groupId, [FromBody] GroupContentRequest request,
        CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var editorId)) return Forbid();
        var memberIds = await db.NhomCauHoi.AsNoTracking().Where(m => m.MaNguLieu == groupId)
            .Select(m => m.MaCauHoi).ToArrayAsync(ct);
        if (memberIds.Length == 0) return Conflict(new { code = "GROUP_EMPTY",
            message = "Nhóm không còn câu thành viên; vui lòng tải lại." });
        try
        {
            var affected = await versions.ApplySourceEditAsync(memberIds, async token => {
                var group = await db.NguLieu.FromSqlInterpolated(
                    $"SELECT * FROM dbo.NguLieu WITH (UPDLOCK, HOLDLOCK) WHERE MaNguLieu = {groupId}")
                    .SingleOrDefaultAsync(token) ?? throw new ExamProblem(
                        "GROUP_NOT_FOUND", "Không tìm thấy nhóm.", 404);
                if ((await LegacyDependencies(memberIds, token)).Length > 0)
                    throw new ExamProblem("LEGACY_ATTEMPT_DEPENDENCY",
                        "Lượt thi cũ còn đọc ngữ liệu nguồn; chưa thể sửa an toàn.", 409);
                var part = await db.CauHoi.AsNoTracking().Where(q => memberIds.Contains(q.MaCauHoi))
                    .Select(q => q.MaPartNavigation.SoPart).FirstOrDefaultAsync(token);
                group.NoiDungNguLieu = request.Text?.Trim();
                group.NoiDungDich = request.Translation?.Trim();
                if (part == 6 && !QuestionContentRules.ValidPart6(group.NoiDungDich))
                    throw new ExamProblem("PART6_TRANSLATION_REQUIRED",
                        "Cần bản dịch cho đủ bốn câu Part 6 theo thứ tự [1]–[4].");
                if (!ExamSourceValidator.ValidMedia(group, part))
                    throw new ExamProblem("INVALID_GROUP_CONTENT",
                        "Ngữ liệu chưa đủ nội dung bắt buộc cho Part này.");
                if (part == 7)
                {
                    var documents = await db.NguLieuTaiLieu.AsNoTracking()
                        .Where(x => x.MaNguLieu == groupId).OrderBy(x => x.ThuTu).ToArrayAsync(token);
                    if (!Part7TranslationCodec.IsValid(group.NoiDungDich, documents))
                        throw new ExamProblem("INVALID_PART7_TRANSLATION",
                            "Bản dịch Part 7 không khớp cấu trúc tài liệu gốc.");
                }
            }, editorId, ct);
            return Ok(new { groupId, text = request.Text?.Trim(),
                translation = request.Translation?.Trim(), affectedExamIds = affected });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? part,
        [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (part is < 1 or > 7 || page < 1 || pageSize is < 1 or > 50)
            return BadRequest(new { code = "INVALID_FILTER", message = "Bộ lọc không hợp lệ." });
        var query = db.NguLieu.AsNoTracking().Where(g => g.NhomCauHoi.Any());
        if (part.HasValue)
            query = query.Where(g => g.NhomCauHoi.Any(m => m.MaCauHoiNavigation.MaPartNavigation.SoPart == part));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(g => g.MaNguLieu.ToString().Contains(term) ||
                g.NoiDungNguLieu != null && g.NoiDungNguLieu.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(g => g.MaNguLieu).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(g => new {
                groupId = g.MaNguLieu, part = g.NhomCauHoi.Select(m => m.MaCauHoiNavigation.MaPartNavigation.SoPart).FirstOrDefault(),
                questionIds = g.NhomCauHoi.OrderBy(m => m.ThuTu).Select(m => m.MaCauHoi).ToArray(),
                bank = g.NhomCauHoi.Select(m => m.MaCauHoiNavigation.LoaiCauHoi).FirstOrDefault(),
                text = g.NoiDungNguLieu, audio = g.DuongDanAudio, image = g.DuongDanAnh,
                examCount = g.NhomCauHoi.SelectMany(m => m.MaCauHoiNavigation.CauHoiDeThi)
                    .Select(l => l.MaDeThi).Distinct().Count()
            }).ToListAsync(ct);
        return Ok(new { items = rows, total, page, pageSize });
    }

    [HttpPut("{groupId:int}/media/{kind:regex(audio|image)}")]
    public async Task<IActionResult> Link(int groupId, string kind,
        [FromBody] MediaLinkRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var editorId)) return Forbid();
        var path = request.Path?.Trim();
        if (path is { Length: > 512 } || path?.Contains("..", StringComparison.Ordinal) == true ||
            path?.StartsWith('/') == true || path?.Contains('\\') == true)
            return BadRequest(new { code = "INVALID_PATH", message = "Đường dẫn media không hợp lệ." });
        await using var mediaLease = string.IsNullOrWhiteSpace(path) ? null :
            await ExamMediaPathLease.AcquireManyAsync(db.Database.GetConnectionString()!,
                [(configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media", path),
                    ("practice-question", path)], ct);
        var banks = await db.NhomCauHoi.AsNoTracking().Where(m => m.MaNguLieu == groupId)
            .Select(m => m.MaCauHoiNavigation.LoaiCauHoi).Distinct().ToArrayAsync(ct);
        if (banks.Length != 1 || banks[0] is not ("DE_THI" or "LUYEN_TAP"))
            return Conflict(new { code = "INVALID_GROUP_BANK", message = "Ngân hàng của nhóm câu không nhất quán." });
        var memberIds = await db.NhomCauHoi.AsNoTracking().Where(m => m.MaNguLieu == groupId)
            .Select(m => m.MaCauHoi).ToArrayAsync(ct);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var containerName = banks[0] == "DE_THI" ?
                configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media" : "practice-question";
            var blob = blobs.GetBlobContainerClient(containerName).GetBlobClient(path);
            if (!(await blob.ExistsAsync(ct)).Value)
                return BadRequest(new { code = "MEDIA_NOT_FOUND", message = "Tệp chưa có trên Azure Blob." });
            var type = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentType;
            if (kind == "audio" && type != "audio/mpeg" ||
                kind == "image" && type is not ("image/jpeg" or "image/png" or "image/webp"))
                return BadRequest(new { code = "MEDIA_TYPE_MISMATCH", message = "Định dạng media không đúng." });
        }
        try
        {
            var affected = await versions.ApplySourceEditAsync(memberIds, async token => {
                var group = await db.NguLieu.FromSqlInterpolated(
                    $"SELECT * FROM dbo.NguLieu WITH (UPDLOCK, HOLDLOCK) WHERE MaNguLieu = {groupId}")
                    .SingleOrDefaultAsync(token) ?? throw new ExamProblem(
                        "GROUP_NOT_FOUND", "Không tìm thấy nhóm câu hỏi.", 404);
                if ((await LegacyDependencies(memberIds, token)).Length > 0)
                    throw new ExamProblem("LEGACY_ATTEMPT_DEPENDENCY",
                        "Lượt thi cũ còn đọc media nguồn; chưa thể sửa an toàn.", 409);
                if (kind == "audio") group.DuongDanAudio = path;
                else group.DuongDanAnh = path;
            }, editorId, ct);
            return Ok(new { groupId, kind, path, affectedExamIds = affected });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }
}
