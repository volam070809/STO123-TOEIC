using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace STO123.Models;

[Table("AiHoiThoai", Schema = "dbo")]
[Index(nameof(MaHocVien), nameof(CapNhatLuc), nameof(MaHoiThoai),
    Name = "IX_AiHoiThoai_HocVien_CapNhat", IsDescending = [false, true, true])]
public sealed class AiHoiThoai
{
    [Key]
    public long MaHoiThoai { get; set; }
    public int MaHocVien { get; set; }
    [MaxLength(120)]
    public string TieuDe { get; set; } = "Cuộc trò chuyện mới";
    [Column(TypeName = "datetime2(3)")]
    public DateTime TaoLuc { get; set; }
    [Column(TypeName = "datetime2(3)")]
    public DateTime CapNhatLuc { get; set; }
    [ForeignKey(nameof(MaHocVien))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public NguoiDung MaHocVienNavigation { get; set; } = null!;
    public ICollection<AiTinNhan> AiTinNhan { get; set; } = new List<AiTinNhan>();
}
