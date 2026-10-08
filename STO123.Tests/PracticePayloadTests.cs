using System.Reflection;
using System.Text.Json;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class PracticePayloadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ActiveListeningPayloadDoesNotExposeProtectedText(int part)
    {
        var attempt = new KetQuaLamBai { MaKetQua = 42, TrangThai = "DANG_LAM" };
        attempt.NhomLuotLam.Add(new NhomLuotLam { MaNhomLuotLam = 7, ThuTu = 1,
            NoiDungNguLieu = "SECRET_TRANSCRIPT", TaiLieuJson = "[]" });
        attempt.CauHoiLuotLam.Add(new CauHoiLuotLam { MaCauHoiLuotLam = 9, ThuTu = 1,
            NoiDung = "SECRET_STEM", PhuongAnA = "SECRET_OPTION_A", PhuongAnB = "SECRET_OPTION_B",
            PhuongAnC = "SECRET_OPTION_C", PhuongAnD = "SECRET_OPTION_D",
            PhuongAnDung = "A", GiaiThich = "SECRET_EXPLANATION" });

        var payload = typeof(PracticePartService).GetMethod("Payload", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [attempt, part, 1, "SHORT", 1, false]);
        var json = JsonSerializer.Serialize(payload);

        Assert.DoesNotContain("SECRET_TRANSCRIPT", json);
        Assert.DoesNotContain("SECRET_EXPLANATION", json);
        Assert.DoesNotContain("\"correctOption\":\"A\"", json);
        if (part <= 2)
        {
            Assert.DoesNotContain("SECRET_STEM", json);
            Assert.DoesNotContain("SECRET_OPTION_A", json);
        }
        else
        {
            Assert.Contains("SECRET_STEM", json);
            Assert.Contains("SECRET_OPTION_A", json);
        }
    }

    [Fact]
    public void ResumeRevealsOnlyQuestionsAlreadyChecked()
    {
        var attempt = new KetQuaLamBai { MaKetQua = 42, TrangThai = ExamCore.Active };
        attempt.CauHoiLuotLam.Add(new CauHoiLuotLam { MaCauHoiLuotLam = 1, ThuTu = 1,
            NoiDung = "Checked transcript", PhuongAnA = "Checked option", PhuongAnB = "B",
            PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "A",
            ChiTietKetQua = new ChiTietKetQua { DapAnChon = "A" } });
        attempt.CauHoiLuotLam.Add(new CauHoiLuotLam { MaCauHoiLuotLam = 2, ThuTu = 2,
            NoiDung = "Hidden transcript", PhuongAnA = "Hidden option", PhuongAnB = "B",
            PhuongAnC = "C", PhuongAnD = "D", PhuongAnDung = "B" });
        var payload = typeof(PracticePartService).GetMethod("Payload", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [attempt, 1, null, null, null, false]);
        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("Checked transcript", json);
        Assert.DoesNotContain("Hidden transcript", json);
        Assert.DoesNotContain("Hidden option", json);
        Assert.Contains("\"isChecked\":true", json);
        Assert.Contains("\"isChecked\":false", json);
    }
}
