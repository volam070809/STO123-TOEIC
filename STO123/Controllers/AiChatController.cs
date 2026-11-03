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
    AiLearningContextService learning) : ControllerBase
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
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(id, ct);
            var conversation = await db.AiHoiThoai.FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaHocVien == LearnerId, ct);
            if (conversation is null) return NotFound(new { message = "Không tìm thấy cuộc trò chuyện." });
            var previous = await db.AiTinNhan.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaHoiThoai == id && x.MaYeuCau == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.NoiDung != text)
                    return Conflict(new { code = "AI_REQUEST_MISMATCH",
                        message = "Yêu cầu gửi lại đã thay đổi nội dung. Hãy gửi như một câu hỏi mới." });
                var priorAnswer = await db.AiTinNhan.AsNoTracking().FirstAsync(x =>
                    x.MaHoiThoai == id && x.ThuTu == previous.ThuTu + 1 && x.VaiTro == "MODEL", ct);
                await tx.CommitAsync(ct);
                return Ok(new { conversationId = id, title = conversation.TieuDe,
                    userMessage = View(previous), aiMessage = View(priorAnswer) });
            }
            var count = await db.AiTinNhan.CountAsync(x => x.MaHoiThoai == id, ct);
            if (count >= 100) return Conflict(new { code = "AI_MESSAGE_LIMIT",
                message = "Cuộc trò chuyện đã dài. Hãy tạo cuộc trò chuyện mới." });
            var history = await db.AiTinNhan.AsNoTracking().Where(x => x.MaHoiThoai == id)
                .OrderByDescending(x => x.ThuTu).Take(12)
                .Select(x => new AiPromptMessage(x.VaiTro, x.NoiDung)).ToListAsync(ct);
            history.Reverse();
            history.Add(new AiPromptMessage("USER", text));
            var context = await learning.ResolveAsync(LearnerId, input.Context, ct);
            var reply = await ai.ReplyAsync(history, context, ct);
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

    private async Task LockAsync(long id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={"ai-chat:" + id}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51001, 'Conversation busy.', 1;
            """, ct);
    }
}
