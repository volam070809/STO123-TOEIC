namespace STO123.Services.SmartLearning;

public sealed record SkillAnswer(int Part, string Source, string Session, int QuestionId,
    bool Correct, DateTime CompletedAt, int? GroupId = null);

public sealed record PracticeAggregate(int Part, int Sessions, int Questions, int Correct,
    int LatestQuestions, int LatestCorrect, DateTime? LastAt);

public sealed record SourceEvidence(string Source, int Questions, double EffectiveQuestions);

public sealed record PartSkill(int Part, int EstimatedAccuracy, string Confidence, string Trend,
    int EvidenceQuestions, double EffectiveQuestions, int IndependentSessions,
    int? PreviousAccuracy, int? RecentAccuracy, double Priority, string Explanation,
    IReadOnlyList<SourceEvidence> Sources);

public sealed record PartAdvice(int Part, string Reason, string Confidence, int EstimatedAccuracy,
    int EvidenceQuestions, string ActionUrl);

public sealed record RecommendationOutcome(int Part, decimal? Before, decimal? After,
    int? Questions, DateTime? CompletedAt, bool InProgress);

public sealed record SmartLearningProfile(DateTime CalculatedAt, IReadOnlyList<PartSkill> Parts,
    IReadOnlyList<PartAdvice> Recommendations)
{
    public IReadOnlyList<RecommendationOutcome> RecommendationOutcomes { get; init; } = [];
}

/// <summary>Deterministic evidence model; estimates are learning guidance, never official TOEIC scores.</summary>
public static class SkillEstimator
{
    // A full exam is strongest evidence. Casual Arena questions are supplementary.
    public static readonly IReadOnlyDictionary<string, double> SourceWeights = new Dictionary<string, double> {
        ["PLACEMENT"] = 1.0, ["MOCK"] = .9, ["PRACTICE"] = .45, ["ARENA"] = .25
    };
    private static readonly int[] ConfidenceTargets = [12, 30, 30, 24, 30, 20, 35];
    private static readonly int[] PracticePerSessionCaps = [6, 12, 9, 9, 12, 8, 12];

    public static SmartLearningProfile Estimate(IEnumerable<SkillAnswer> answers,
        IEnumerable<PracticeAggregate> practice, DateTime now)
    {
        var answerRows = answers.Where(x => x.Part is >= 1 and <= 7 && x.QuestionId > 0 &&
            SourceWeights.ContainsKey(x.Source)).ToArray();
        var practiceRows = practice.Where(x => x.Part is >= 1 and <= 7).ToDictionary(x => x.Part);
        var parts = new List<PartSkill>(7);
        foreach (var part in Enumerable.Range(1, 7))
        {
            var rows = answerRows.Where(x => x.Part == part)
                .OrderByDescending(x => x.CompletedAt).ToArray();
            var seenQuestions = new HashSet<int>();
            var seenGroupInSession = new HashSet<(string Session, int GroupId)>();
            var sourceTotals = new Dictionary<string, (int Raw, double Effective)>();
            double weightedCorrect = 0, effective = 0;
            foreach (var row in rows)
            {
                var ageDays = Math.Max(0, (now - row.CompletedAt).TotalDays);
                var recency = Math.Max(.45, Math.Pow(.5, ageDays / 180));
                var repeated = seenQuestions.Add(row.QuestionId) ? 1.0 : .4;
                var grouped = row.GroupId is { } groupId &&
                    !seenGroupInSession.Add((row.Session, groupId)) ? .75 : 1.0;
                var weight = SourceWeights[row.Source] * recency * repeated * grouped;
                effective += weight;
                if (row.Correct) weightedCorrect += weight;
                var previous = sourceTotals.GetValueOrDefault(row.Source);
                sourceTotals[row.Source] = (previous.Raw + 1, previous.Effective + weight);
            }
            practiceRows.TryGetValue(part, out var aggregate);
            var practiceQuestions = Math.Max(0, aggregate?.Questions ?? 0);
            var practiceCorrect = Math.Clamp(aggregate?.Correct ?? 0, 0, practiceQuestions);
            var practiceSessions = Math.Max(0, aggregate?.Sessions ?? 0);
            if (practiceQuestions > 0 && practiceSessions > 0)
            {
                var cap = Math.Min(practiceQuestions, practiceSessions * PracticePerSessionCaps[part - 1]);
                var ageDays = aggregate?.LastAt is { } last ? Math.Max(0, (now - last).TotalDays) : 180;
                var weight = cap * SourceWeights["PRACTICE"] * Math.Max(.45, Math.Pow(.5, ageDays / 180));
                effective += weight;
                weightedCorrect += weight * practiceCorrect / practiceQuestions;
                sourceTotals["PRACTICE"] = (practiceQuestions, weight);
            }

            // Beta(2,2) prevents a few lucky answers from becoming a confident 0% or 100% claim.
            var accuracy = (int)Math.Round(100 * (weightedCorrect + 2) / (effective + 4));
            var distinctSessions = rows.Select(x => x.Session).Distinct(StringComparer.Ordinal).Count() + practiceSessions;
            var target = ConfidenceTargets[part - 1];
            var confidence = effective >= target * 1.5 && distinctSessions >= 3 ? "CAO" :
                effective >= target && distinctSessions >= 2 ? "TRUNG_BINH" : "TAM_THOI";
            var (trend, previousAccuracy, recentAccuracy) = Trend(rows, aggregate);
            var uncertainty = confidence == "TAM_THOI" ? 28 : confidence == "TRUNG_BINH" ? 8 : 0;
            var priority = Math.Max(0, 80 - accuracy) + uncertainty + (trend == "GIAM" ? 18 : 0);
            var explanation = confidence == "TAM_THOI" ?
                $"Ước tính tạm thời từ {rows.Length + practiceQuestions} câu trong {distinctSessions} lượt; cần thêm bài làm độc lập để đánh giá chắc chắn." :
                $"Ước tính từ {rows.Length + practiceQuestions} câu trong {distinctSessions} lượt, đã giảm ảnh hưởng của câu gặp lại và dữ liệu cũ.";
            parts.Add(new PartSkill(part, accuracy, confidence, trend,
                rows.Length + practiceQuestions, Math.Round(effective, 1), distinctSessions,
                previousAccuracy, recentAccuracy, priority, explanation,
                sourceTotals.OrderBy(x => x.Key).Select(x =>
                    new SourceEvidence(x.Key, x.Value.Raw, Math.Round(x.Value.Effective, 1))).ToArray()));
        }
        var recommendations = parts.OrderByDescending(x => x.Priority).ThenBy(x => x.Part).Take(3)
            .Select(x => new PartAdvice(x.Part, Reason(x), x.Confidence, x.EstimatedAccuracy,
                x.EvidenceQuestions, $"/practice/parts?part={x.Part}&recommended=1")).ToArray();
        return new SmartLearningProfile(now, parts, recommendations);
    }

