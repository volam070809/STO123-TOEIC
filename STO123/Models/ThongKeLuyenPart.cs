namespace STO123.Models;

public sealed class ThongKeLuyenPart
{
    public int MaHocVien { get; set; }
    public int MaPart { get; set; }
    public int SoLanLuyen { get; set; }
    public int TongCauDaLam { get; set; }
    public int TongCauDung { get; set; }
    public int SoCauTrongLanCuoi { get; set; }
    public int SoCauLanCuoi { get; set; }
    public int SoDungLanCuoi { get; set; }
    public int? MaKetQuaCuoi { get; set; }
    public DateTime? LanLuyenGanNhat { get; set; }
}
