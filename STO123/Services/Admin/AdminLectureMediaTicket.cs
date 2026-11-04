using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace STO123.Services.Admin;

// A short-lived bearer grant for HTML media elements, which cannot send JWT headers.
public sealed class AdminLectureMediaTicket(IConfiguration configuration, TimeProvider clock)
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ??
        throw new InvalidOperationException("Jwt:Key is required for admin media tickets."));

    public string Issue(int adminId, int lectureId, int resourceId)
    {
        var expiry = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"admin-video:1:{adminId}:{lectureId}:{resourceId}:{expiry}");
        return WebEncoders.Base64UrlEncode(payload) + "." +
            WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, payload));
    }

    public bool Valid(string? token, int lectureId, int resourceId)
    {
        if (token is null || token.Length is < 30 or > 512) return false;
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature,
                    HMACSHA256.HashData(key, payload))) return false;
            var fields = Encoding.UTF8.GetString(payload).Split(':');
            return fields.Length == 6 && fields[0] == "admin-video" && fields[1] == "1" &&
                int.TryParse(fields[2], out var adminId) && adminId > 0 &&
                int.TryParse(fields[3], out var issuedLecture) && issuedLecture == lectureId &&
                int.TryParse(fields[4], out var issuedResource) && issuedResource == resourceId &&
                long.TryParse(fields[5], out var expiry) &&
                DateTimeOffset.FromUnixTimeSeconds(expiry) > clock.GetUtcNow();
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException) { return false; }
    }
}
