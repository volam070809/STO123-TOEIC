using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace STO123.Services.Admin;

public sealed record LectureMediaGrant(int LearnerId, int CourseId, int StepId, int ResourceId, DateTimeOffset ExpiresAt);

public sealed class LectureMediaTicket(IConfiguration configuration, TimeProvider clock)
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ??
        throw new InvalidOperationException("Jwt:Key is required for lecture media tickets."));
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    public (string Token, DateTimeOffset ExpiresAt) Issue(int learnerId, int courseId, int stepId, int resourceId,
        DateTimeOffset? sessionExpiresAt = null)
    {
        var expires = clock.GetUtcNow().Add(Lifetime);
        if (sessionExpiresAt is { } sessionExpiry && sessionExpiry < expires) expires = sessionExpiry;
        if (expires <= clock.GetUtcNow()) throw new InvalidOperationException("The session has expired.");
        var payload = $"1:{learnerId}:{courseId}:{stepId}:{resourceId}:{expires.ToUnixTimeSeconds()}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(key, bytes);
        return (WebEncoders.Base64UrlEncode(bytes) + "." + WebEncoders.Base64UrlEncode(signature), expires);
    }

    public bool TryRead(string? token, out LectureMediaGrant? grant)
    {
        grant = null;
        if (token is null || token.Length is < 20 or > 512) return false;
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, payload)))
                return false;
            var fields = Encoding.UTF8.GetString(payload).Split(':');
            if (fields.Length != 6 || fields[0] != "1" ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var learnerId) ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var courseId) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var stepId) ||
                !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var resourceId) ||
                !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry) ||
                learnerId <= 0 || courseId <= 0 || stepId <= 0 || resourceId <= 0) return false;
            var expires = DateTimeOffset.FromUnixTimeSeconds(expiry);
            if (expires <= clock.GetUtcNow()) return false;
            grant = new(learnerId, courseId, stepId, resourceId, expires);
            return true;
        }
        catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException) { return false; }
    }
}
