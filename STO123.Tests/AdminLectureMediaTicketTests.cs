using Microsoft.Extensions.Configuration;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminLectureMediaTicketTests
{
    [Fact]
    public void TicketIsScopedToResourceAndExpires()
    {
        var clock = new MutableClock();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Key"] = "isolated-test-key-with-enough-length-123456" }).Build();
        var issuer = new AdminLectureMediaTicket(config, clock);
        var ticket = issuer.Issue(7, 11, 13);
        Assert.True(issuer.Valid(ticket, 11, 13));
        Assert.False(issuer.Valid(ticket, 11, 14));
        Assert.False(issuer.Valid(ticket, 12, 13));
        Assert.False(issuer.Valid(ticket + "x", 11, 13));
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(issuer.Valid(ticket, 11, 13));
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now = now.Add(time);
    }
}
