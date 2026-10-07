namespace STO123.DTOs.GoiHoc;

public class GoiHocDetailDto
{
    public int MaGoiHoc { get; set; }
    public string TenGoiHoc { get; set; }
    public decimal Gia { get; set; }
    public int SoNgaySuDung { get; set; }
    public string MoTa { get; set; }
    public string TrangThai { get; set; }
    public DateTime NgayTao { get; set; }

    public bool DaMua { get; set; }
    public bool DangSuDung { get; set; }
    public DateTime? NgayKetThuc { get; set; }
}