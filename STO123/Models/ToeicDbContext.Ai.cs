using Microsoft.EntityFrameworkCore;

namespace STO123.Models;

public partial class ToeicDbContext
{
    public DbSet<AiHoiThoai> AiHoiThoai => Set<AiHoiThoai>();
    public DbSet<AiTinNhan> AiTinNhan => Set<AiTinNhan>();
}
