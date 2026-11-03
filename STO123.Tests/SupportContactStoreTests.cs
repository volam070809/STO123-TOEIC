using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class SupportContactStoreTests
{
    [Fact]
    public void Initial_links_are_the_requested_destinations()
    {
        Assert.Equal("https://zalo.me/0342463560", SupportContactStore.DefaultZalo);
        Assert.Equal("https://m.me/lam.vo.561222", SupportContactStore.DefaultMessenger);
    }

    [Theory]
    [InlineData("https://zalo.me/0342463560", "https://m.me/lam.vo.561222", true)]
    [InlineData("http://zalo.me/0342463560", "https://m.me/lam.vo.561222", false)]
    [InlineData("https://zalo.me.evil.test/0342463560", "https://m.me/lam.vo.561222", false)]
    [InlineData("https://zalo.me/0342463560", "https://m.me.evil.test/lam.vo.561222", false)]
    [InlineData("https://zalo.me/0342463560", "https://m.me/lam.vo.561222?redirect=evil", false)]
    [InlineData("https://zalo.me/a/b", "https://m.me/lam.vo.561222", false)]
    public void Only_direct_https_contact_destinations_are_accepted(string zalo, string messenger, bool valid)
    {
        var input = new SaveSupportContactRequest(zalo, true, messenger, true, null);
        Assert.Equal(valid, SupportContactStore.ValidationError(input) is null);
    }

    [Fact]
    public void Disabled_contact_can_be_empty_but_enabled_contact_requires_url()
    {
        Assert.Null(SupportContactStore.ValidationError(new(null, false, null, false, null)));
        Assert.NotNull(SupportContactStore.ValidationError(new(null, true, null, false, null)));
        Assert.NotNull(SupportContactStore.ValidationError(new(null, false, null, true, null)));
    }
}
