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

    [Fact]
    public async Task SelectsOnlyWhitelistedReadOnlyCapabilitiesFromNewQuestion()
    {
        var service = Service(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"{\"domains\":[\"PRACTICE_STATS\",\"COURSES\",\"LESSONS\",\"ADMIN\"],\"terms\":[\"Part 7\",\"'; DELETE TABLE\"]}"}]}}]}""");
        var plan = await service.PlanAsync("Tôi yếu Part 7, trong khóa đã mua nên học bài nào?",
            [new AiPromptMessage("USER", "Tôi vừa xem kết quả luyện tập")], "Khóa học", default);
        Assert.NotNull(plan);
        Assert.True(plan.Includes("PRACTICE_STATS"));
        Assert.True(plan.Includes("COURSES"));
        Assert.True(plan.Includes("LESSONS"));
        Assert.False(plan.Includes("ADMIN"));
        Assert.Equal(["Part 7"], plan.Terms);
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
