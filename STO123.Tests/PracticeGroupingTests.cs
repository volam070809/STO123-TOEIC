using System.Collections;
using System.Reflection;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class PracticeGroupingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ListeningIndependentQuestionsKeepTheirOwnMedia(int part)
    {
        var rowType = typeof(PracticePartService).GetNestedType("SourceRow", BindingFlags.NonPublic)!;
        IList NewList() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        var source = NewList();
        var members = NewList();
        for (var i = 1; i <= 2; i++)
        {
            var question = new CauHoi { MaCauHoi = i, MaPart = part, DoKho = 1,
                LoaiCauHoi = PracticeQuestionBank.Discriminator, TrangThai = ExamCore.Published,
                PhuongAnA = "A", PhuongAnB = "B", PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "A" };
            var row = Activator.CreateInstance(rowType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [question, part, 87 + i, 1], null)!;
            source.Add(row);
            members.Add(row);
        }
        var resources = new[] { 88, 89 }.Select(id => new NguLieu { MaNguLieu = id,
            DuongDanAudio = $"{id}.mp3", DuongDanAnh = $"{id}.png" }).ToArray();
        var units = Build(source, members, rowType, resources, []);
        Assert.Equal(2, units.Length);
        Assert.Equal(new[] { 88, 89 }, units.Select(unit =>
            ((NguLieu)unit.GetType().GetProperty("Resource")!.GetValue(unit)!).MaNguLieu));
    }

    [Fact]
    public void PartFiveQuestionsDoNotGainArtificialGroups()
    {
        var rowType = typeof(PracticePartService).GetNestedType("SourceRow", BindingFlags.NonPublic)!;
        var source = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        for (var i = 1; i <= 2; i++)
        {
            var question = new CauHoi { MaCauHoi = i, MaPart = 5, DoKho = 1,
                LoaiCauHoi = PracticeQuestionBank.Discriminator, TrangThai = ExamCore.Published,
                PhuongAnA = "A", PhuongAnB = "B", PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "A" };
            source.Add(Activator.CreateInstance(rowType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [question, 5, null, null], null)!);
        }
        var units = Build(source, (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!, rowType, [], []);
        Assert.Equal(2, units.Length);
        Assert.All(units, unit => Assert.Null(unit.GetType().GetProperty("Resource")!.GetValue(unit)));
    }

    [Theory]
    [InlineData(3, 3, 0)]
    [InlineData(4, 3, 0)]
    [InlineData(6, 4, 0)]
    [InlineData(7, 2, 1)]
    [InlineData(7, 3, 2)]
    [InlineData(7, 5, 3)]
    public void CompleteGroupsKeepSourceOrderAndDocuments(int part, int count, int documentCount)
    {
        var (source, members, rowType) = Rows(part, count, mixedDifficulty: false);
        var resource = new NguLieu { MaNguLieu = 88, DuongDanAudio = "audio.mp3",
            NoiDungNguLieu = "Passage with [1] [2] [3] [4]", DuongDanAnh = "image.png" };
        var documents = Enumerable.Range(1, documentCount).Select(i => new NguLieuTaiLieu {
            MaNguLieu = 88, ThuTu = i, LoaiTaiLieu = "TEXT", NoiDung = $"Document {i}"
        }).Reverse().ToArray();
        var units = Build(source, members, rowType, [resource], documents);
        var unit = Assert.Single(units);
        var questions = (IEnumerable<CauHoi>)unit.GetType().GetProperty("Questions")!.GetValue(unit)!;
        Assert.Equal(Enumerable.Range(1, count), questions.Select(q => q.MaCauHoi));
        var selectedDocuments = (IEnumerable<NguLieuTaiLieu>)unit.GetType().GetProperty("Documents")!.GetValue(unit)!;
        Assert.Equal(Enumerable.Range(1, documentCount), selectedDocuments.Select(d => d.ThuTu));
    }

    [Fact]
    public void MixedDifficultyGroupIsIncludedWholeAtRoundedAverage()
    {
        var (source, members, rowType) = Rows(6, 4, mixedDifficulty: true);
        var resource = new NguLieu { MaNguLieu = 88, NoiDungNguLieu = "Passage" };
        var unit = Assert.Single(Build(source, members, rowType, [resource], []));
        Assert.Equal(1, unit.GetType().GetProperty("Difficulty")!.GetValue(unit));
        var questions = (IEnumerable<CauHoi>)unit.GetType().GetProperty("Questions")!.GetValue(unit)!;
        Assert.Equal([1, 2, 3, 4], questions.Select(q => q.MaCauHoi));
        Assert.Equal([1, 1, 1, 2], questions.Select(q => (int)q.DoKho));
    }

    [Theory]
    [InlineData(1, 2, 2)]
    [InlineData(2, 3, 3)]
    public void SqlServerMidpointRoundingAndDifficultyFilterKeepWholeGroup(int first, int second, int expected)
    {
        Assert.Equal(expected, PracticeQuestionBank.GroupDifficulty([first, second]));
        var (source, members, rowType) = Rows(7, 2, mixedDifficulty: true);
        foreach (var list in new[] { source, members })
            foreach (var row in list)
            {
                var question = (CauHoi)row!.GetType().GetProperty("Question")!.GetValue(row)!;
                question.DoKho = (byte)(question.MaCauHoi == 1 ? first : second);
            }
        var documents = new[] { new NguLieuTaiLieu { MaNguLieu = 88, ThuTu = 1,
            LoaiTaiLieu = "TEXT", NoiDung = "Passage" } };
        var unit = Assert.Single(Build(source, members, rowType,
            [new NguLieu { MaNguLieu = 88 }], documents));
        Assert.Equal(expected, unit.GetType().GetProperty("Difficulty")!.GetValue(unit));
        var unitType = unit.GetType();
        var typedList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(unitType))!;
        typedList.Add(unit);
        var filter = typeof(PracticePartService).GetMethod("FilterDifficulty", BindingFlags.NonPublic | BindingFlags.Static)!;
        var selected = (IEnumerable)filter.Invoke(null, [typedList, expected])!;
        Assert.Single(selected.Cast<object>());
        var wrong = (IEnumerable)filter.Invoke(null, [typedList, expected == 1 ? 2 : 1])!;
        Assert.Empty(wrong.Cast<object>());
    }

    [Theory]
    [InlineData("DE_THI", "XUAT_BAN")]
    [InlineData("LUYEN_TAP", "BAN_NHAP")]
    public void MixedGroupStillRejectsNonPracticeOrUnpublishedMember(string type, string status)
    {
        var (source, members, rowType) = Rows(6, 4, mixedDifficulty: true);
        foreach (var row in members)
        {
            var question = (CauHoi)row!.GetType().GetProperty("Question")!.GetValue(row)!;
            if (question.MaCauHoi != 4) continue;
            question.LoaiCauHoi = type;
            question.TrangThai = status;
        }
        Assert.Empty(Build(source, members, rowType,
            [new NguLieu { MaNguLieu = 88, NoiDungNguLieu = "Passage" }], []));
    }

    [Fact]
    public void MixedPartSevenGroupStillRejectsInvalidStructuredDocument()
    {
        var (source, members, rowType) = Rows(7, 2, mixedDifficulty: true);
        var invalidEmail = new NguLieuTaiLieu { MaNguLieu = 88, ThuTu = 1,
            LoaiTaiLieu = "EMAIL", NoiDung = "{invalid-json" };
        Assert.Empty(Build(source, members, rowType,
            [new NguLieu { MaNguLieu = 88 }], [invalidEmail]));
    }

    private static (IList Source, IList Members, Type RowType) Rows(int part, int count, bool mixedDifficulty)
    {
        var rowType = typeof(PracticePartService).GetNestedType("SourceRow", BindingFlags.NonPublic)!;
        IList NewList() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        var source = NewList();
        var members = NewList();
        for (var i = 1; i <= count; i++)
        {
            var question = new CauHoi { MaCauHoi = i, MaPart = part, DoKho = (byte)(mixedDifficulty && i == count ? 2 : 1),
                LoaiCauHoi = PracticeQuestionBank.Discriminator, TrangThai = ExamCore.Published,
                PhuongAnA = "A", PhuongAnB = "B", PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "A" };
            var row = Activator.CreateInstance(rowType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [question, part, 88, i], null)!;
            source.Add(row);
            members.Add(row);
        }
        return (source, members, rowType);
    }

    private static object[] Build(IList source, IList members, Type rowType, NguLieu[] resources,
        NguLieuTaiLieu[] documents)
    {
        var method = typeof(PracticePartService).GetMethod("BuildUnits", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IEnumerable)method.Invoke(null, [source, members, resources, documents])!;
        return result.Cast<object>().ToArray();
    }
}
