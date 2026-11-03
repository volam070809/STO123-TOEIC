using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace STO123.Models;

[Table("AiTinNhan", Schema = "dbo")]
[Index(nameof(MaHoiThoai), nameof(ThuTu), Name = "UX_AiTinNhan_HoiThoai_ThuTu", IsUnique = true)]
[Index(nameof(MaHoiThoai), nameof(MaYeuCau), Name = "UX_AiTinNhan_HoiThoai_YeuCau", IsUnique = true)]
public sealed class AiTinNhan
{
    [Key]
    public long MaTinNhan { get; set; }
    public long MaHoiThoai { get; set; }
    public int ThuTu { get; set; }
    [MaxLength(8)]
    [Unicode(false)]
    public string VaiTro { get; set; } = "USER";
    public string NoiDung { get; set; } = "";
    public Guid? MaYeuCau { get; set; }
    [Column(TypeName = "datetime2(3)")]
    public DateTime TaoLuc { get; set; }
    [ForeignKey(nameof(MaHoiThoai))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AiHoiThoai MaHoiThoaiNavigation { get; set; } = null!;
}
