using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using STO123.Services.Ai;

namespace STO123.Tests;

public sealed class AiToeicServiceTests
{
    private static readonly AiPromptMessage[] Prompt = [new("USER", "Hello")];

    [Fact]
    public async Task ParsesActualGeminiResponseShape()
    {
        var service = Service(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"Xin chào"}]}}]}""");
        Assert.Equal("Xin chào", await service.ReplyAsync(Prompt, "Beginner", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "AI_QUOTA", 429)]
    [InlineData(HttpStatusCode.Unauthorized, "AI_KEY_INVALID", 503)]
    [InlineData(HttpStatusCode.NotFound, "AI_MODEL_UNAVAILABLE", 503)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "AI_UNAVAILABLE", 503)]
    public async Task MapsGeminiErrorsToSafeVietnameseMessages(HttpStatusCode status, string code, int httpStatus)
    {
        var service = Service(status, "{}");
        var error = await Assert.ThrowsAsync<AiChatException>(() =>
            service.ReplyAsync(Prompt, "Beginner", CancellationToken.None));
        Assert.Equal(code, error.Code);
        Assert.Equal(httpStatus, error.Status);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Fact]
    public async Task RejectsUnapprovedModelBeforeCallingProvider()
    {
        var service = Service(HttpStatusCode.OK, "{}", "paid-model");
        var error = await Assert.ThrowsAsync<AiChatException>(() =>
            service.ReplyAsync(Prompt, "Beginner", CancellationToken.None));
        Assert.Equal("AI_MODEL_NOT_ALLOWED", error.Code);
    }

    private static AiToeicService Service(HttpStatusCode status, string body, string model = "gemini-3.5-flash-lite")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gemini:ApiKey"] = "test-key", ["Gemini:Model"] = model }).Build();
        return new AiToeicService(new HttpClient(new StubHandler(status, body)), config);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
