using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;
using STO123.Services.Arena;

namespace STO123.Tests;

public sealed class ArenaQuestionSelectionSqlReadTests
{
    [Fact]
    public async Task PublishedPracticeBankCanBuildEveryPartWithoutSeparatingGroups()
    {
        var connection = Environment.GetEnvironmentVariable("STO123_TEST_SQL_READONLY");
        if (string.IsNullOrWhiteSpace(connection)) return;
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlServer(connection).Options);
        var selector = new PracticePartService(db);
        foreach (var part in Enumerable.Range(1, 7))
        {
            var selected = await selector.SelectArenaQuestionsAsync(part, CancellationToken.None);
            Assert.True(selected.Count > 0, $"Part {part} has no valid Arena questions.");
            Assert.All(selected, question => {
                Assert.Equal(part, question.Part);
                Assert.False(string.IsNullOrWhiteSpace(question.Correct));
                if (part <= 4) Assert.False(string.IsNullOrWhiteSpace(question.AudioPath));
                if (part == 1) Assert.False(string.IsNullOrWhiteSpace(question.ImagePath));
                if (part == 2) Assert.Null(question.D);
                if (part >= 3) Assert.False(string.IsNullOrWhiteSpace(question.Content));
                if (part >= 6) Assert.True(!string.IsNullOrWhiteSpace(question.Context) ||
                    question.Documents.Count > 0);
                var view = ArenaQuestionPresenter.Present(question, Guid.NewGuid(), 0, false);
                Assert.Null(view.Correct);
                Assert.Null(view.Explanation);
                if (part <= 2)
                { Assert.Null(view.Content); Assert.Null(view.A); Assert.Null(view.Context); }
                if (part is 3 or 4)
                { Assert.NotNull(view.A); Assert.Null(view.Context); }
                if (part >= 5) Assert.NotNull(view.A);
            });
            if (PracticeQuestionBank.GroupedParts.Contains(part))
            {
                Assert.All(selected, question => Assert.NotNull(question.GroupId));
                Assert.All(selected.GroupBy(question => question.GroupId), group =>
                    Assert.True(group.Count() >= 2, $"Part {part} split a question group."));
                if (part is 3 or 4) Assert.All(selected.GroupBy(q => q.GroupId), group =>
                    Assert.Single(group.Select(q => q.AudioPath).Distinct()));
            }
        }
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
    }
}
