using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/content/questions")]
public sealed class AdminQuestionBankController(ToeicDbContext db,
    ExamGenerationService generation) : ControllerBase
{
    [HttpGet("units")]
    public async Task<IActionResult> Units([FromQuery] int? part, [FromQuery] string? bank,
        [FromQuery] string? search, [FromQuery] string scope = "all",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (part is < 1 or > 7 || bank is not (null or "DE_THI" or "LUYEN_TAP") ||
            scope is not ("all" or "groups" or "single") || page < 1 || pageSize is < 1 or > 50)
            return BadRequest(new { code = "INVALID_FILTER", message = "Bộ lọc không hợp lệ." });
        var query = db.CauHoi.AsNoTracking().AsQueryable();
        if (part.HasValue) query = query.Where(q => q.MaPartNavigation.SoPart == part);
        if (bank is not null) query = query.Where(q => q.LoaiCauHoi == bank);
        var rows = await query.Select(q => new {
            id = q.MaCauHoi, part = q.MaPartNavigation.SoPart, bank = q.LoaiCauHoi,
            text = q.NoiDung, groupId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
            order = q.NhomCauHoi == null ? 0 : q.NhomCauHoi.ThuTu,
            groupText = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.NoiDungNguLieu,
            audio = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.DuongDanAudio,
            image = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.DuongDanAnh,
            examCount = q.CauHoiDeThi.Count
        }).ToListAsync(ct);
        var usage = await db.CauHoiDeThi.AsNoTracking()
            .Select(link => new { link.MaCauHoi, link.MaDeThi,
                name = link.MaDeThiNavigation.TenDe, status = link.MaDeThiNavigation.TrangThai,
                current = link.MaDeThiNavigation.MaPhienBanHienHanh != null }).ToListAsync(ct);
        var keyword = search?.Trim();
        var groupSearch = keyword?.StartsWith("nhóm ", StringComparison.OrdinalIgnoreCase) == true ?
            keyword[5..].Trim() : keyword;
        var numericSearch = int.TryParse(keyword, out var number);
        var exactQuestionExists = numericSearch && rows.Any(q => q.id == number);
        var units = rows.GroupBy(q => q.groupId is null ? $"q{q.id}" : $"g{q.groupId}")
            .Select(group => {
                var members = group.OrderBy(q => q.order).ThenBy(q => q.id).ToArray();
                var first = members[0];
                var matching = string.IsNullOrWhiteSpace(keyword) ? [] : members.Where(q =>
                    numericSearch ? q.id == number : q.id.ToString().Contains(keyword) ||
                    (q.text?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)).Select(q => q.id).ToArray();
                var memberIds = members.Select(q => q.id).ToHashSet();
                return new { first.groupId, first.part, first.bank,
                    isGroup = first.groupId != null && first.part is 3 or 4 or 6 or 7,
                    questionIds = members.Select(q => q.id).ToArray(),
                    questionCount = members.Length, examCount = usage.Where(link => memberIds.Contains(link.MaCauHoi))
                        .Select(link => link.MaDeThi).Distinct().Count(),
                    exams = usage.Where(link => memberIds.Contains(link.MaCauHoi))
                        .GroupBy(link => link.MaDeThi).Select(g => new { examId = g.Key,
                            name = g.First().name, status = g.First().status,
                            current = g.First().current }).ToArray(),
                    preview = first.groupText ?? first.text, first.audio, first.image, matchingQuestionIds = matching,
                    matches = string.IsNullOrWhiteSpace(keyword) || matching.Length > 0 ||
                        (!exactQuestionExists && first.groupId?.ToString().Contains(groupSearch!) == true) ||
                        (!numericSearch && first.groupText?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (!numericSearch && usage.Any(link => memberIds.Contains(link.MaCauHoi) &&
                            link.name.Contains(keyword, StringComparison.OrdinalIgnoreCase))) };
            }).Where(u => u.matches && (scope == "all" || scope == "groups" && u.isGroup ||
                scope == "single" && !u.isGroup))
            .OrderBy(u => u.questionIds[0]).ToArray();
        return Ok(new { items = units.Skip((page - 1) * pageSize).Take(pageSize), total = units.Length, page, pageSize });
    }

    [HttpGet("units/{questionId:int}")]
    public async Task<IActionResult> UnitDetail(int questionId, CancellationToken ct)
    {
        var anchor = await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == questionId)
            .Select(q => new { groupId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
                part = q.MaPartNavigation.SoPart, bank = q.LoaiCauHoi }).SingleOrDefaultAsync(ct);
        if (anchor is null) return NotFound(new { code = "QUESTION_NOT_FOUND" });
        var members = await db.CauHoi.AsNoTracking().Where(q => anchor.groupId == null ?
                q.MaCauHoi == questionId : q.NhomCauHoi != null && q.NhomCauHoi.MaNguLieu == anchor.groupId)
            .Select(q => new { id = q.MaCauHoi, order = q.NhomCauHoi == null ? 0 : q.NhomCauHoi.ThuTu,
                text = q.NoiDung, a = q.PhuongAnA, b = q.PhuongAnB, c = q.PhuongAnC,
                d = q.PhuongAnD, correct = q.PhuongAnDung, explanation = q.GiaiThich,
                difficulty = q.DoKho, examCount = q.CauHoiDeThi.Count }).ToListAsync(ct);
        var resource = anchor.groupId is null ? null : await db.NguLieu.AsNoTracking()
            .Where(r => r.MaNguLieu == anchor.groupId).Select(r => new {
                text = r.NoiDungNguLieu, translation = r.NoiDungDich,
                audio = r.DuongDanAudio, image = r.DuongDanAnh }).SingleOrDefaultAsync(ct);
        var documents = anchor.groupId is null ? [] : await db.NguLieuTaiLieu.AsNoTracking()
            .Where(d => d.MaNguLieu == anchor.groupId).OrderBy(d => d.ThuTu)
            .Select(d => new { type = d.LoaiTaiLieu, content = d.NoiDung, image = d.DuongDanAnh }).ToArrayAsync(ct);
        var memberIds = members.Select(q => q.id).ToArray();
        var exams = await db.CauHoiDeThi.AsNoTracking().Where(x => memberIds.Contains(x.MaCauHoi))
            .Select(x => new { examId = x.MaDeThi, name = x.MaDeThiNavigation.TenDe,
                status = x.MaDeThiNavigation.TrangThai,
                current = x.MaDeThiNavigation.MaPhienBanHienHanh != null })
            .Distinct().ToArrayAsync(ct);
        return Ok(new { groupId = anchor.groupId, anchor.part, anchor.bank,
            questionCount = members.Count, resource, documents, exams,
            questions = members.OrderBy(q => q.order).ThenBy(q => q.id).ToArray() });
    }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? part, [FromQuery] string? bank,
        [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (part is < 1 or > 7 || bank is not (null or "DE_THI" or "LUYEN_TAP") ||
            page < 1 || pageSize is < 1 or > 50)
            return BadRequest(new { code = "INVALID_FILTER", message = "Bộ lọc không hợp lệ." });
        var query = db.CauHoi.AsNoTracking().AsQueryable();
        if (part.HasValue) query = query.Where(q => q.MaPartNavigation.SoPart == part);
        if (bank is not null) query = query.Where(q => q.LoaiCauHoi == bank);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(q => q.MaCauHoi.ToString().Contains(term) ||
                q.NoiDung != null && q.NoiDung.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(q => q.MaCauHoi).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(q => new {
                id = q.MaCauHoi, part = q.MaPartNavigation.SoPart,
                text = q.NoiDung, bank = q.LoaiCauHoi, difficulty = q.DoKho,
                groupId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
                examCount = q.CauHoiDeThi.Count
            }).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates([FromQuery] int? part,
        [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 50 || part is < 1 or > 7)
            return BadRequest(new { code = "INVALID_FILTER", message = "Bộ lọc không hợp lệ." });
        var units = await generation.BuildAdminCandidatesAsync(ct);
        var keyword = search?.Trim();
        var query = units.Where(u => !part.HasValue || u.Part == part.Value)
            .Where(u => string.IsNullOrWhiteSpace(keyword) ||
                u.Questions.Any(q => q.MaCauHoi.ToString().Contains(keyword) ||
                    (q.NoiDung?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)) ||
                (u.Resource?.NoiDungNguLieu?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
        var items = query.Skip((page - 1) * pageSize).Take(pageSize).Select(u => new {
            part = u.Part, groupId = u.Resource?.MaNguLieu,
            questionIds = u.Questions.Select(q => q.MaCauHoi).ToArray(),
            questionCount = u.Questions.Count,
            preview = u.Resource?.NoiDungNguLieu ?? u.Questions[0].NoiDung,
            difficulties = u.Questions.Select(q => q.DoKho).ToArray(),
            resource = u.Resource is null ? null : new {
                text = u.Resource.NoiDungNguLieu, translation = u.Resource.NoiDungDich,
                audio = u.Resource.DuongDanAudio, image = u.Resource.DuongDanAnh
            },
            documents = u.Documents.Select(d => new { type = d.LoaiTaiLieu,
                content = d.NoiDung, image = d.DuongDanAnh }).ToArray(),
            questions = u.Questions.Select(q => new { id = q.MaCauHoi, text = q.NoiDung,
                a = q.PhuongAnA, b = q.PhuongAnB, c = q.PhuongAnC, d = q.PhuongAnD,
                correct = q.PhuongAnDung, explanation = q.GiaiThich,
                difficulty = q.DoKho }).ToArray()
        }).ToArray();
        return Ok(new { items, total = query.Length, page, pageSize });
    }

    [HttpGet("{id:int}/dependencies")]
    public async Task<IActionResult> Dependencies(int id, CancellationToken ct)
    {
        var bank = await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == id)
            .Select(q => q.LoaiCauHoi).SingleOrDefaultAsync(ct);
        if (bank is null)
            return NotFound(new { code = "QUESTION_NOT_FOUND", message = "Không tìm thấy câu hỏi." });
        var examIds = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaCauHoi == id)
            .Select(x => x.MaDeThi).ToArrayAsync(ct);
        var unversionedExams = await db.DeThi.AsNoTracking()
            .Where(x => examIds.Contains(x.MaDeThi) && x.MaPhienBanHienHanh == null)
            .Select(x => x.MaDeThi).ToArrayAsync(ct);
        var compactAttempts = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaPhienBanDeThi == null && x.DuLieuBaiLamJson != null &&
                (x.LoaiBaiLam == ExamCore.Mock || x.LoaiBaiLam == ExamCore.Placement))
            .Select(x => new { x.MaKetQua, x.DuLieuBaiLamJson }).ToListAsync(ct);
        var dependentAttempts = compactAttempts.Where(x =>
            CompactExamStore.Decode(x.DuLieuBaiLamJson).QuestionIds.Contains(id))
            .Select(x => x.MaKetQua).ToArray();
        var legacyCount = await db.CauHoiLuotLam.AsNoTracking()
            .CountAsync(x => x.MaCauHoiGoc == id, ct);
        return Ok(new { questionId = id, examIds, unversionedExams,
            compactAttemptIds = dependentAttempts, legacySnapshotCount = legacyCount,
            canDelete = examIds.Length == 0 && unversionedExams.Length == 0 && dependentAttempts.Length == 0,
            bank });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var question = await db.CauHoi.FromSqlInterpolated(
            $"SELECT * FROM dbo.CauHoi WITH (UPDLOCK, HOLDLOCK) WHERE MaCauHoi = {id}")
            .SingleOrDefaultAsync(ct);
        if (question is null) return NotFound(new { code = "QUESTION_NOT_FOUND", message = "Không tìm thấy câu hỏi." });
        var groupId = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaCauHoi == id)
            .Select(x => (int?)x.MaNguLieu).SingleOrDefaultAsync(ct);
        if (groupId is not null)
            return Conflict(new { code = "GROUP_DELETE_REQUIRED",
                message = "Câu thuộc nhóm TOEIC; xóa riêng sẽ làm sai cấu trúc nhóm. Hãy xóa cả nhóm hoặc nhân bản nhóm để thay câu." });
        var links = await db.CauHoiDeThi.Where(x => x.MaCauHoi == id).ToListAsync(ct);
        var examIds = links.Select(x => x.MaDeThi).ToArray();
        if (examIds.Length > 0)
            return Conflict(new { code = "REPLACEMENT_REQUIRED",
                message = "Câu còn trong đề thi hiện hành. Hãy thay bằng câu khác và xuất bản phiên bản mới trước khi xóa nguồn.", examIds });
        var unavailable = await db.DeThi.AsNoTracking().Where(e => examIds.Contains(e.MaDeThi) &&
            e.MaPhienBanHienHanh == null).Select(e => e.MaDeThi).ToArrayAsync(ct);
        if (unavailable.Length > 0)
            return Conflict(new { code = "EXAM_SNAPSHOT_REQUIRED",
                message = "Cần xuất bản bản lưu đề độc lập trước khi xóa câu nguồn.", examIds = unavailable });
        var compact = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaPhienBanDeThi == null &&
            a.DuLieuBaiLamJson != null &&
            (a.LoaiBaiLam == ExamCore.Mock || a.LoaiBaiLam == ExamCore.Placement))
            .Select(a => new { a.MaKetQua, a.DuLieuBaiLamJson }).ToListAsync(ct);
        var blocked = compact.Where(a => CompactExamStore.Decode(a.DuLieuBaiLamJson)
            .QuestionIds.Contains(id)).Select(a => a.MaKetQua).ToArray();
        if (blocked.Length > 0)
            return Conflict(new { code = "LEGACY_ATTEMPT_DEPENDENCY",
                message = "Lượt thi cũ còn đọc câu hỏi từ ngân hàng nguồn; chưa thể xóa an toàn.",
                attemptIds = blocked });
        // Relational attempts already contain their own question content; only detach their
        // optional source FK. No learner row, answer, score or history row is deleted.
        await db.CauHoiLuotLam.Where(x => x.MaCauHoiGoc == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MaCauHoiGoc, (int?)null), ct);
        db.CauHoiDeThi.RemoveRange(links);
        var membership = await db.NhomCauHoi.FindAsync([id], ct);
        if (membership is not null) db.NhomCauHoi.Remove(membership);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.ChiTietPhanLoai WHERE MaCauHoi = {id}", ct);
        db.CauHoi.Remove(question);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { deleted = true, questionId = id, affectedExamIds = examIds });
    }
}
