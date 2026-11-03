using Azure.Storage.Blobs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/content/question-authoring")]
public sealed class AdminQuestionAuthoringController(ToeicDbContext db,
    BlobServiceClient blobs, IConfiguration configuration, ExamVersionStore versions) : ControllerBase
{
    public sealed record QuestionInput(string? Text, string A, string B, string C,
        string? D, string Correct, byte Difficulty, string? Explanation, string? Translation = null);
    public sealed record DocumentInput(string Type, string? Content, string? ImagePath);
    public sealed record UnitInput(int Part, string Bank, string? Text, string? Translation,
        string? AudioPath, string? ImagePath, List<DocumentInput> Documents,
        List<QuestionInput> Questions);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var row = await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == id)
            .Select(q => new { id = q.MaCauHoi, part = q.MaPartNavigation.SoPart,
                bank = q.LoaiCauHoi, groupId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
                text = q.NoiDung, a = q.PhuongAnA, b = q.PhuongAnB, c = q.PhuongAnC,
                d = q.PhuongAnD, correct = q.PhuongAnDung,
                difficulty = q.DoKho, explanation = q.GiaiThich,
                translation = q.MaPartNavigation.SoPart == 2 ? q.NhomCauHoi!.MaNguLieuNavigation.NoiDungDich :
                    q.MaPartNavigation.SoPart == 6 ? q.NhomCauHoi!.MaNguLieuNavigation.NoiDungDich : null,
                groupOrder = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.ThuTu,
                examCount = q.CauHoiDeThi.Count }).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound(new { code = "QUESTION_NOT_FOUND",
            message = "Không tìm thấy câu hỏi." });
        return Ok(new { row.id, row.part, row.bank, row.groupId, row.text,
            row.a, row.b, row.c, row.d, row.correct, row.difficulty, row.explanation,
            translation = row.part == 6 ? QuestionContentRules.Part6Translation(row.translation, row.groupOrder ?? 0) : row.translation,
            row.examCount });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] QuestionInput input, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var editorId)) return Forbid();
        try
        {
            var affected = await versions.ApplySourceEditAsync([id], async token => {
                var question = await db.CauHoi.FromSqlInterpolated(
                    $"SELECT * FROM dbo.CauHoi WITH (UPDLOCK, HOLDLOCK) WHERE MaCauHoi = {id}")
                    .SingleOrDefaultAsync(token) ?? throw new ExamProblem(
                        "QUESTION_NOT_FOUND", "Không tìm thấy câu hỏi.", 404);
                var part = await db.PartTOEIC.AsNoTracking().Where(p => p.MaPart == question.MaPart)
                    .Select(p => p.SoPart).SingleAsync(token);
                var compact = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaPhienBanDeThi == null &&
                    a.DuLieuBaiLamJson != null &&
                    (a.LoaiBaiLam == ExamCore.Mock || a.LoaiBaiLam == ExamCore.Placement))
                    .Select(a => new { a.MaKetQua, a.DuLieuBaiLamJson }).ToListAsync(token);
                if (compact.Any(a => CompactExamStore.Decode(a.DuLieuBaiLamJson).QuestionIds.Contains(id)))
                    throw new ExamProblem("LEGACY_ATTEMPT_DEPENDENCY",
                        "Lượt thi cũ còn đọc câu từ nguồn; chưa thể sửa an toàn.", 409);
                question.NoiDung = input.Text?.Trim();
                question.PhuongAnA = input.A?.Trim();
                question.PhuongAnB = input.B?.Trim();
                question.PhuongAnC = input.C?.Trim();
                question.PhuongAnD = part == 2 ? null : input.D?.Trim();
                question.PhuongAnDung = input.Correct?.Trim().ToUpperInvariant();
                question.DoKho = input.Difficulty;
                question.GiaiThich = input.Explanation?.Trim();
                var invalidField = QuestionContentRules.InvalidField(part,
                    question.NoiDung, question.GiaiThich, input.Translation);
                if (invalidField is not null)
                    throw new ExamProblem("QUESTION_FIELD_REQUIRED", $"{invalidField} của câu #{id} không được để trống.");
                if (question.DoKho is < 1 or > 3 ||
                    question.PhuongAnA?.Length is < 1 or > 512 ||
                    question.PhuongAnB?.Length is < 1 or > 512 ||
                    question.PhuongAnC?.Length is < 1 or > 512 ||
                    question.PhuongAnD?.Length > 512 || !ExamSourceValidator.ValidQuestion(question, part))
                    throw new ExamProblem("INVALID_QUESTION",
                        "Nội dung, độ khó hoặc đáp án không hợp lệ.");
                if (part is 2 or 6)
                {
                    var membership = await db.NhomCauHoi.AsNoTracking()
                        .Where(x => x.MaCauHoi == id).Select(x => new { x.MaNguLieu, x.ThuTu })
                        .SingleOrDefaultAsync(token) ?? throw new ExamProblem("GROUP_NOT_FOUND", "Câu hỏi thiếu ngữ liệu nhóm.", 409);
                    var resource = await db.NguLieu.FromSqlInterpolated(
                        $"SELECT * FROM dbo.NguLieu WITH (UPDLOCK, HOLDLOCK) WHERE MaNguLieu = {membership.MaNguLieu}")
                        .SingleAsync(token);
                    if (part == 2)
                    {
                        if (input.Translation is not null)
                            resource.NoiDungDich = string.IsNullOrWhiteSpace(input.Translation) ? null : input.Translation.Trim();
                    }
                    else
                        resource.NoiDungDich = QuestionContentRules.ReplacePart6(
                            resource.NoiDungDich, membership.ThuTu, input.Translation) ??
                            throw new ExamProblem("PART6_TRANSLATION_REQUIRED",
                                "Cần nhập bản dịch cho cả bốn câu Part 6 trước khi lưu.");
                }
            }, editorId, ct);
            return Ok(new { id, updated = true, affectedExamIds = affected });
        }
        catch (ExamProblem e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UnitInput input, CancellationToken ct)
        => await CreateCore(input, null, ct);

    [HttpPost("exam/{examId:int}")]
    public async Task<IActionResult> CreateInExam(int examId, [FromBody] UnitInput input, CancellationToken ct)
        => await CreateCore(input, examId, ct);

    private async Task<IActionResult> CreateCore(UnitInput input, int? examId, CancellationToken ct)
    {
        if (examId.HasValue && input.Bank != "DE_THI")
            return BadRequest(new { code = "EXAM_BANK_REQUIRED", message = "Câu trong đề phải thuộc ngân hàng DE_THI." });
        if (input.Bank is not ("DE_THI" or "LUYEN_TAP") || input.Part is < 1 or > 7 ||
            input.Questions is null || input.Documents is null)
            return BadRequest(new { code = "INVALID_UNIT", message = "Part hoặc ngân hàng câu hỏi không hợp lệ." });
        if (input.Questions.Any(q => q is null) || input.Documents.Any(d => d is null))
            return BadRequest(new { code = "INVALID_UNIT", message = "Danh sách câu hỏi hoặc tài liệu có mục không hợp lệ." });
        var partId = await db.PartTOEIC.AsNoTracking().Where(p => p.SoPart == input.Part)
            .Select(p => (int?)p.MaPart).SingleOrDefaultAsync(ct);
        if (partId is null) return BadRequest(new { code = "INVALID_PART", message = "Part không tồn tại." });
        var expectedCount = input.Part switch { 1 or 2 or 5 => 1, 3 or 4 => 3, 6 => 4,
            7 when input.Questions.Count is >= 2 and <= 5 => input.Questions.Count, _ => 0 };
        if (expectedCount == 0 || input.Questions.Count != expectedCount)
            return BadRequest(new { code = "INVALID_GROUP_COUNT",
                message = "Số câu trong nhóm không đúng cấu trúc Part." });
        for (var index = 0; index < input.Questions.Count; index++)
        {
            var item = input.Questions[index];
            var invalidField = QuestionContentRules.InvalidField(input.Part,
                item.Text, item.Explanation, item.Translation);
            if (invalidField is not null)
                return BadRequest(new { code = "QUESTION_FIELD_REQUIRED",
                    question = index + 1, field = invalidField,
                    message = $"Câu {index + 1}: {invalidField} không được để trống." });
        }
        if (input.Part != 7 && input.Documents.Count > 0 || input.Part == 7 &&
            input.Documents.Count is < 1 or > 3)
            return BadRequest(new { code = "INVALID_DOCUMENT_COUNT",
                message = "Part 7 cần 1–3 tài liệu, Part khác không dùng tài liệu." });
        var questions = input.Questions.Select(q => new CauHoi {
            MaPart = partId.Value, NoiDung = q.Text?.Trim(),
            PhuongAnA = q.A?.Trim(), PhuongAnB = q.B?.Trim(),
            PhuongAnC = q.C?.Trim(), PhuongAnD = q.D?.Trim(),
            PhuongAnDung = q.Correct?.Trim().ToUpperInvariant(),
            DoKho = q.Difficulty, GiaiThich = q.Explanation?.Trim(),
            LoaiCauHoi = input.Bank, TrangThai = ExamCore.Published
        }).ToList();
        if (questions.Any(q => q.DoKho is < 1 or > 3 ||
            q.PhuongAnA?.Length is < 1 or > 512 || q.PhuongAnB?.Length is < 1 or > 512 ||
            q.PhuongAnC?.Length is < 1 or > 512 || q.PhuongAnD?.Length > 512 ||
            !ExamSourceValidator.ValidQuestion(q, input.Part)))
            return BadRequest(new { code = "INVALID_QUESTION",
                message = "Nội dung, độ khó hoặc đáp án của câu hỏi không hợp lệ." });
        var resource = input.Part == 5 ? null : new NguLieu {
            NoiDungNguLieu = input.Text?.Trim(),
            NoiDungDich = input.Part == 6 ? QuestionContentRules.FormatPart6(
                input.Questions.Select(q => q.Translation).ToArray()) :
                input.Part == 2 ? (input.Questions[0].Translation ?? input.Translation)?.Trim() : input.Translation?.Trim(),
            DuongDanAudio = input.AudioPath?.Trim(), DuongDanAnh = input.ImagePath?.Trim()
        };
        var docs = input.Documents.Select((d, index) => new NguLieuTaiLieu {
            ThuTu = index + 1, LoaiTaiLieu = d.Type?.Trim().ToUpperInvariant(),
            NoiDung = d.Content?.Trim(), DuongDanAnh = d.ImagePath?.Trim()
        }).ToList();
        if (resource is not null && (!ExamSourceValidator.ValidMedia(resource, input.Part) ||
            !ExamSourceValidator.ValidDocuments(input.Part, docs)))
            return BadRequest(new { code = "INVALID_RESOURCE",
                message = "Ngữ liệu, media hoặc tài liệu của nhóm chưa hợp lệ." });
        if (resource is null && (input.AudioPath is not null || input.ImagePath is not null ||
            input.Text is not null || input.Translation is not null))
            return BadRequest(new { code = "INVALID_PART_5_RESOURCE",
                message = "Part 5 là câu độc lập, không có ngữ liệu chung." });
        var mediaPaths = new (string? Path, string Kind)[] { (resource?.DuongDanAudio, "AUDIO"),
            (Path: resource?.DuongDanAnh, Kind: "IMAGE") }
            .Concat(docs.Select(d => (Path: (string?)d.DuongDanAnh, Kind: "IMAGE")))
            .Where(p => !string.IsNullOrWhiteSpace(p.Path)).Distinct().ToArray();
        var containerName = input.Bank == "DE_THI" ?
            configuration["ExamMedia:ContainerName"] ?? "toeic-exam-media" : "practice-question";
        var container = blobs.GetBlobContainerClient(containerName);
        await using var mediaLease = await ExamMediaPathLease.AcquireManyAsync(
            db.Database.GetConnectionString()!, mediaPaths.Select(p => (containerName, p.Path!)), ct);
        foreach (var (path, kind) in mediaPaths)
        {
            if (path!.Length > 512 || path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) ||
                path.Contains('\\') || !(await container.GetBlobClient(path).ExistsAsync(ct)).Value)
                return BadRequest(new { code = "MEDIA_NOT_FOUND",
                    message = $"Không tìm thấy tệp media: {path}" });
            var type = (await container.GetBlobClient(path).GetPropertiesAsync(cancellationToken: ct))
                .Value.ContentType;
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (kind == "AUDIO" && (extension != ".mp3" || type is not ("audio/mpeg" or "application/octet-stream")) ||
                kind == "IMAGE" && (extension is not (".jpg" or ".jpeg" or ".png" or ".webp") ||
                    type is not ("image/jpeg" or "image/png" or "image/webp" or "application/octet-stream")))
                return BadRequest(new { code = "MEDIA_TYPE_MISMATCH",
                    message = $"Tệp media không đúng loại audio hoặc hình ảnh: {path}" });
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        DeThi? exam = null;
        if (examId.HasValue)
        {
            exam = await db.DeThi.FromSqlInterpolated(
                $"SELECT * FROM dbo.DeThi WITH (UPDLOCK, HOLDLOCK) WHERE MaDeThi = {examId.Value}")
                .SingleOrDefaultAsync(ct);
            if (exam is null) return NotFound(new { code = "EXAM_NOT_FOUND" });
            if (exam.MaPhienBanHienHanh is not null || exam.TrangThai != "CLOSE")
                return Conflict(new { code = "EXAM_ALREADY_PUBLISHED", message = "Chỉ thêm câu vào đề chưa xuất bản." });
            var count = await db.CauHoiDeThi.CountAsync(x => x.MaDeThi == examId.Value, ct);
            var partCount = await db.CauHoiDeThi.CountAsync(x => x.MaDeThi == examId.Value &&
                x.MaCauHoiNavigation.MaPartNavigation.SoPart == input.Part, ct);
            var target = new[] { 6, 25, 39, 30, 30, 16, 54 }[input.Part - 1];
            if (count + questions.Count > 200 || partCount + questions.Count > target)
                return Conflict(new { code = "PART_FULL", message = $"Part {input.Part} đã đủ số câu." });
        }
        if (resource is not null)
        {
            db.NguLieu.Add(resource);
            await db.SaveChangesAsync(ct);
            foreach (var doc in docs) doc.MaNguLieu = resource.MaNguLieu;
            db.NguLieuTaiLieu.AddRange(docs);
        }
        db.CauHoi.AddRange(questions);
        await db.SaveChangesAsync(ct);
        if (resource is not null)
            db.NhomCauHoi.AddRange(questions.Select((q, index) => new NhomCauHoi {
                MaCauHoi = q.MaCauHoi, MaNguLieu = resource.MaNguLieu, ThuTu = index + 1
            }));
        if (exam is not null)
        {
            var lastOrder = await db.CauHoiDeThi.Where(x => x.MaDeThi == exam.MaDeThi)
                .MaxAsync(x => (int?)x.ThuTu, ct) ?? 0;
            db.CauHoiDeThi.AddRange(questions.Select((q, index) => new CauHoiDeThi {
                MaDeThi = exam.MaDeThi, MaCauHoi = q.MaCauHoi, ThuTu = lastOrder + index + 1
            }));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { groupId = resource?.MaNguLieu,
            questionIds = questions.Select(q => q.MaCauHoi).ToArray(), part = input.Part,
            bank = input.Bank, examId });
    }
}
