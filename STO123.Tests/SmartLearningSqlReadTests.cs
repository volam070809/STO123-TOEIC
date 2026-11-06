using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.SmartLearning;

namespace STO123.Tests;

public sealed class SmartLearningSqlReadTests
{
    [Fact]
    public async Task HistoricalCompletedResultsProduceSevenPartProfileFromRealSql()
    {
        var connection = Environment.GetEnvironmentVariable("STO123_TEST_SQL_READONLY");
        if (string.IsNullOrWhiteSpace(connection)) return;
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlServer(connection).Options);
        var learner = await db.KetQuaLamBai.AsNoTracking()
            .Where(x => x.TrangThai == ExamCore.Submitted &&
                (x.LoaiBaiLam == ExamCore.Mock || x.LoaiBaiLam == ExamCore.Placement))
            .GroupBy(x => x.MaHocVien).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefaultAsync();
        Assert.True(learner > 0, "CSDL kiểm thử cần có ít nhất một bài thi đã hoàn thành.");
        var profile = await new SmartLearningService(db).ProfileAsync(learner, CancellationToken.None);
        Assert.Equal(7, profile.Parts.Count);
        Assert.Contains(profile.Parts, part => part.Sources.Any(source =>
            source.Source is ExamCore.Mock or ExamCore.Placement && source.Questions > 0));
        Assert.Equal(3, profile.Recommendations.Count);
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
    }
}
