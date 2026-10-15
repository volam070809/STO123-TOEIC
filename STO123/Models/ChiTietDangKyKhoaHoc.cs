using System.ComponentModel.DataAnnotations.Schema;

namespace STO123.Models;

public sealed class ChiTietDangKyKhoaHoc
{
    public int MaDangKy { get; set; }
    public int MaKhoaHoc { get; set; }
    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGia { get; set; }
    public DangKyGoiHoc MaDangKyNavigation { get; set; } = null!;
    public KhoaHoc MaKhoaHocNavigation { get; set; } = null!;
}
