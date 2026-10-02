using STO123.Services.Auth;

namespace STO123.Tests;

public class AvatarFileValidatorTests
{
    [Theory]
    [InlineData("photo.jpg", "image/jpeg", new byte[] { 0xff, 0xd8, 0xff, 0x00, 0xff, 0xd9 }, "jpg")]
    [InlineData("photo.png", "image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0, 73, 69, 78, 68, 0, 0, 0, 0 }, "png")]
    [InlineData("photo.webp", "image/webp", new byte[] { 82, 73, 70, 70, 8, 0, 0, 0, 87, 69, 66, 80, 86, 80, 56, 32 }, "webp")]
    public void Accepts_supported_image_signatures(string name, string mime, byte[] bytes, string expectedExtension)
    {
        Assert.True(AvatarFileValidator.TryValidate(name, mime, bytes, out var extension, out _));
        Assert.Equal(expectedExtension, extension);
    }

    [Fact]
    public void Rejects_mismatched_or_truncated_files()
    {
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0, 0xff, 0xd9 };
        Assert.False(AvatarFileValidator.TryValidate("photo.png", "image/png", jpeg, out _, out _));
        Assert.False(AvatarFileValidator.TryValidate("photo.jpg", "image/jpeg", jpeg[..^2], out _, out _));
        Assert.False(AvatarFileValidator.TryValidate("photo.gif", "image/gif", jpeg, out _, out _));
    }
}
