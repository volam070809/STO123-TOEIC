using STO123.Services.Arena;

namespace STO123.Tests;

public sealed class ArenaPresenceTests
{
    [Fact]
    public void APlayerStaysOnlineWhileAtLeastOneConnectionRemains()
    {
        var presence = new ArenaPresence();
        var match = Guid.NewGuid();
        presence.Join("first", match, 10);
        presence.Join("second", match, 10);
        Assert.True(presence.IsOnline(match, 10));
        Assert.False(presence.IsOnline(match, 11));
        Assert.Equal(match, presence.Leave("first"));
        Assert.True(presence.IsOnline(match, 10));
        Assert.Equal(match, presence.Leave("second"));
        Assert.False(presence.IsOnline(match, 10));
    }
}
