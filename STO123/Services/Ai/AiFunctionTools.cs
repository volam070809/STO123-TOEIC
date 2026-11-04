using System.Text.Json;

namespace STO123.Services.Ai;

public sealed record AiToolResult(string Text, string? ExactAnswer, IReadOnlyList<AiSource> Sources);

// Function names are the only capabilities exposed to Gemini. The authenticated learner ID
// comes from the controller, never from model arguments. All underlying retrieval is read-only.
public sealed class AiFunctionTools(AiKnowledgeService knowledge)
{
    private static readonly (string Name, string Domain, string Description)[] Capabilities =
    [
        ("get_vocabulary", "TOPICS,VOCABULARY", "Read published vocabulary topics, counts, words and meanings."),
        ("get_courses", "COURSES", "Read open courses and this learner's active course access and expiry."),
        ("get_lessons", "LESSONS", "Find authorized lessons, lectures and readable course materials."),
        ("get_progress", "PROGRESS", "Read this learner's course progress, learning path and next activity."),
        ("get_practice", "PRACTICE_STATS", "Read this learner's Part practice statistics and available practice content."),
        ("get_exams", "EXAM_RESULTS", "Read this learner's latest mock exam, placement and KNN results."),
        ("get_payments", "PAYMENT", "Read this learner's payment and enrollment status."),
        ("get_profile", "PROFILE", "Read this learner's own account information."),
        ("get_website", "WEBSITE", "Read verified STO123 learner routes, permissions and workflows.")
    ];

    public static object Declarations => new[] { new {
        functionDeclarations = Capabilities.Select(capability => new {
            name = capability.Name,
            description = capability.Description + " Call separately for each relevant information type. " +
                "Never infer database counts or account status without a tool result.",
            parameters = new {
                type = "OBJECT",
                properties = new { question = new { type = "STRING",
                    description = "The specific information to retrieve. Include a topic, Part, course or lesson name when relevant." } },
                required = new[] { "question" }
            }
        }).ToArray()
    } };

    public static bool IsKnown(string name) => Capabilities.Any(x => x.Name == name);

    public async Task<AiToolResult> ExecuteAsync(string name, JsonElement args, int learnerId,
        string originalQuestion, IReadOnlyList<AiPromptMessage> history, string? route,
        CancellationToken ct)
    {
        if (learnerId <= 0) throw new AiChatException("AI_UNAUTHORIZED", "Bạn cần đăng nhập để dùng trợ lý.", 401);
        var capability = Capabilities.FirstOrDefault(x => x.Name == name);
        if (capability.Name is null)
            throw new AiChatException("AI_TOOL_INVALID", "Công cụ truy vấn không được phép.", 503);
        var modelQuestion = args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("question", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(modelQuestion) || modelQuestion.Length > 300)
            modelQuestion = "";
        var previousQuestion = AiQuestionIntent.IsFollowUp(originalQuestion)
            ? history.LastOrDefault(message => message.Role == "USER")?.Text : null;
        // Model arguments refine the search; they cannot replace the authenticated user's
        // question or erase the referent of "chủ đề đó" / "bài đó" in a prior turn.
        var question = string.Join(" ", new[] { previousQuestion, originalQuestion, modelQuestion }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var plan = new AiRetrievalPlan(capability.Domain.Split(',').ToHashSet(StringComparer.Ordinal), []);
        var result = await knowledge.ResolveAsync(learnerId, question, null, ct, history, route, plan);
        return new AiToolResult(result.Text[..Math.Min(result.Text.Length, 12000)],
            result.ExactAnswer, result.Sources.Take(12).ToArray());
    }
}
