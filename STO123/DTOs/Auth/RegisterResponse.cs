namespace STO123.DTOs.Auth;

public sealed record RegisterResponse(
    int MaNguoiDung,
    string HoTen,
    string Email,
    string VaiTro,
    string TrangThai);
