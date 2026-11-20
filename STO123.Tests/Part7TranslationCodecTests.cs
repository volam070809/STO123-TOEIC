using System.Text.Json;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class Part7TranslationCodecTests
{
    [Theory]
    [InlineData("LETTER")]
    [InlineData("ARTICLE")]
    [InlineData("NOTICE")]
    [InlineData("POLICY")]
    [InlineData("INVOICE")]
    [InlineData("WEB")]
    [InlineData("AD")]
    [InlineData("SCHEDULE")]
    [InlineData("SURVEY")]
    public void NewPartSevenLayoutsValidateAndSurviveSnapshotRoundTrip(string type)
    {
        const string source = """{"version":1,"title":"Notice","subtitle":"Office","footer":"Signed","metadata":[{"label":"Date","value":"Monday"}],"paragraphs":["First paragraph"],"items":["Item one"],"headers":["Time","Activity"],"rows":[["09:00","Meeting"]]}""";
        var original = new NguLieuTaiLieu { LoaiTaiLieu = type, NoiDung = source,
            DuongDanAnh = "part7/chart.png", ThuTu = 1 };
        Assert.True(ExamDocumentCodec.IsValidSource(original));
        var encoded = ExamDocumentCodec.Encode([new ExamDocumentSnapshot(1, type, source, original.DuongDanAnh)]);
        var restored = Assert.Single(ExamDocumentCodec.Decode(encoded));
        Assert.Equal(type, restored.Type);
        Assert.Equal(original.DuongDanAnh, restored.ImagePath);
        Assert.Equal("Notice", ((JsonElement)ExamDocumentCodec.ToContent(restored)!).GetProperty("title").GetString());

        var translation = Part7TranslationCodec.Prefix + JsonSerializer.Serialize(new {
            version = 1, kind = "PART7_TRANSLATIONS", documents = new[] { new { type, content = new {
                version = 1, title = "Thông báo", subtitle = "Văn phòng", footer = "Ký tên",
                metadata = new[] { new { label = "Ngày", value = "Thứ hai" } },
                paragraphs = new[] { "Đoạn đầu" }, items = new[] { "Mục một" },
                headers = new[] { "Giờ", "Hoạt động" }, rows = new[] { new[] { "09:00", "Họp" } }
            } } }
        });
        Assert.True(Part7TranslationCodec.IsValid(translation, [original]));
        Assert.False(Part7TranslationCodec.IsValid(translation.Replace("\"paragraphs\":[", "\"paragraphs\":[\"Extra\","), [original]));
    }

    [Fact]
    public void StructuredTranslationsRequireOriginalDocumentShapes()
    {
        NguLieuTaiLieu[] originals = [
            new() { LoaiTaiLieu = "TABLE", NoiDung = """{"version":1,"headers":["A","B"],"rows":[["x","y"]]}""" },
            new() { LoaiTaiLieu = "EMAIL", NoiDung = """{"version":1,"body":"Message"}""" }
        ];
        var valid = Part7TranslationCodec.Prefix + JsonSerializer.Serialize(new {
            version = 1, kind = "PART7_TRANSLATIONS", legacyText = "Bản dịch cũ", documents = new object[] {
                new { type = "TABLE", content = new { version = 1, headers = new[] { "Tên", "Giá" },
                    rows = new[] { new[] { "Trà", "2 đô" } } } },
                new { type = "EMAIL", content = new { version = 1, from = "A", to = "B",
                    date = "Hôm nay", subject = "Chủ đề", body = "Xin chào" } }
            }
        });
        Assert.True(Part7TranslationCodec.IsValid(valid, originals));
        Assert.True(Part7TranslationCodec.IsValid("Bản dịch văn bản cũ\nDòng hai", originals));
        Assert.False(Part7TranslationCodec.IsValid(valid.Replace("\"headers\":[", "\"headers\":[\"Extra\","), originals));
        Assert.False(Part7TranslationCodec.IsValid(valid.Replace("\"TABLE\"", "\"CHAT\""), originals));
        Assert.False(Part7TranslationCodec.IsValid(Part7TranslationCodec.Prefix + "{broken", originals));
    }

    [Fact]
    public void MetadataOnlyEmailTranslationCanBeSavedIncrementally()
    {
        NguLieuTaiLieu[] originals = [new() { LoaiTaiLieu = "EMAIL",
            NoiDung = """{"version":1,"from":"Teacher","to":"Class","date":"Monday","subject":"News","body":"Hello"}""" }];
        var value = Part7TranslationCodec.Prefix + """{"version":1,"kind":"PART7_TRANSLATIONS","documents":[{"type":"EMAIL","content":{"version":1,"from":"Giáo viên","to":"Class","date":"Monday","subject":"","body":""}}]}""";
        Assert.True(Part7TranslationCodec.IsValid(value, originals));
    }
}
