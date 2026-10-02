using STO123.Services.Auth;

namespace STO123.Tests;

public class AvatarStorageTests
{
    [Fact]
    public void Accepts_only_https_provider_pictures()
    {
        Assert.Equal("https://lh3.googleusercontent.com/photo", AvatarStorage.ValidProviderUrl("https://lh3.googleusercontent.com/photo"));
        Assert.Null(AvatarStorage.ValidProviderUrl(null));
        Assert.Null(AvatarStorage.ValidProviderUrl("http://example.com/photo"));
        Assert.Null(AvatarStorage.ValidProviderUrl("not a URL"));
    }

    [Fact]
    public void Custom_reference_belongs_only_to_its_learner()
    {
        Assert.True(AvatarStorage.IsCustomReference(12, "avatars/12/abc.webp"));
        Assert.False(AvatarStorage.IsCustomReference(1, "avatars/12/abc.webp"));
        Assert.False(AvatarStorage.IsCustomReference(12, "https://lh3.googleusercontent.com/photo"));
    }
}
