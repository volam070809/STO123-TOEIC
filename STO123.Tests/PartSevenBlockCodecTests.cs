using System.Text.Json;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class PartSevenBlockCodecTests
{
    private const string Source = """
        {"version":2,"blocks":[
          {"id":"heading","kind":"HEADING","text":"Summer schedule"},
          {"id":"body","kind":"PARAGRAPH","text":"Please see the schedule below."},
          {"id":"table","kind":"TABLE","headers":["Time","Activity"],"rows":[["09:00","Meeting"]]},
          {"id":"photo1","kind":"IMAGE","path":"part7/first.png","caption":"Floor plan","align":"center","width":80},
          {"id":"photo2","kind":"IMAGE","path":"part7/second.webp","caption":"Map","align":"right","width":50}
        ]}
        """;

    [Theory]
    [InlineData("TEXT")][InlineData("EMAIL")][InlineData("LETTER")][InlineData("INVOICE")]
    [InlineData("RECEIPT")][InlineData("ARTICLE")][InlineData("WEB")][InlineData("NOTICE")]
    [InlineData("AD")][InlineData("POLICY")][InlineData("TABLE")][InlineData("SCHEDULE")]
    [InlineData("FORM")][InlineData("SURVEY")][InlineData("CHAT")][InlineData("MEMO")]
    [InlineData("REPORT")][InlineData("ORDER")][InlineData("REVIEW")][InlineData("MENU")]
    [InlineData("AGENDA")][InlineData("BROCHURE")][InlineData("POSTER")]
    [InlineData("CATALOG")][InlineData("IMAGE")]
    public void EveryStudioTemplateAcceptsVersionTwoDocuments(string type)
    {
        Assert.True(ExamDocumentCodec.IsValidSource(new NguLieuTaiLieu {
            LoaiTaiLieu = type, NoiDung = Source, ThuTu = 1 }));
    }

    [Fact]
    public void MultipleImagesSurviveSnapshotAndGetAuthenticatedUrls()
    {
        var document = new ExamDocumentSnapshot(1, "SCHEDULE", Source, null);
        var restored = Assert.Single(ExamDocumentCodec.Decode(ExamDocumentCodec.Encode([document])));
        Assert.Equal(["part7/first.png", "part7/second.webp"], ExamDocumentCodec.ImagePaths(restored));
        var content = JsonSerializer.Serialize(ExamDocumentCodec.ToClientContent(restored, 27, 9));
        Assert.Contains("/api/attempts/27/groups/9/documents/1/images/photo1", content);
        Assert.Contains("/api/attempts/27/groups/9/documents/1/images/photo2", content);
        Assert.DoesNotContain("part7/first.png", content);
        Assert.Equal("part7/second.webp", PartSevenBlockCodec.ImagePath(restored.Content, "photo2"));
        Assert.Null(PartSevenBlockCodec.ImagePath(restored.Content, "unknown"));
    }

    [Fact]
    public void ArchiveRewritesEveryBlockImagePath()
    {
        var archived = PartSevenBlockCodec.ArchiveImages(Source,
            new Dictionary<string, string> { ["part7/first.png"] = "archive/first.png",
                ["part7/second.webp"] = "archive/second.webp" });
        Assert.True(PartSevenBlockCodec.IsValid("SCHEDULE", archived));
        Assert.Equal(["archive/first.png", "archive/second.webp"], PartSevenBlockCodec.ImagePaths(archived));
        Assert.Contains("Summer schedule", archived);
    }

    [Theory]
    [InlineData("../secret.png")][InlineData("/absolute.png")][InlineData("part7\\bad.png")]
    [InlineData("part7/photo.svg")]
    public void UnsafeImagePathsAreRejected(string path)
    {
        Assert.False(PartSevenBlockCodec.IsValid("IMAGE", Source.Replace("part7/first.png", path)));
    }

    [Fact]
    public void TranslationMustMatchBlockIdsAndTableShape()
    {
        var original = new NguLieuTaiLieu { LoaiTaiLieu = "SCHEDULE", NoiDung = Source };
        var translation = Part7TranslationCodec.PrefixV2 + """
            {"version":2,"kind":"PART7_TRANSLATIONS","legacyText":null,"documents":[
              {"type":"SCHEDULE","blocks":[
                {"id":"heading","kind":"HEADING","text":"Lịch mùa hè"},
                {"id":"body","kind":"PARAGRAPH","text":"Xem lịch bên dưới."},
                {"id":"table","kind":"TABLE","headers":["Giờ","Hoạt động"],"rows":[["09:00","Họp"]]},
                {"id":"photo1","kind":"IMAGE","caption":"Sơ đồ"},
                {"id":"photo2","kind":"IMAGE","caption":"Bản đồ"}
              ]}
            ]}
            """;
        Assert.True(Part7TranslationCodec.IsValid(translation, [original]));
        Assert.False(Part7TranslationCodec.IsValid(translation.Replace("\"photo2\"", "\"other\""), [original]));
        Assert.False(Part7TranslationCodec.IsValid(translation.Replace("\"Giờ\",\"Hoạt động\"", "\"Giờ\""), [original]));
        Assert.False(Part7TranslationCodec.IsValid(Part7TranslationCodec.Prefix + "{}", [original]));
    }
}
