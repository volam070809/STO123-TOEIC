using System.Collections.Concurrent;

namespace STO123.Services.Arena;

/// <summary>Connection presence for the single backend used by local and tunnel testing.</summary>
public sealed class ArenaPresence
{
    private readonly ConcurrentDictionary<string, (Guid MatchId, int LearnerId)> connections = new();

    public void Join(string connectionId, Guid matchId, int learnerId) =>
        connections[connectionId] = (matchId, learnerId);

    public Guid? Leave(string connectionId) =>
        connections.TryRemove(connectionId, out var entry) ? entry.MatchId : null;

    public bool IsOnline(Guid matchId, int learnerId) =>
        connections.Values.Any(entry => entry.MatchId == matchId && entry.LearnerId == learnerId);
}
