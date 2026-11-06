namespace STO123.Models;

/// <summary>One current recommendation outcome per learner and Part, without practice answer history.</summary>
public sealed class SmartLearningFollowUp
{
    public int MaHocVien { get; set; }
    public int MaPart { get; set; }
    public int? MaLuotDangLuyen { get; set; }
    public decimal? TyLeTruocDangLuyen { get; set; }
    public decimal? TyLeTruocGanNhat { get; set; }
    public decimal? TyLeSauGanNhat { get; set; }
    public int? SoCauSauGanNhat { get; set; }
    public DateTime? HoanThanhLucUtc { get; set; }
    public DateTime CapNhatLucUtc { get; set; }
}
