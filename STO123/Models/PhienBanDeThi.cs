using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace STO123.Models;

[Index(nameof(MaDeThi), nameof(SoPhienBan), IsUnique = true)]
[Index(nameof(BlobContainer), nameof(BlobPath), IsUnique = true)]
[Index(nameof(MaDeThi), nameof(KhoaYeuCau), IsUnique = true)]
public sealed class PhienBanDeThi
{
    [Key] public int MaPhienBanDeThi { get; set; }
    public int MaDeThi { get; set; }
    public int SoPhienBan { get; set; }
    [Required, StringLength(63), Unicode(false)] public string BlobContainer { get; set; } = "";
    [Required, StringLength(512), Unicode(false)] public string BlobPath { get; set; } = "";
    [Required, StringLength(64), Unicode(false)] public string ContentHash { get; set; } = "";
    public int SchemaVersion { get; set; }
    public int SoCau { get; set; }
    public int ThoiGianLamBai { get; set; }
    [Required, StringLength(128)] public string TenDe { get; set; } = "";
    [Precision(3)] public DateTime NgayXuatBan { get; set; }
    public int? MaNguoiXuatBan { get; set; }
    public Guid KhoaYeuCau { get; set; }
    public DeThi DeThi { get; set; } = null!;
}
