using Microsoft.EntityFrameworkCore;
using STO123.Models;

namespace STO123.Services.Vocabulary;

public static class PublicVocabularyQueries
{
    public const int PreviewLimit = 10;

    public static IQueryable<TuVungChuDe> PreviewLinks(ToeicDbContext db, int topicId) =>
        db.TuVungChuDe.AsNoTracking()
            .Where(link => link.MaChuDe == topicId && link.MaChuDeNavigation.TrangThai == "DANG_MO")
            .OrderBy(link => link.MaTuVung)
            .Take(PreviewLimit);
}
