using System.Text.Json;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.Scoring;

namespace STO123.Tests;

public class ExamPhase1Tests
{
    private static (List<CauHoiDeThi> Links, List<PlannedUnit> Units) FixedSource()
    {
        var links = new List<CauHoiDeThi>();
        var units = new List<PlannedUnit>();
        var order = 0;
        foreach (var (part, count) in ExamCore.PartCounts)
        {
            var size = part switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 };
            for (var n = 0; n < count; n += size)
            {
                var questions = Enumerable.Range(0, size).Select(_ =>
                {
                    var id = ++order;
                    links.Add(new CauHoiDeThi { MaDeThi = 1, ThuTu = id, MaCauHoi = id });
                    return new CauHoi { MaCauHoi = id };
                }).ToList();
                units.Add(new PlannedUnit(part, null, questions, []));
            }
        }
        return (links, units);
    }

    [Fact]
    public void FixedPlanPreservesSourceOrderAndWholeGroups()
    {
        var (links, units) = FixedSource();
        var selected = FixedExamPlan.Select(links, units.AsEnumerable().Reverse());
        Assert.Equal(Enumerable.Range(1, 200), selected.SelectMany(u => u.Questions.Select(q => q.MaCauHoi)));
        foreach (var (part, count) in ExamCore.PartCounts)
            Assert.Equal(count, selected.Where(u => u.Part == part).Sum(u => u.Questions.Count));
        Assert.All(selected.Where(u => u.Part is 3 or 4 or 6 or 7),
            unit => Assert.Equal(unit.Part == 6 ? 4 : 3, unit.Questions.Count));
    }

    [Fact]
    public void FixedPlanRejectsSplitOrReorderedGroup()
    {
        var (links, units) = FixedSource();
        (links[31].MaCauHoi, links[32].MaCauHoi) = (links[32].MaCauHoi, links[31].MaCauHoi);
        Assert.Equal("INVALID_EXAM_STRUCTURE",
            Assert.Throws<ExamProblem>(() => FixedExamPlan.Select(links, units)).Code);
    }

    [Fact]
    public void HistoryUsesFinalizedFixedAveragesAndAllFinalizedMockScores()
    {
        static KetQuaLamBai Row(int id, int? exam, string status, int? score, int day, string type = ExamCore.Mock) =>
            new() { MaKetQua = id, MaDeThi = exam, LoaiBaiLam = type, TrangThai = status,
                DiemTong = score, NgayLamBai = new DateTime(2026, 1, day, 8, 0, 0, DateTimeKind.Utc),
                NgayNopBai = status is ExamCore.Submitted or ExamCore.Expired ?
                    new DateTime(2026, 1, day, 9, 0, 0, DateTimeKind.Utc) : null };
        var attempts = new List<KetQuaLamBai> {
            Row(1, 1, ExamCore.Submitted, 100, 1),
            Row(2, 1, ExamCore.Expired, 200, 2),
            Row(3, 1, ExamCore.Active, 990, 6),
            Row(4, 2, ExamCore.Submitted, 300, 3),
            Row(5, null, ExamCore.Submitted, 500, 4),
            Row(6, null, ExamCore.Expired, 400, 5),
            Row(7, null, ExamCore.Submitted, 990, 6, "PLACEMENT")
        };
        var exams = new List<DeThi> {
            new() { MaDeThi = 1, TenDe = "One", LoaiDe = "DE_THI", TrangThai = "OPEN" },
            new() { MaDeThi = 2, TenDe = "Two", LoaiDe = "DE_THI", TrangThai = "CLOSE" }
        };
        var summary = MockHistorySummary.Build(attempts, exams);
        Assert.Equal(2, summary.FixedExams[0].CompletedAttempts);
        Assert.Equal(3, summary.FixedExams[0].ActiveAttemptId);
        Assert.Equal(150m, summary.FixedExams[0].AverageScore);
        Assert.Equal(200, summary.FixedExams[0].BestScore);
        Assert.Equal(200, summary.FixedExams[0].LatestScore);
        Assert.Equal(225m, summary.Statistics.AverageBetweenFixedExams);
        Assert.Equal(500, summary.Statistics.HighestMockScore);
        Assert.Equal(400, summary.Statistics.LatestMockScore);
        Assert.Equal("CLOSE", summary.FixedExams[1].ExamStatus);
        Assert.Equal(2, summary.RandomAttempts.Count);
        Assert.All(summary.RandomAttempts, row => Assert.Contains(row.Status,
            new[] { ExamCore.Submitted, ExamCore.Expired }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PreSubmitListeningDtoHidesTranscriptAnswerTextAndKey(int part)
    {
        var question = new CauHoiLuotLam { MaCauHoiLuotLam = 1, MaPartNavigation = new PartTOEIC { SoPart = part },
            NoiDung = "answer-revealing transcript", PhuongAnA = "option text", PhuongAnB = "B",
            PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "A", GiaiThich = "secret explanation" };
        var dto = ExamQuestionProjection.BeforeSubmit(question, null);
        Assert.Null(dto.Text);
        Assert.Null(dto.A);
        Assert.Null(dto.B);
        Assert.Null(dto.C);
        Assert.Null(dto.D);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("transcript", json);
        Assert.DoesNotContain("secret explanation", json);
        Assert.DoesNotContain("PhuongAnDung", json);
    }

    [Fact]
    public void CurrentEstimatedCalculatorRemainsExplicitlyEstimated()
    {
        var calculator = new EstimatedLinearToeicScoreCalculator();
        var result = calculator.Calculate(0, 100, 100, 100);
        Assert.Equal(5, result.ListeningScore);
        Assert.Equal(495, result.ReadingScore);
        Assert.Equal(500, result.TotalScore);
        Assert.True(result.IsEstimated);
    }

    [Fact]
    public void EstimatedCalculatorUsesActualSectionTotalsForShortRandomExams()
    {
        var calculator = new EstimatedLinearToeicScoreCalculator();
        var result = calculator.Calculate(2, 4, 3, 6);
        Assert.Equal(250, result.ListeningScore);
        Assert.Equal(250, result.ReadingScore);
        Assert.True(result.IsEstimated);
    }
}
