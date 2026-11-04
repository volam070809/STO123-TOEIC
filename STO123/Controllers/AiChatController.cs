using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Ai;

namespace STO123.Controllers;

public sealed record AiChatMessageRequest(Guid RequestId, string? Message, AiLearningContext? Context);

[ApiController]
[Authorize(Roles = "HOC_VIEN")]
[Route("api/ai/conversations")]
public sealed class AiChatController(ToeicDbContext db, AiToeicService ai,
    AiKnowledgeService knowledge) : ControllerBase
{
    private int LearnerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private static object View(AiTinNhan item) => new { id = item.MaTinNhan, role = item.VaiTro,
        text = item.NoiDung, createdAt = item.TaoLuc };

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.AiHoiThoai.AsNoTracking().Where(x => x.MaHocVien == LearnerId)
            .OrderByDescending(x => x.CapNhatLuc).ThenByDescending(x => x.MaHoiThoai)
            .Take(50).Select(x => new { id = x.MaHoiThoai, title = x.TieuDe,
                updatedAt = x.CapNhatLuc, messageCount = x.AiTinNhan.Count }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (LearnerId <= 0) return Unauthorized();
        if (await db.AiHoiThoai.CountAsync(x => x.MaHocVien == LearnerId, ct) >= 50)
            return Conflict(new { code = "AI_CONVERSATION_LIMIT", message = "Bạn đã có 50 cuộc trò chuyện. Hãy xóa cuộc cũ trước khi tạo mới." });
        var now = DateTime.UtcNow;
        var row = new AiHoiThoai { MaHocVien = LearnerId, TieuDe = "Cuộc trò chuyện mới",
            TaoLuc = now, CapNhatLuc = now };
        db.AiHoiThoai.Add(row);
        await db.SaveChangesAsync(ct);
        return Created($"/api/ai/conversations/{row.MaHoiThoai}", new { id = row.MaHoiThoai,
            title = row.TieuDe, updatedAt = now, messageCount = 0 });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        if (await HasActiveExamAsync(ct)) return ActiveExamConflict();
        var conversation = await db.AiHoiThoai.AsNoTracking()
            .Where(x => x.MaHoiThoai == id && x.MaHocVien == LearnerId)
            .Select(x => new { id = x.MaHoiThoai, title = x.TieuDe, updatedAt = x.CapNhatLuc })
            .FirstOrDefaultAsync(ct);
        if (conversation is null) return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
        var messages = await db.AiTinNhan.AsNoTracking().Where(x => x.MaHoiThoai == id)
            .OrderBy(x => x.ThuTu).Select(x => new { id = x.MaTinNhan, role = x.VaiTro,
                text = x.NoiDung, createdAt = x.TaoLuc }).ToListAsync(ct);
        return Ok(new { conversation.id, conversation.title, conversation.updatedAt, messages });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(id, ct);
        var row = await db.AiHoiThoai.FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaHocVien == LearnerId, ct);
        if (row is null) return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
        db.AiHoiThoai.Remove(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:long}/messages")]
    public async Task<IActionResult> Send(long id, [FromBody] AiChatMessageRequest input, CancellationToken ct)
    {
        var text = input.Message?.Trim();
        if (input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(text) || text.Length > 3000)
            return BadRequest(new { code = "AI_INVALID_MESSAGE", message = "Câu hỏi cần có nội dung, tối đa 3.000 ký tự." });
        try
        {
            var conversation = await db.AiHoiThoai.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaHocVien == LearnerId, ct);
            if (conversation is null) return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            if (await HasActiveExamAsync(ct)) return ActiveExamConflict();
            var previous = await db.AiTinNhan.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaYeuCau == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.NoiDung != text)
                    return Conflict(new { code = "AI_REQUEST_MISMATCH",
                        message = "Yêu cầu gửi lại đã thay đổi nội dung. Hãy gửi như một câu hỏi mới." });
                var priorAnswer = await db.AiTinNhan.AsNoTracking().FirstAsync(x =>
                    x.MaHoiThoai == id && x.ThuTu == previous.ThuTu + 1 && x.VaiTro == "MODEL", ct);
                return Ok(new { conversationId = id, title = conversation.TieuDe,
                    userMessage = View(previous), aiMessage = View(priorAnswer) });
            }
            if (input.Context?.Type != "PRACTICE" &&
                await db.KetQuaLamBai.AsNoTracking().AnyAsync(x => x.MaHocVien == LearnerId &&
                    x.LoaiBaiLam == "PRACTICE" && x.TrangThai == "DANG_LAM", ct))
                return Conflict(new { code = "AI_PRACTICE_CONTEXT_REQUIRED",
                    message = "Hãy mở câu luyện tập đang làm để nhận gợi ý theo đúng trạng thái kiểm tra." });
            var count = await db.AiTinNhan.CountAsync(x => x.MaHoiThoai == id, ct);
            if (count >= 100) return Conflict(new { code = "AI_MESSAGE_LIMIT",
                message = "Cuộc trò chuyện đã dài. Hãy tạo cuộc trò chuyện mới." });
            var history = await db.AiTinNhan.AsNoTracking().Where(x => x.MaHoiThoai == id)
                .OrderByDescending(x => x.ThuTu).Take(12)
                .Select(x => new AiPromptMessage(x.VaiTro, x.NoiDung)).ToListAsync(ct);
            history.Reverse();
            history.Add(new AiPromptMessage("USER", text));
            var grounding = await knowledge.ResolveAsync(LearnerId, text, input.Context, ct);
            var reply = grounding.UncheckedPractice
                ? AiAnswerComposer.Compose(PracticeHint(grounding.PracticePart), grounding.Sources)
                : AiAnswerComposer.Compose(await ai.ReplyAsync(history, grounding.Text, ct), grounding.Sources);
            // The provider call has completed before acquiring any SQL transaction or conversation lock.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(id, ct);
            conversation = await db.AiHoiThoai.FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaHocVien == LearnerId, ct);
            if (conversation is null) return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            if (await HasActiveExamAsync(ct)) return ActiveExamConflict();
            previous = await db.AiTinNhan.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaYeuCau == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.NoiDung != text) return Conflict(new { code = "AI_REQUEST_MISMATCH",
                    message = "Yêu cầu gửi lại đã thay đổi nội dung. Hãy gửi như một câu hỏi mới." });
                var priorAnswer = await db.AiTinNhan.AsNoTracking().FirstAsync(x =>
                    x.MaHoiThoai == id && x.ThuTu == previous.ThuTu + 1 && x.VaiTro == "MODEL", ct);
                await tx.CommitAsync(ct);
                return Ok(new { conversationId = id, title = conversation.TieuDe,
                    userMessage = View(previous), aiMessage = View(priorAnswer) });
            }
            count = await db.AiTinNhan.CountAsync(x => x.MaHoiThoai == id, ct);
            if (count >= 100) return Conflict(new { code = "AI_MESSAGE_LIMIT",
                message = "Cuộc trò chuyện đã dài. Hãy tạo cuộc trò chuyện mới." });
            var now = DateTime.UtcNow;
            var userMessage = new AiTinNhan { MaHoiThoai = id, ThuTu = count + 1, VaiTro = "USER",
                NoiDung = text, MaYeuCau = input.RequestId, TaoLuc = now };
            var aiMessage = new AiTinNhan { MaHoiThoai = id, ThuTu = count + 2, VaiTro = "MODEL",
                NoiDung = reply, TaoLuc = DateTime.UtcNow };
            db.AiTinNhan.AddRange(userMessage, aiMessage);
            if (count == 0) conversation.TieuDe = text.Length > 90 ? text[..90] + "…" : text;
            conversation.CapNhatLuc = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { conversationId = id, title = conversation.TieuDe,
                userMessage = View(userMessage), aiMessage = View(aiMessage) });
        }
        catch (AiChatException error)
        { return StatusCode(error.Status, new { code = error.Code, message = error.Message }); }
        catch (SqlException error) when (error.Number == 51001)
        { return Conflict(new { code = "AI_BUSY", message = "Cuộc trò chuyện đang xử lý câu hỏi khác. Hãy thử lại." }); }
    }

    private Task<bool> HasActiveExamAsync(CancellationToken ct) => db.KetQuaLamBai.AsNoTracking()
        .AnyAsync(x => x.MaHocVien == LearnerId &&
            (x.LoaiBaiLam == "MOCK" || x.LoaiBaiLam == "PLACEMENT") &&
            (x.TrangThai == "DANG_LAM" || x.TrangThai == "BO_DO"), ct);

    private ConflictObjectResult ActiveExamConflict() => Conflict(new { code = "AI_EXAM_ACTIVE",
        message = "Hãy hoàn thành bài thi đang làm trước khi dùng trợ lý AI." });

    private async Task LockAsync(long id, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={"ai-chat:" + id}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51001, 'Conversation busy.', 1;
            """, ct);
    }

    private static string PracticeHint(int? part) => part switch
    {
        1 or 2 => "Hãy nghe tín hiệu chính và loại các phương án không khớp. Bạn cần kiểm tra câu trước khi tôi có thể giải thích đáp án.",
        3 or 4 => "Hãy nghe mục đích, người nói và chi tiết được hỏi; ghi nhớ từ khóa thay vì chọn ngay. Bạn cần kiểm tra câu trước khi xem giải thích.",
        5 or 6 => "Hãy xác định loại từ, thì, chủ ngữ và ngữ cảnh quanh chỗ trống. Bạn cần kiểm tra câu trước khi tôi giải thích đáp án.",
        7 => "Hãy đọc câu hỏi trước, tìm thông tin tương ứng trong ngữ liệu và đối chiếu từng lựa chọn. Bạn cần kiểm tra câu trước khi xem đáp án.",
        _ => "Hãy phân tích ngữ cảnh và loại trừ đáp án chưa phù hợp. Kiểm tra câu để xem giải thích cụ thể."
    };
}
