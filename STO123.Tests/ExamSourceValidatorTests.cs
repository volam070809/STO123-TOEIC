using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public class ExamSourceValidatorTests
{
    private sealed class Fixture
    {
        public DeThi Exam { get; } = new() { LoaiDe = "DE_THI", TrangThai = "CLOSE", ThoiGianLamBai = 120 };
        public List<CauHoiDeThi> Links { get; } = [];
        public Dictionary<int, CauHoi> Questions { get; } = [];
        public Dictionary<int, int> Parts { get; } = Enumerable.Range(1, 7).ToDictionary(x => x, x => x);
        public List<NhomCauHoi> Members { get; } = [];
        public Dictionary<int, NguLieu> Resources { get; } = [];
        public List<NguLieuTaiLieu> Documents { get; } = [];
        public HashSet<string> Blobs { get; } = [];

        public ExamValidationReport Validate() => ExamSourceValidator.ValidateFixed(Exam, Links,
            Questions, Parts, Members, Resources, Documents, Blobs);
    }

    private static Fixture CompleteExam()
    {
        var fixture = new Fixture();
        var nextQuestion = 0;
        var nextResource = 0;
        foreach (var (part, count) in ExamCore.PartCounts)
        {
            var size = part switch { 3 or 4 or 7 => 3, 6 => 4, _ => 1 };
            for (var offset = 0; offset < count; offset += size)
            {
                var resourceId = ++nextResource;
                if (part != 5)
                {
                    var resource = new NguLieu { MaNguLieu = resourceId,
                        DuongDanAudio = part <= 4 ? $"audio/{resourceId}.mp3" : null,
                        DuongDanAnh = part == 1 ? $"image/{resourceId}.png" : null,
                        NoiDungNguLieu = part == 6 ? "Reading passage" : null };
                    fixture.Resources.Add(resourceId, resource);
                    if (resource.DuongDanAudio is not null) fixture.Blobs.Add(resource.DuongDanAudio);
                    if (resource.DuongDanAnh is not null) fixture.Blobs.Add(resource.DuongDanAnh);
                    if (part == 7) fixture.Documents.Add(new NguLieuTaiLieu { MaNguLieu = resourceId,
                        ThuTu = 1, LoaiTaiLieu = "TEXT", NoiDung = "Reading passage" });
                }
                for (var n = 1; n <= size; n++)
                {
                    var id = ++nextQuestion;
                    fixture.Questions.Add(id, new CauHoi { MaCauHoi = id, MaPart = part,
                        TrangThai = ExamCore.Published, PhuongAnA = "A", PhuongAnB = "B",
                        PhuongAnC = "C", PhuongAnD = part == 2 ? null : "D", PhuongAnDung = "A" });
                    fixture.Links.Add(new CauHoiDeThi { MaCauHoi = id, ThuTu = id });
                    if (part != 5) fixture.Members.Add(new NhomCauHoi { MaCauHoi = id,
                        MaNguLieu = resourceId, ThuTu = n });
                }
            }
        }
        return fixture;
    }

    [Fact]
    public void CompleteClosedExamCanBeValidatedBeforeOpening()
    {
        var fixture = CompleteExam();
        Assert.True(fixture.Validate().Valid);
    }

    [Fact]
    public void ReportsQuestionGroupMediaAndCountProblems()
    {
        var fixture = CompleteExam();
        fixture.Questions[1].TrangThai = "NHAP";
        fixture.Questions[2].PhuongAnDung = "D";
        fixture.Members.Single(m => m.MaCauHoi == 32).ThuTu = 9;
        fixture.Blobs.Remove("audio/1.mp3");
        fixture.Links.RemoveAt(199);
        var codes = fixture.Validate().Issues.Select(i => i.Code).ToHashSet();
        Assert.Contains("QUESTION_INVALID", codes);
        Assert.Contains("GROUP_INVALID", codes);
        Assert.Contains("BLOB_MISSING", codes);
        Assert.Contains("EXAM_ORDER", codes);
        Assert.Contains("PART_COUNT", codes);
    }

    [Fact]
    public void DetectsSplitGroupAndInvalidCanonicalDocument()
    {
        var fixture = CompleteExam();
        (fixture.Links[31].MaCauHoi, fixture.Links[32].MaCauHoi) =
            (fixture.Links[32].MaCauHoi, fixture.Links[31].MaCauHoi);
        fixture.Documents[0].NoiDung = "";
        var codes = fixture.Validate().Issues.Select(i => i.Code).ToHashSet();
        Assert.Contains("GROUP_SPLIT", codes);
        Assert.Contains("DOCUMENT_INVALID", codes);
    }
}
