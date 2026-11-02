using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Controllers;

[Authorize(Roles = "ADMIN_NOI_DUNG,ADMIN_QUAN_LY")]
[ApiController]
[Route("api/admin/content/groups")]
public sealed class AdminQuestionGroupDeleteController(ToeicDbContext db) : ControllerBase
{
    [HttpDelete("{groupId:int}")]
    public async Task<IActionResult> Delete(int groupId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var resource = await db.NguLieu.FromSqlInterpolated(
            $"SELECT * FROM dbo.NguLieu WITH (UPDLOCK, HOLDLOCK) WHERE MaNguLieu = {groupId}")
            .SingleOrDefaultAsync(ct);
        if (resource is null) return NotFound(new { code = "GROUP_NOT_FOUND",
            message = "Không tìm thấy nhóm câu hỏi." });
        var members = await db.NhomCauHoi.Where(m => m.MaNguLieu == groupId)
            .OrderBy(m => m.ThuTu).ToListAsync(ct);
        if (members.Count == 0) return Conflict(new { code = "GROUP_EMPTY",
            message = "Ngữ liệu không còn câu thành viên; cần kiểm tra trước khi xóa." });
        var ids = members.Select(m => m.MaCauHoi).ToArray();
        var questions = await db.CauHoi.Where(q => ids.Contains(q.MaCauHoi)).ToListAsync(ct);
        if (questions.Count != ids.Length) return Conflict(new { code = "GROUP_CHANGED",
            message = "Nhóm câu đã thay đổi, vui lòng tải lại." });
        if (questions.Select(q => q.LoaiCauHoi).Distinct().Count() != 1)
            return Conflict(new { code = "GROUP_BANK_MISMATCH",
                message = "Nhóm trộn ngân hàng đề thi và luyện tập; cần kiểm tra trước khi xóa." });
        var links = await db.CauHoiDeThi.Where(l => ids.Contains(l.MaCauHoi)).ToListAsync(ct);
        var examIds = links.Select(l => l.MaDeThi).Distinct().ToArray();
        if (examIds.Length > 0) return Conflict(new { code = "REPLACEMENT_REQUIRED",
            message = "Nhóm còn trong đề thi hiện hành. Hãy thay nguyên nhóm và xuất bản phiên bản mới trước khi xóa nguồn.", examIds });
        var unversioned = await db.DeThi.AsNoTracking().Where(e => examIds.Contains(e.MaDeThi) &&
            e.MaPhienBanHienHanh == null).Select(e => e.MaDeThi).ToArrayAsync(ct);
        if (unversioned.Length > 0) return Conflict(new { code = "EXAM_SNAPSHOT_REQUIRED",
            message = "Cần xuất bản bản lưu độc lập của đề trước khi xóa nhóm nguồn.", examIds = unversioned });
        var compact = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaPhienBanDeThi == null &&
            a.DuLieuBaiLamJson != null &&
            (a.LoaiBaiLam == ExamCore.Mock || a.LoaiBaiLam == ExamCore.Placement))
            .Select(a => new { a.MaKetQua, a.DuLieuBaiLamJson }).ToListAsync(ct);
        var idSet = ids.ToHashSet();
        var blocked = compact.Where(a => CompactExamStore.Decode(a.DuLieuBaiLamJson)
            .QuestionIds.Any(idSet.Contains)).Select(a => a.MaKetQua).ToArray();
        if (blocked.Length > 0) return Conflict(new { code = "LEGACY_ATTEMPT_DEPENDENCY",
            message = "Lượt thi cũ còn đọc nhóm nguồn; chưa thể xóa an toàn.", attemptIds = blocked });

        await db.CauHoiLuotLam.Where(q => q.MaCauHoiGoc != null && ids.Contains(q.MaCauHoiGoc.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.MaCauHoiGoc, (int?)null), ct);
        await db.NhomLuotLam.Where(g => g.MaNguLieuGoc == groupId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.MaNguLieuGoc, (int?)null), ct);
        foreach (var id in ids)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.ChiTietPhanLoai WHERE MaCauHoi = {id}", ct);
        db.CauHoiDeThi.RemoveRange(links);
        db.NhomCauHoi.RemoveRange(members);
        var docs = await db.NguLieuTaiLieu.Where(d => d.MaNguLieu == groupId).ToListAsync(ct);
        db.NguLieuTaiLieu.RemoveRange(docs);
        db.CauHoi.RemoveRange(questions);
        db.NguLieu.Remove(resource);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { deleted = true, groupId, questionIds = ids,
            affectedExamIds = examIds, mediaFilesRemainInLibrary = true });
    }
}