    private static string Reason(PartSkill part)
    {
        if (part.Confidence == "TAM_THOI")
            return part.EvidenceQuestions == 0 ?
                $"Part {part.Part} chưa có kết quả. Hãy luyện một lượt để bắt đầu đánh giá." :
                $"Part {part.Part} cần thêm chứng cứ từ các lượt độc lập trước khi kết luận điểm yếu.";
        if (part.Trend == "GIAM") return $"Part {part.Part} có kết quả gần đây thấp hơn lượt trước; nên ôn lại.";
        if (part.EstimatedAccuracy < 70) return $"Part {part.Part} có tỷ lệ đúng ước tính {part.EstimatedAccuracy}%; nên ưu tiên luyện tiếp.";
        return $"Duy trì Part {part.Part} và kiểm tra lại sau những lượt luyện mới.";
    }

    private static (string Trend, int? Previous, int? Recent) Trend(
        IReadOnlyList<SkillAnswer> rows, PracticeAggregate? practice)
    {
        var sessions = rows.GroupBy(x => x.Session)
            .Select(g => new { At = g.Max(x => x.CompletedAt), Count = g.Count(),
                Rate = 100.0 * g.Count(x => x.Correct) / g.Count() })
            .Where(x => x.Count >= 3).OrderByDescending(x => x.At).Take(2).ToArray();
        if (sessions.Length == 2)
            return ClassifyTrend(sessions[1].Rate, sessions[0].Rate);
        if (practice is { Sessions: >= 2, LatestQuestions: >= 3 } &&
            practice.Questions - practice.LatestQuestions >= 3)
        {
            var previous = 100.0 * (practice.Correct - practice.LatestCorrect) /
                (practice.Questions - practice.LatestQuestions);
            var recent = 100.0 * practice.LatestCorrect / practice.LatestQuestions;
            return ClassifyTrend(previous, recent);
        }
        return ("CHUA_DU", null, null);
    }

    private static (string Trend, int? Previous, int? Recent) ClassifyTrend(double previous, double recent)
        => (recent - previous >= 12 ? "TANG" : recent - previous <= -12 ? "GIAM" : "ON_DINH",
            (int)Math.Round(previous), (int)Math.Round(recent));
}
