using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using STO123.Services.Exam;

namespace STO123.Services.Arena;

[Authorize(Roles = "HOC_VIEN")]
public sealed class ArenaHub(ArenaService arena, ArenaPresence presence) : Hub
{
    private int LearnerId => int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id :
        throw new HubException("Phiên đăng nhập không hợp lệ.");

    public async Task<object> Sync(Guid matchId)
    {
        // Check access before registering presence. Return a fresh state after joining so
        // the Sync response cannot overwrite the online event with an offline snapshot.
        await Run(() => arena.StateAsync(matchId, LearnerId, Context.ConnectionAborted));
        presence.Join(Context.ConnectionId, matchId, LearnerId);
        await arena.NotifyPresenceAsync(matchId, Context.ConnectionAborted);
        return await Run(() => arena.StateAsync(matchId, LearnerId, Context.ConnectionAborted));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var matchId = presence.Leave(Context.ConnectionId);
        if (matchId.HasValue)
            await arena.NotifyPresenceAsync(matchId.Value, CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<object> Ready(Guid matchId)
        => await Run(() => arena.ReadyAsync(matchId, LearnerId, Context.ConnectionAborted));

    public async Task<object> SubmitAnswer(Guid matchId, int questionIndex, string choice)
        => await Run(() => arena.AnswerAsync(matchId, LearnerId, questionIndex, choice,
            Context.ConnectionAborted));

    private static async Task<object> Run(Func<Task<object>> action)
    {
        try { return await action(); }
        catch (ArenaProblem problem) { throw new HubException(problem.Message); }
        catch (ExamProblem problem) { throw new HubException(problem.Message); }
    }
}
