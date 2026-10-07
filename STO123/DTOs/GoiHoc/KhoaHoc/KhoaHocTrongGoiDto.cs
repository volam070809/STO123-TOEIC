namespace STO123.DTOs.GoiHoc;

public class KhoaHocTrongGoiDto
{
    public int MaGoiHoc { get; set; }
    public int MaKhoaHoc { get; set; }
    public string TenKhoaHoc { get; set; }
    public byte GiaiDoan { get; set; }
    public int DiemMucTieuToiDa { get; set; }
    public string? MoTa { get; set; }
    public string? DuongDanAnhDaiDien { get; set; }
}