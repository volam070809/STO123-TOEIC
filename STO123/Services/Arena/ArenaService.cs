using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.SmartLearning;
using STO123.Services.Auth;

namespace STO123.Services.Arena;

public sealed record ArenaSubmission(int QuestionIndex, int PlayerId, string? Choice,
    bool? Correct, DateTime SubmittedUtc);

public sealed class ArenaService(ToeicDbContext db, PracticePartService practice,
    SmartLearningService smartLearning, IHubContext<ArenaHub> hub, ArenaPresence presence,
    ILogger<ArenaService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MatchLocks = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> QueueLocks = new();
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static SemaphoreSlim MatchLock(Guid id) => MatchLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
    private static IReadOnlyList<ArenaQuestionSnapshot> Questions(ArenaMatch row) =>
        JsonSerializer.Deserialize<List<ArenaQuestionSnapshot>>(row.QuestionsJson ?? "[]", Json) ?? [];
    private static List<ArenaSubmission> Answers(ArenaMatch row) =>
        JsonSerializer.Deserialize<List<ArenaSubmission>>(row.AnswersJson, Json) ?? [];

    public async Task<object> QuickAsync(int learnerId, int part, CancellationToken ct)
    {
        ValidatePart(part);
        var gate = QueueLocks.GetOrAdd(part, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await LockResourceAsync($"arena-quick:{part}", ct);
            var own = await db.ArenaMatch.AsNoTracking().Where(x => x.OwnerId == learnerId &&
                x.Part == part && x.RoomCode == null && x.Status == "WAITING" && x.OpponentId == null)
                .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
            if (own is not null) return await StateForAsync(own, learnerId, ct);
            var existing = await ActiveMatchRowAsync(learnerId, ct);
            if (existing is not null) return await StateForAsync(existing, learnerId, ct);
            var candidates = await db.ArenaMatch.Where(x => x.Part == part && x.RoomCode == null &&
                x.Status == "WAITING" && x.OpponentId == null && x.OwnerId != learnerId &&
                x.CreatedUtc > DateTime.UtcNow.AddMinutes(-15))
                .OrderBy(x => x.CreatedUtc).Take(8).ToListAsync(ct);
            ArenaMatch? chosen = null;
            if (candidates.Count > 0)
            {
                chosen = candidates[0];
                var myPart = (await smartLearning.ProfileAsync(learnerId, ct)).Parts[part - 1];
                if (myPart.Confidence != "TAM_THOI")
                {
                    var best = double.MaxValue;
                    foreach (var candidate in candidates)
                    {
                        var other = (await smartLearning.ProfileAsync(candidate.OwnerId, ct)).Parts[part - 1];
                        if (other.Confidence == "TAM_THOI") continue;
                        var distance = Math.Abs(other.EstimatedAccuracy - myPart.EstimatedAccuracy);
                        if (distance < best) { chosen = candidate; best = distance; }
                    }
                }
            }
            if (chosen is null)
            {
                chosen = new ArenaMatch { Id = Guid.NewGuid(), Part = part, OwnerId = learnerId,
                    CreatedUtc = DateTime.UtcNow, Status = "WAITING" };
                db.ArenaMatch.Add(chosen);
            }
            else { chosen.OpponentId = learnerId; chosen.Status = "READY"; }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await NotifyAsync(chosen, ct);
            return await StateForAsync(chosen, learnerId, ct);
        }
        catch (DbUpdateConcurrencyException)
        { throw new ArenaProblem("MATCH_CHANGED", "Phòng vừa thay đổi. Vui lòng thử ghép trận lại.", 409); }
        finally { gate.Release(); }
    }

    public async Task<object> CreatePrivateAsync(int learnerId, int part, CancellationToken ct)
    {
        ValidatePart(part);
        var existing = await ActiveMatchRowAsync(learnerId, ct);
        if (existing is not null) return await StateForAsync(existing, learnerId, ct);
        var code = new string(Enumerable.Range(0, 8)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        var row = new ArenaMatch { Id = Guid.NewGuid(), Part = part, OwnerId = learnerId,
            RoomCode = code, CreatedUtc = DateTime.UtcNow, Status = "WAITING" };
        db.ArenaMatch.Add(row);
        await db.SaveChangesAsync(ct);
        return await StateForAsync(row, learnerId, ct);
    }

    public async Task<object> InviteAsync(Guid id, int learnerId, CancellationToken ct) =>
        await WithMatchAsync(id, learnerId, async row => {
            if (row.OwnerId != learnerId || row.Status != "WAITING" || row.OpponentId.HasValue)
                throw new ArenaProblem("ROOM_NOT_WAITING", "Chỉ chủ phòng đang chờ mới có thể tạo lời mời.", 409);
            row.RoomCode ??= new string(Enumerable.Range(0, 8)
                .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct);

    public async Task<object> JoinPrivateAsync(int learnerId, string? code, CancellationToken ct)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (normalized is null || normalized.Length != 8 || normalized.Any(c => !Alphabet.Contains(c)))
            throw new ArenaProblem("INVALID_ROOM_CODE", "Mã phòng không hợp lệ.");
        var room = await db.ArenaMatch.AsNoTracking()
            .Where(x => x.RoomCode == normalized).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (room == Guid.Empty) throw new ArenaProblem("ROOM_NOT_FOUND", "Không tìm thấy phòng này.", 404);
        return await WithMatchAsync(room, learnerId, async row => {
            if (row.OwnerId == learnerId || row.OpponentId == learnerId)
                return await StateForAsync(row, learnerId, ct);
            if (await ActiveMatchRowAsync(learnerId, ct) is not null)
                throw new ArenaProblem("ALREADY_IN_MATCH", "Bạn đang có trận khác. Hãy trở lại hoặc rời trận đó trước.", 409);
            if (row.Status != "WAITING" || row.OpponentId.HasValue)
                throw new ArenaProblem("ROOM_FULL", "Phòng đã đủ người hoặc đã bắt đầu.", 409);
            row.OpponentId = learnerId; row.Status = "READY";
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct, allowJoin: true);
    }

    public async Task<object> ReadyAsync(Guid id, int learnerId, CancellationToken ct) =>
        await WithMatchAsync(id, learnerId, async row => {
            if (row.Status is not ("WAITING" or "READY")) return await StateForAsync(row, learnerId, ct);
            if (row.OwnerId == learnerId) row.OwnerReady = true;
            else row.OpponentReady = true;
            if (row.OpponentId.HasValue && row.OwnerReady && row.OpponentReady)
            {
                var questions = await practice.SelectArenaQuestionsAsync(row.Part, ct);
                row.QuestionsJson = JsonSerializer.Serialize(questions, Json);
                row.AnswersJson = "[]";
                row.QuestionIndex = 0;
                row.StartedUtc = DateTime.UtcNow.AddSeconds(3);
                row.Status = "STARTING";
                row.DeadlineUtc = null;
            }
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct);

    public async Task<object> AnswerAsync(Guid id, int learnerId, int questionIndex,
        string? choice, CancellationToken ct) => await WithMatchAsync(id, learnerId, async row => {
            var now = DateTime.UtcNow;
            var questions = Questions(row);
            ArenaRules.Submit(row, questions, learnerId, questionIndex, choice, now);
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct);

    public async Task<object> StateAsync(Guid id, int learnerId, CancellationToken ct)
    {
        var row = await db.ArenaMatch.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ArenaProblem("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.", 404);
        RequireParticipant(row, learnerId);
        return await StateForAsync(row, learnerId, ct);
    }

    public async Task NotifyPresenceAsync(Guid id, CancellationToken ct)
    {
        var row = await db.ArenaMatch.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is not null) await NotifyAsync(row, ct);
    }

    public async Task<string?> AvatarReferenceAsync(Guid id, int learnerId,
        int playerId, CancellationToken ct)
    {
        var row = await db.ArenaMatch.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ArenaProblem("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.", 404);
        RequireParticipant(row, learnerId);
        RequireParticipant(row, playerId);
        return await db.NguoiDung.AsNoTracking().Where(x => x.MaNguoiDung == playerId)
            .Select(x => x.AnhDaiDien).SingleOrDefaultAsync(ct);
    }

    public async Task<object> LeaveAsync(Guid id, int learnerId, CancellationToken ct) =>
        await WithMatchAsync(id, learnerId, async row => {
            if (row.Status is "WAITING" or "READY")
            {
                if (row.OwnerId == learnerId) row.Status = "ABANDONED";
                else { row.OpponentId = null; row.OpponentReady = false;
                    row.OwnerReady = false; row.Status = "WAITING"; }
            }
            else if (row.Status is "STARTING" or "ACTIVE" or "REVEAL")
                throw new ArenaProblem("SURRENDER_REQUIRED", "Trận đã bắt đầu. Hãy dùng Đầu hàng để kết thúc.", 409);
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct);

    public async Task<object> SurrenderAsync(Guid id, int learnerId, CancellationToken ct) =>
        await WithMatchAsync(id, learnerId, async row => {
            if (row.Status is not ("STARTING" or "ACTIVE" or "REVEAL"))
                throw new ArenaProblem("SURRENDER_UNAVAILABLE", "Trận này không còn ở trạng thái có thể đầu hàng.", 409);
            row.Status = "SURRENDERED";
            row.ForfeitById = learnerId;
            row.CompletedUtc = DateTime.UtcNow;
            row.DeadlineUtc = null;
            row.RevealUntilUtc = null;
            await db.SaveChangesAsync(ct);
            await NotifyAsync(row, ct);
            return await StateForAsync(row, learnerId, ct);
        }, ct);

    public async Task<object?> ActiveAsync(int learnerId, CancellationToken ct)
    {
        var row = await ActiveMatchRowAsync(learnerId, ct);
        return row is null ? null : await StateForAsync(row, learnerId, ct);
    }

    private Task<ArenaMatch?> ActiveMatchRowAsync(int learnerId, CancellationToken ct) =>
        db.ArenaMatch.AsNoTracking()
            .Where(x => (x.OwnerId == learnerId || x.OpponentId == learnerId) &&
                (x.Status == "WAITING" || x.Status == "READY" ||
                 x.Status == "STARTING" || x.Status == "ACTIVE" || x.Status == "REVEAL"))
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);

    public async Task<(string Container, string Path)> MediaPathAsync(Guid id, int learnerId,
        int index, string kind, int? documentOrder, CancellationToken ct)
    {
        var row = await db.ArenaMatch.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ArenaProblem("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.", 404);
        RequireParticipant(row, learnerId);
        var questions = Questions(row);
        if (row.Status != "COMPLETED" && index != row.QuestionIndex)
            throw new ArenaProblem("MEDIA_NOT_AVAILABLE", "Media của câu này chưa được mở.", 403);
        if (index < 0 || index >= questions.Count)
            throw new ArenaProblem("MEDIA_NOT_FOUND", "Không tìm thấy tệp media.", 404);
        var question = questions[index];
        var path = kind switch {
            "audio" => question.AudioPath,
            "image" => question.ImagePath,
            "document" => question.Documents.FirstOrDefault(d => d.Order == documentOrder)?.ImagePath,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(path))
            throw new ArenaProblem("MEDIA_NOT_FOUND", "Không tìm thấy tệp media.", 404);
        return ("practice-question", path);
    }

    public async Task<(string Container, string Path)> GroupAudioPathAsync(Guid id,
        int learnerId, int groupId, CancellationToken ct)
    {
        var row = await db.ArenaMatch.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ArenaProblem("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.", 404);
        RequireParticipant(row, learnerId);
        var questions = Questions(row);
        var current = row.QuestionIndex < questions.Count ? questions[row.QuestionIndex] : null;
        if (row.Status != "COMPLETED" && current?.GroupId != groupId)
            throw new ArenaProblem("MEDIA_NOT_AVAILABLE", "Âm thanh của nhóm này chưa được mở.", 403);
        var path = questions.FirstOrDefault(q => q.GroupId == groupId)?.AudioPath;
        if (path is null) throw new ArenaProblem("MEDIA_NOT_FOUND", "Không tìm thấy âm thanh nhóm.", 404);
        return ("practice-question", path);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = await db.ArenaMatch.AsNoTracking().Where(x =>
            (x.Status == "STARTING" && x.StartedUtc <= now) ||
            (x.Status == "ACTIVE" && x.DeadlineUtc <= now) ||
            (x.Status == "REVEAL" && x.RevealUntilUtc <= now) ||
            ((x.Status == "WAITING" || x.Status == "READY") && x.CreatedUtc <= now.AddMinutes(-30)))
            .Select(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await WithMatchAsync(id, 0, async row => {
                var tickAt = DateTime.UtcNow;
                if (row.Status == "STARTING" && row.StartedUtc <= tickAt)
                { row.Status = "ACTIVE"; row.DeadlineUtc = tickAt.AddSeconds(QuestionSeconds(row.Part)); }
                else if (row.Status == "ACTIVE" && row.DeadlineUtc <= tickAt)
                {
                    var questions = Questions(row);
                    if (row.QuestionIndex < questions.Count)
                        ArenaRules.Reveal(row, questions[row.QuestionIndex], Answers(row), tickAt);
                }
                else if (row.Status == "REVEAL" && row.RevealUntilUtc <= tickAt)
                {
                    if (!ArenaRules.ResolveAbandonment(row, Answers(row), tickAt))
                        ArenaRules.Advance(row, Questions(row).Count, tickAt, QuestionSeconds(row.Part));
                }
                else if (row.Status is "WAITING" or "READY" && row.CreatedUtc <= tickAt.AddMinutes(-30))
                    row.Status = "ABANDONED";
                await db.SaveChangesAsync(ct);
                await NotifyAsync(row, ct);
                return row.Id;
            }, ct, system: true); }
            catch (ArenaProblem) { }
        }
    }

    private async Task<T> WithMatchAsync<T>(Guid id, int learnerId,
        Func<ArenaMatch, Task<T>> action, CancellationToken ct, bool allowJoin = false, bool system = false)
    {
        var gate = MatchLock(id);
        await gate.WaitAsync(ct);
        try
        {
            var row = await db.ArenaMatch.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ArenaProblem("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.", 404);
            if (!system && !allowJoin) RequireParticipant(row, learnerId);
            return await action(row);
        }
        catch (DbUpdateConcurrencyException)
        { throw new ArenaProblem("MATCH_CHANGED", "Trận đấu vừa thay đổi. Vui lòng đồng bộ lại.", 409); }
        finally { gate.Release(); }
    }

    private async Task NotifyAsync(ArenaMatch row, CancellationToken ct)
    {
        // SQL is already committed. A transient transport failure must not turn a saved answer
        // into an apparent failure; clients also synchronize from SQL after reconnect/polling.
        try
        {
            await hub.Clients.User(row.OwnerId.ToString()).SendAsync("ArenaState",
                await StateForAsync(row, row.OwnerId, CancellationToken.None), CancellationToken.None);
            if (row.OpponentId is { } opponent)
                await hub.Clients.User(opponent.ToString()).SendAsync("ArenaState",
                    await StateForAsync(row, opponent, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception error) { logger.LogWarning(error, "Arena match {MatchId} notification will be recovered by sync", row.Id); }
    }

    private async Task LockResourceAsync(string resource, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Arena matchmaking is busy.', 1;
                """, ct);
        }
        catch (SqlException error) when (error.Number == 51001)
        { throw new ArenaProblem("MATCH_BUSY", "Ghép trận đang bận. Vui lòng thử lại.", 409); }
    }

    private async Task<object> StateForAsync(ArenaMatch row, int learnerId, CancellationToken ct)
    {
        RequireParticipant(row, learnerId);
        var questions = Questions(row);
        var answers = Answers(row);
        var index = row.QuestionIndex;
        var question = index < questions.Count ? questions[index] : null;
        var myAnswer = answers.FirstOrDefault(x => x.QuestionIndex == index && x.PlayerId == learnerId);
        var otherId = row.OwnerId == learnerId ? row.OpponentId : row.OwnerId;
        var otherAnswer = answers.FirstOrDefault(x => x.QuestionIndex == index && x.PlayerId == otherId);
        Dictionary<int, (string Name, string? Avatar)> names;
        try
        {
            names = await db.NguoiDung.AsNoTracking().Where(x => x.MaNguoiDung == row.OwnerId ||
                x.MaNguoiDung == row.OpponentId)
                .Select(x => new { x.MaNguoiDung, x.HoTen, x.AnhDaiDien })
                .ToDictionaryAsync(x => x.MaNguoiDung,
                    x => (x.HoTen, (string?)x.AnhDaiDien), ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Arena participant names unavailable for match {MatchId}", row.Id);
            names = new Dictionary<int, (string Name, string? Avatar)>();
        }
        (string? Url, string? Endpoint) Avatar(int id)
        {
            var avatar = names.GetValueOrDefault(id).Avatar;
            return AvatarStorage.IsCustomReference(id, avatar) ?
                (null, $"/api/arena/matches/{row.Id}/players/{id}/avatar?v={AvatarStorage.Version(avatar)}") :
                (AvatarStorage.ValidProviderUrl(avatar), null);
        }
        var reveal = row.Status is "REVEAL" or "COMPLETED";
        return new {
            row.Id, row.Part, row.RoomCode, row.Status, row.QuestionIndex,
            revision = Convert.ToHexString(row.RowVersion),
            serverNow = DateTime.UtcNow,
            totalQuestions = questions.Count, deadlineUtc = Utc(row.DeadlineUtc),
            revealUntilUtc = Utc(row.RevealUntilUtc), startsAtUtc = Utc(row.StartedUtc),
            winnerId = row.Status is "SURRENDERED" or "ABANDONED" && row.ForfeitById.HasValue ?
                row.ForfeitById == row.OwnerId ? row.OpponentId : row.OwnerId : null,
            endReason = row.Status == "SURRENDERED" ? "SURRENDER" :
                row.Status == "ABANDONED" ? "ABANDONMENT" : null,
            you = new { id = learnerId, name = names.GetValueOrDefault(learnerId).Name ?? "Học viên",
                avatarUrl = Avatar(learnerId).Url, avatarEndpoint = Avatar(learnerId).Endpoint,
                online = presence.IsOnline(row.Id, learnerId),
                ready = row.OwnerId == learnerId ? row.OwnerReady : row.OpponentReady,
                score = answers.Count(x => x.PlayerId == learnerId && x.Correct == true) },
            opponent = otherId is null ? null : new { id = otherId.Value,
                name = names.GetValueOrDefault(otherId.Value).Name ?? "Đối thủ",
                avatarUrl = Avatar(otherId.Value).Url, avatarEndpoint = Avatar(otherId.Value).Endpoint,
                online = presence.IsOnline(row.Id, otherId.Value),
                ready = row.OwnerId == otherId ? row.OwnerReady : row.OpponentReady,
                score = answers.Count(x => x.PlayerId == otherId && x.Correct == true) },
            question = question is null || row.Status is "WAITING" or "READY" or "STARTING" ? null :
                ArenaQuestionPresenter.Present(question, row.Id, index, reveal),
            submitted = myAnswer is not null,
            opponentSubmitted = otherAnswer is not null,
            selected = myAnswer?.Choice,
            result = reveal && question is not null ? new {
                youCorrect = myAnswer?.Correct == true, opponentCorrect = otherAnswer?.Correct == true,
                youSelected = myAnswer?.Choice, opponentSelected = otherAnswer?.Choice,
                leading = answers.Count(x => x.PlayerId == learnerId && x.Correct == true) ==
                    answers.Count(x => x.PlayerId == otherId && x.Correct == true) ? "TIE" :
                    answers.Count(x => x.PlayerId == learnerId && x.Correct == true) >
                    answers.Count(x => x.PlayerId == otherId && x.Correct == true) ? "YOU" : "OPPONENT"
            } : null,
            details = row.Status == "COMPLETED" ? questions.Select((q, i) => new {
                number = i + 1, q.Content, q.Correct, q.Explanation,
                youSelected = answers.FirstOrDefault(a => a.QuestionIndex == i && a.PlayerId == learnerId)?.Choice,
                opponentSelected = answers.FirstOrDefault(a => a.QuestionIndex == i && a.PlayerId == otherId)?.Choice,
                youCorrect = answers.Any(a => a.QuestionIndex == i && a.PlayerId == learnerId && a.Correct == true),
                opponentCorrect = answers.Any(a => a.QuestionIndex == i && a.PlayerId == otherId && a.Correct == true)
            }).ToArray() : null
        };
    }

    private static int QuestionSeconds(int part) => part switch {
        1 or 2 => 45, 3 or 4 => 55, 5 => 35, 6 => 55, _ => 75
    };
    private static DateTime? Utc(DateTime? value) => value is null ? null :
        DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    private static void ValidatePart(int part)
    {
        if (part is < 1 or > 7) throw new ArenaProblem("INVALID_PART", "Hãy chọn Part từ 1 đến 7.");
    }
    private static void RequireParticipant(ArenaMatch row, int learnerId)
    {
        if (row.OwnerId != learnerId && row.OpponentId != learnerId)
            throw new ArenaProblem("MATCH_FORBIDDEN", "Bạn không có quyền xem trận đấu này.", 403);
    }
}
