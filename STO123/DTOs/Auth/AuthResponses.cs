namespace STO123.DTOs.Auth;

public sealed record MessageResponse(string Message);

public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc);

public sealed record MeResponse(
    int MaNguoiDung,
    string HoTen,
    string Email,
    string? SoDienThoai,
    string? AnhDaiDien,
    string VaiTro,
    string TrangThai);
