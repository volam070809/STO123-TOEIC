using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Admin;

namespace STO123.Tests;

public sealed class AdminQuestionStatisticsTests
{
    [Fact]
    public async Task Counts_unique_questions_and_only_multi_question_groups_by_bank_part_and_exam()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE PartTOEIC (MaPart INTEGER PRIMARY KEY, SoPart INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CauHoi (MaCauHoi INTEGER PRIMARY KEY, MaPart INTEGER NOT NULL, LoaiCauHoi TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE NhomCauHoi (MaCauHoi INTEGER PRIMARY KEY, MaNguLieu INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE DeThi (MaDeThi INTEGER PRIMARY KEY, TenDe TEXT NOT NULL, LoaiDe TEXT NOT NULL, TrangThai TEXT NOT NULL, MaPhienBanHienHanh INTEGER NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CauHoiDeThi (MaDeThi INTEGER NOT NULL, MaCauHoi INTEGER NOT NULL, ThuTu INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE PhienBanDeThi (MaPhienBanDeThi INTEGER PRIMARY KEY, SoCau INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO PartTOEIC VALUES (1,1),(3,3),(6,6),(7,7)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CauHoi VALUES (1,1,'DE_THI'),(2,3,'DE_THI'),(3,3,'DE_THI'),(4,3,'DE_THI'),(5,6,'DE_THI'),(6,6,'DE_THI'),(7,6,'DE_THI'),(8,6,'DE_THI'),(9,7,'LUYEN_TAP'),(10,7,'LUYEN_TAP'),(11,1,'LUYEN_TAP')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO NhomCauHoi VALUES (1,100),(2,200),(3,200),(4,200),(5,300),(6,300),(7,300),(8,300),(9,400),(10,400)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO DeThi VALUES (1,'Đề nháp','DE_THI','CLOSE',NULL),(2,'Đề xuất bản','DE_THI','OPEN',20)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CauHoiDeThi VALUES (1,2,1),(1,3,2),(1,4,3),(2,2,1),(2,3,2)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO PhienBanDeThi VALUES (20,200)");

        var result = await AdminQuestionStatisticsQueries.Load(db, CancellationToken.None);
        var examBank = Assert.Single(result.Banks, bank => bank.Bank == "DE_THI");
        var practiceBank = Assert.Single(result.Banks, bank => bank.Bank == "LUYEN_TAP");
        Assert.Equal(8, examBank.TotalQuestions);
        Assert.Equal(2, examBank.TotalGroups);
        Assert.Equal(0, examBank.Parts.Single(part => part.Part == 1).Groups);
        Assert.Equal((3, 1), (examBank.Parts.Single(part => part.Part == 3).Questions,
            examBank.Parts.Single(part => part.Part == 3).Groups));
        Assert.Equal((4, 1), (examBank.Parts.Single(part => part.Part == 6).Questions,
            examBank.Parts.Single(part => part.Part == 6).Groups));
        Assert.Equal((3, 1), (practiceBank.TotalQuestions, practiceBank.TotalGroups));
        Assert.Equal(examBank.TotalQuestions, examBank.Parts.Sum(part => part.Questions));
        Assert.Equal(practiceBank.TotalGroups, practiceBank.Parts.Sum(part => part.Groups));
        Assert.Equal(3, result.Exams.Single(exam => exam.ExamId == 1).Questions);
        Assert.Equal(200, result.Exams.Single(exam => exam.ExamId == 2).Questions);
    }
}
