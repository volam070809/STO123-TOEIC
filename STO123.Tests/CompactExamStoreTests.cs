using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class CompactExamStoreTests
{
    [Fact]
    public async Task Result_uses_saved_answer_keys_and_parts_without_querying_question_bank()
    {
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var ids = Enumerable.Range(1, 200).ToList();
        var data = new CompactExamData(1, ids,
            new Dictionary<int, CompactAnswer> { [1] = new("B", false) },
            ids.ToDictionary(id => id, _ => "A"),
            ids.ToDictionary(id => id, id => id <= 100 ? 1 : 5));
        var saved = CompactExamStore.Decode(CompactExamStore.Encode(data));
        var snapshot = await new CompactExamStore(db).BuildSnapshotAsync(
            new KetQuaLamBai { MaKetQua = 7, LoaiBaiLam = ExamCore.Mock },
            saved, SnapshotView.Result, CancellationToken.None);

        Assert.Equal(200, snapshot.Questions.Count);
        Assert.Equal("A", snapshot.Questions[0].PhuongAnDung);
        Assert.Equal(5, snapshot.Questions[199].MaPartNavigation.SoPart);
        Assert.Equal("B", snapshot.Answers[1].DapAnChon);
    }
}
