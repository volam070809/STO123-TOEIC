using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.GoiHoc;
using STO123.Models;
using System.Security.Claims;

namespace STO123.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoiHocController : ControllerBase
{
    private readonly ToeicDbContext _context;

    public GoiHocController(ToeicDbContext context)
    {
        _context = context;
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GoiHocDto>>> GetGoiHoc()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (claim == null ||
            !int.TryParse(claim.Value, out var maNguoiDung))
        {
            return Unauthorized(new
            {
                message = "Không xác định được người dùng."
            });
        }

        var result = await _context.GoiHoc
            .Where(g => g.TrangThai == "DANG_MO")
            .Select(g => new GoiHocDto
            {
                MaGoiHoc = g.MaGoiHoc,
                TenGoiHoc = g.TenGoiHoc,
                Gia = g.Gia,
                SoNgaySuDung = g.SoNgaySuDung,
                MoTa = g.MoTa,

                DaMua = _context.DangKyGoiHoc.Any(d =>
                    d.MaNguoiDung == maNguoiDung &&
                    d.MaGoiHoc == g.MaGoiHoc
                ),

                DangSuDung = _context.DangKyGoiHoc.Any(d =>
                    d.MaNguoiDung == maNguoiDung &&
                    d.MaGoiHoc == g.MaGoiHoc &&
                    d.NgayKetThuc.HasValue &&
                    d.NgayKetThuc.Value > DateTime.Now
                )
            })
            .ToListAsync();

        return Ok(result);
    }

    [Authorize]
    [HttpGet("{id}/khoa-hoc")]
    public async Task<ActionResult<KhoaHocTrongGoiDto>> GetKhoaHocTrongGoi(int id)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (claim == null ||
            !int.TryParse(claim.Value, out var maNguoiDung))
        {
            return Unauthorized(new
            {
                message = "Không xác định được người dùng."
            });
        }

        // Kiểm tra user có đang sử dụng gói này không
        var dangSuDung = await _context.DangKyGoiHoc
            .AnyAsync(d =>
                d.MaNguoiDung == maNguoiDung &&
                d.MaGoiHoc == id &&
                d.TrangThai == "DANG_SU_DUNG" &&
                d.NgayKetThuc.HasValue &&
                d.NgayKetThuc.Value > DateTime.Now
            );

        if (!dangSuDung)
        {
            return Forbid();
        }

        // Lấy khóa học duy nhất mà gói này mở khóa
        var khoaHoc = await _context.GoiHoc
            .Where(g => g.MaGoiHoc == id)
            .SelectMany(g => g.MaKhoaHoc.Select(k => new KhoaHocTrongGoiDto
            {
                MaGoiHoc = g.MaGoiHoc,
                MaKhoaHoc = k.MaKhoaHoc,
                TenKhoaHoc = k.TenKhoaHoc,
                GiaiDoan = k.GiaiDoan,
                DiemMucTieuToiDa = k.DiemMucTieuToiDa,
                MoTa = k.MoTa,
                DuongDanAnhDaiDien = k.DuongDanAnhDaiDien
            }))
            .FirstOrDefaultAsync();

        if (khoaHoc == null)
        {
            return NotFound(new
            {
                message = "Gói học chưa được liên kết với khóa học."
            });
        }

        return Ok(khoaHoc);
    }
    [Authorize]
    [HttpGet("{id}")]
    public async Task<ActionResult<GoiHocDto>> GetGoiHocById(int id)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (claim == null ||
            !int.TryParse(claim.Value, out var maNguoiDung))
        {
            return Unauthorized(new
            {
                message = "Không xác định được người dùng."
            });
        }

        var goiHoc = await _context.GoiHoc
            .Where(g => g.MaGoiHoc == id &&
                        g.TrangThai == "DANG_MO")
            .Select(g => new GoiHocDto
            {
                MaGoiHoc = g.MaGoiHoc,
                TenGoiHoc = g.TenGoiHoc,
                Gia = g.Gia,
                SoNgaySuDung = g.SoNgaySuDung,
                MoTa = g.MoTa,

                DaMua = _context.DangKyGoiHoc.Any(d =>
                    d.MaNguoiDung == maNguoiDung &&
                    d.MaGoiHoc == g.MaGoiHoc
                ),

                DangSuDung = _context.DangKyGoiHoc.Any(d =>
                    d.MaNguoiDung == maNguoiDung &&
                    d.MaGoiHoc == g.MaGoiHoc &&
                    d.TrangThai == "DANG_SU_DUNG" &&
                    d.NgayKetThuc.HasValue &&
                    d.NgayKetThuc.Value > DateTime.Now
                )
            })
            .FirstOrDefaultAsync();

        if (goiHoc == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy gói học."
            });
        }

        return Ok(goiHoc);
    }
}