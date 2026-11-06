using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using STO123.Models;
using STO123.Services.Arena;
using STO123.Services.Exam;

namespace STO123.Services.SmartLearning;

public sealed class SmartLearningService(ToeicDbContext db)
{
    public async Task<SmartLearningProfile> ProfileAsync(int learnerId, CancellationToken ct)
    {
        var practice = await db.ThongKeLuyenPart.AsNoTracking()
            .Where(x => x.MaHocVien == learnerId)
            .Select(x => new PracticeAggregate(x.MaPart, x.SoLanLuyen, x.TongCauDaLam,
                x.TongCauDung, x.SoCauLanCuoi, x.SoDungLanCuoi, x.LanLuyenGanNhat))
            .ToListAsync(ct);
        var attempts = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaHocVien == learnerId &&
                (x.LoaiBaiLam == ExamCore.Placement || x.LoaiBaiLam == ExamCore.Mock) &&
                (x.TrangThai == ExamCore.Submitted || x.TrangThai == ExamCore.Expired))
            .OrderByDescending(x => x.NgayNopBai).ThenByDescending(x => x.MaKetQua)
            .Select(x => new { x.MaKetQua, x.MaDeThi, x.LoaiBaiLam, x.NgayNopBai,
                x.DuLieuBaiLamJson })
            .ToListAsync(ct);
        // Learners see one completed placement and the latest completion of each mock exam.
        // Older physical rows may still exist for audit; they must not bias the profile.
        var latest = attempts.GroupBy(x => x.LoaiBaiLam == ExamCore.Placement ?
                "PLACEMENT" : $"MOCK:{x.MaDeThi?.ToString() ?? "RANDOM"}")
            .Select(g => g.First()).ToArray();
        var answers = new List<SkillAnswer>();
        var legacyIds = new List<int>();
        var missingPartCompacts = new List<(int ResultId, string Kind, DateTime At, CompactExamData Data)>();
        foreach (var row in latest)
        {
            if (string.IsNullOrWhiteSpace(row.DuLieuBaiLamJson)) { legacyIds.Add(row.MaKetQua); continue; }
            CompactExamData data;
            try { data = CompactExamStore.Decode(row.DuLieuBaiLamJson); }
            catch (ExamProblem) { legacyIds.Add(row.MaKetQua); continue; }
            var completedAt = row.NgayNopBai ?? DateTime.UtcNow;
            if (data.PartNumbers is null || data.AnswerKeys is null)
            {
                // Older compact attempts kept their answer keys but not Part numbers.
                // Prefer an immutable legacy snapshot if one exists; otherwise resolve
                // surviving source questions without substituting new answer keys.
                legacyIds.Add(row.MaKetQua);
                if (data.AnswerKeys is not null)
                    missingPartCompacts.Add((row.MaKetQua, row.LoaiBaiLam, completedAt, data));
                continue;
            }
            var session = $"{row.LoaiBaiLam}:{row.MaKetQua}";
            foreach (var questionId in data.QuestionIds)
            {
                if (!data.PartNumbers.TryGetValue(questionId, out var part) ||
                    !data.AnswerKeys.TryGetValue(questionId, out var correct)) continue;
                var selected = data.Answers.GetValueOrDefault(questionId)?.Selected;
                answers.Add(new SkillAnswer(part, row.LoaiBaiLam, session, questionId,
                    string.Equals(selected, correct, StringComparison.OrdinalIgnoreCase), completedAt));
            }
        }
        if (legacyIds.Count > 0)
        {
            var legacy = await db.CauHoiLuotLam.AsNoTracking()
                .Where(q => legacyIds.Contains(q.MaKetQua))
                .Select(q => new { q.MaKetQua, q.MaCauHoiGoc, q.MaCauHoiLuotLam, q.MaNhomLuotLam,
                    part = q.MaPartNavigation.SoPart, correct = q.PhuongAnDung,
                    selected = q.ChiTietKetQua == null ? null : q.ChiTietKetQua.DapAnChon })
                .ToListAsync(ct);
            var latestById = latest.ToDictionary(x => x.MaKetQua);
            foreach (var question in legacy)
            {
                var result = latestById[question.MaKetQua];
                answers.Add(new SkillAnswer(question.part, result.LoaiBaiLam,
                    $"{result.LoaiBaiLam}:{result.MaKetQua}",
                    question.MaCauHoiGoc ?? question.MaCauHoiLuotLam,
                    string.Equals(question.selected?.Trim(), question.correct?.Trim(),
                        StringComparison.OrdinalIgnoreCase), result.NgayNopBai ?? DateTime.UtcNow,
                    question.MaNhomLuotLam));
            }
            var hasLegacy = legacy.Select(x => x.MaKetQua).ToHashSet();
            var fallback = missingPartCompacts.Where(x => !hasLegacy.Contains(x.ResultId)).ToArray();
            if (fallback.Length > 0)
            {
                var ids = fallback.SelectMany(x => x.Data.QuestionIds).Distinct().ToArray();
                var sourceParts = await db.CauHoi.AsNoTracking()
                    .Where(q => ids.Contains(q.MaCauHoi))
                    .Select(q => new { q.MaCauHoi, part = q.MaPartNavigation.SoPart })
                    .ToDictionaryAsync(x => x.MaCauHoi, x => x.part, ct);
                foreach (var compact in fallback)
                    foreach (var questionId in compact.Data.QuestionIds)
                    {
                        if (!sourceParts.TryGetValue(questionId, out var part) ||
                            !compact.Data.AnswerKeys!.TryGetValue(questionId, out var correct)) continue;
                        answers.Add(new SkillAnswer(part, compact.Kind,
                            $"{compact.Kind}:{compact.ResultId}", questionId,
                            string.Equals(compact.Data.Answers.GetValueOrDefault(questionId)?.Selected,
                                correct, StringComparison.OrdinalIgnoreCase), compact.At));
                    }
            }
        }
        var matches = await db.ArenaMatch.AsNoTracking().Where(x => x.Status == "COMPLETED" &&
                (x.OwnerId == learnerId || x.OpponentId == learnerId))
            .Select(x => new { x.Id, x.Part, x.CompletedUtc, x.QuestionsJson, x.AnswersJson })
            .ToListAsync(ct);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var match in matches)
        {
            try
            {
                var questions = JsonSerializer.Deserialize<List<ArenaQuestionSnapshot>>(
                    match.QuestionsJson ?? "[]", json) ?? [];
                var submissions = JsonSerializer.Deserialize<List<ArenaSubmission>>(
                    match.AnswersJson, json) ?? [];
                foreach (var submission in submissions.Where(x => x.PlayerId == learnerId &&
                    x.QuestionIndex >= 0 && x.QuestionIndex < questions.Count)
                    .DistinctBy(x => x.QuestionIndex))
                {
                    var question = questions[submission.QuestionIndex];
                    answers.Add(new SkillAnswer(match.Part, "ARENA", $"ARENA:{match.Id}",
                        question.QuestionId, submission.Correct == true,
                        match.CompletedUtc ?? DateTime.UtcNow, question.GroupId));
                }
            }
            catch (JsonException) { /* Do not turn a corrupt historical match into fabricated evidence. */ }
        }
        var profile = SkillEstimator.Estimate(answers, practice, DateTime.UtcNow);
        var followUps = await db.SmartLearningFollowUp.AsNoTracking()
            .Where(x => x.MaHocVien == learnerId)
            .Select(x => new { x.MaPart, x.TyLeTruocGanNhat, x.TyLeSauGanNhat,
                x.SoCauSauGanNhat, x.HoanThanhLucUtc, x.MaLuotDangLuyen })
            .ToListAsync(ct);
        var pendingIds = followUps.Where(x => x.MaLuotDangLuyen.HasValue)
            .Select(x => x.MaLuotDangLuyen!.Value).ToArray();
        var activePending = pendingIds.Length == 0 ? [] : await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.MaHocVien == learnerId && x.LoaiBaiLam == PracticeQuestionBank.AttemptType &&
                x.TrangThai == ExamCore.Active && pendingIds.Contains(x.MaKetQua))
            .Select(x => x.MaKetQua).ToArrayAsync(ct);
        return profile with { RecommendationOutcomes = followUps.Select(x => new RecommendationOutcome(
                x.MaPart, x.TyLeTruocGanNhat, x.TyLeSauGanNhat,
                x.SoCauSauGanNhat, x.HoanThanhLucUtc is { } at ?
                    DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
                x.MaLuotDangLuyen.HasValue && activePending.Contains(x.MaLuotDangLuyen.Value))).ToArray() };
    }
}
