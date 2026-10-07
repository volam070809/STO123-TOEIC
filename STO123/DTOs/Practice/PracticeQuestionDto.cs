namespace STO123.DTOs.Practice;

public sealed class PracticeQuestionDto
{
    public int MaCauHoiLuotLam { get; set; }

    public int MaPart { get; set; }

    public int ThuTu { get; set; }

    public int ThuTuTrongPart { get; set; }

    public int? MaNhomLuotLam { get; set; }

    public string NoiDung { get; set; }

    public string PhuongAnA { get; set; }

    public string PhuongAnB { get; set; }

    public string PhuongAnC { get; set; }

    public string PhuongAnD { get; set; }

    // Thông tin NguLieu cho Part 3, 4...
    public string NoiDungNguLieu { get; set; }

    public string NoiDungDich { get; set; }

    public string DuongDanAudio { get; set; }

    public string DuongDanAnh { get; set; }
}