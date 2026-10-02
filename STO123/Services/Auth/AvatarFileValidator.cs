namespace STO123.Services.Auth;

public static class AvatarFileValidator
{
    public static bool TryValidate(string? filename, string? declaredType, ReadOnlySpan<byte> bytes,
        out string extension, out string contentType)
    {
        extension = "";
        contentType = "";
        var suffix = Path.GetExtension(filename)?.ToLowerInvariant();
        if (bytes.Length >= 5 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff &&
            bytes[^2] == 0xff && bytes[^1] == 0xd9 &&
            suffix is ".jpg" or ".jpeg" && declaredType?.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) == true)
        { extension = "jpg"; contentType = "image/jpeg"; return true; }
        if (bytes.Length >= 20 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[^8..^4].SequenceEqual("IEND"u8) &&
            suffix == ".png" && declaredType?.Equals("image/png", StringComparison.OrdinalIgnoreCase) == true)
        { extension = "png"; contentType = "image/png"; return true; }
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes[8..12].SequenceEqual("WEBP"u8) &&
            BitConverter.ToUInt32(bytes[4..8]) == bytes.Length - 8 &&
            suffix == ".webp" && declaredType?.Equals("image/webp", StringComparison.OrdinalIgnoreCase) == true)
        { extension = "webp"; contentType = "image/webp"; return true; }
        return false;
    }
}
