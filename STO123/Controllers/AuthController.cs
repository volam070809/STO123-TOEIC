using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Auth;
using STO123.Models;

namespace STO123.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ToeicDbContext _context;
    private readonly PasswordHasher<NguoiDung> _passwordHasher = new();

    public AuthController(ToeicDbContext context)
    {
        _context = context;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var hoTen = request.HoTen.Trim();

        if (string.IsNullOrWhiteSpace(hoTen) || string.IsNullOrWhiteSpace(email))
        {
            return BadRequest("Name and email are required.");
        }

        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            return BadRequest("A valid email address is required.");
        }

        if (await _context.NguoiDung.AnyAsync(
                user => user.Email.Trim().ToLower() == email, cancellationToken))
        {
            return Conflict("Email is already registered.");
        }

        var user = new NguoiDung
        {
            HoTen = hoTen,
            Email = email,
            VaiTro = "HOC_VIEN"
        };

        var credential = new XacThucDangNhap
        {
            MaNguoiDungNavigation = user,
            LoaiXacThuc = "EMAIL",
            MatKhauMaHoa = _passwordHasher.HashPassword(user, request.Password)
        };

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _context.XacThucDangNhap.Add(credential);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            return Conflict("Email is already registered.");
        }

        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(
            user.MaNguoiDung,
            user.HoTen,
            user.Email,
            user.VaiTro,
            user.TrangThai));
    }
}
