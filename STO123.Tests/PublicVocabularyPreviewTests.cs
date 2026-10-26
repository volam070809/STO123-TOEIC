using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using STO123.Controllers;
using STO123.DTOs.Vocabulary;
using STO123.Models;
using STO123.Services.Vocabulary;

namespace STO123.Tests;

public sealed class PublicVocabularyPreviewTests
{
    [Fact]
    public async Task Preview_is_deterministic_limited_and_independent_per_open_topic()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE ChuDe (
                MaChuDe INTEGER PRIMARY KEY,
                TenChuDe TEXT NOT NULL,
                MoTa TEXT NULL,
                TrangThai TEXT NOT NULL
            );
            CREATE TABLE TuVung (
                MaTuVung INTEGER PRIMARY KEY,
                TuVung TEXT NOT NULL,
                Nghia TEXT NOT NULL,
                PhienAm TEXT NULL,
                ViDu TEXT NULL,
                DichViDu TEXT NULL,
                DuongDanAudio TEXT NULL,
                LoaiTu TEXT NULL,
                DuongDanAudioViDu TEXT NULL
            );
            CREATE TABLE TuVungChuDe (
                MaChuDe INTEGER NOT NULL,
                MaTuVung INTEGER NOT NULL,
                PRIMARY KEY (MaChuDe, MaTuVung)
            );
            """);

        db.ChuDe.AddRange(
            new ChuDe { MaChuDe = 1, TenChuDe = "A", TrangThai = "DANG_MO" },
            new ChuDe { MaChuDe = 2, TenChuDe = "B", TrangThai = "DANG_MO" },
            new ChuDe { MaChuDe = 3, TenChuDe = "Closed", TrangThai = "DONG" });
        for (var id = 1; id <= 15; id++)
            db.TuVung.Add(new TuVung { MaTuVung = id, TuVung1 = $"word-{id}", Nghia = $"meaning-{id}" });
        db.TuVungChuDe.AddRange(Enumerable.Range(1, 12)
            .Select(id => new TuVungChuDe { MaChuDe = 1, MaTuVung = id }));
        db.TuVungChuDe.AddRange(Enumerable.Range(8, 5)
            .Select(id => new TuVungChuDe { MaChuDe = 2, MaTuVung = id }));
        db.TuVungChuDe.Add(new TuVungChuDe { MaChuDe = 3, MaTuVung = 15 });
        await db.SaveChangesAsync();

        var first = await PublicVocabularyQueries.PreviewLinks(db, 1).Select(link => link.MaTuVung).ToListAsync();
        var second = await PublicVocabularyQueries.PreviewLinks(db, 2).Select(link => link.MaTuVung).ToListAsync();
        var closed = await PublicVocabularyQueries.PreviewLinks(db, 3).Select(link => link.MaTuVung).ToListAsync();

        Assert.Equal(Enumerable.Range(1, 10), first);
        Assert.Equal(Enumerable.Range(8, 5), second);
        Assert.Empty(closed);
        Assert.DoesNotContain(11, first);
    }

    [Fact]
    public void Public_contract_is_anonymous_get_only_and_has_no_progress_fields()
    {
        Assert.Equal(10, PublicVocabularyQueries.PreviewLimit);
        Assert.Single(typeof(PublicVocabularyController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Empty(typeof(PublicVocabularyController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true));

        var properties = typeof(PublicVocabularyWordDto).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.DoesNotContain("TrangThai", properties);
        Assert.DoesNotContain("Progress", properties);
        Assert.DoesNotContain("AudioPath", properties);
        Assert.DoesNotContain("UserId", properties);
    }
}
