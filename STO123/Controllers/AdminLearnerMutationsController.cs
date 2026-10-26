using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STO123.DTOs.Admin;
using STO123.DTOs.Auth;
using STO123.Models;
using STO123.Services.Auth;

namespace STO123.Controllers;

[ApiController]
[Authorize(Policy = AdminAuthorization.ManagementPolicy)]
[Route("api/admin/learners")]
public sealed partial class AdminLearnerMutationsController(
    ToeicDbContext db,
    IAuthService authService,
    AvatarStorage avatars,
    ILogger<AdminLearnerMutationsController> logger) : ControllerBase
{
    [GeneratedRegex(@"^\+?[0-9]{9,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateLearnerRequest request, CancellationToken ct)
    {
        var name = request.FullName?.Trim() ?? string.Empty;
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var phone = NormalizePhone(request.PhoneNumber);
        var password = request.Password ?? string.Empty;
        var errors = ValidateProfile(name, phone);
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            errors["email"] = "Email không hợp lệ.";
        if (password.Length > 128)
            errors["password"] = "Mật khẩu tối đa 128 ký tự.";
        else if (PasswordRules.Validate(password) is not null)
            errors["password"] = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ và số.";
        if (errors.Count > 0) return ValidationProblem(errors);

        var result = await authService.RegisterAsync(new RegisterRequest
        {
            HoTen = name,
            Email = email,
            SoDienThoai = phone,
            Password = password
        }, ct);

        if (result.Error is not null)
        {
            if (result.StatusCode == StatusCodes.Status409Conflict)
                return Conflict(new { code = "EMAIL_ALREADY_EXISTS", message = "Email đã được sử dụng." });
            if (result.Error.StartsWith("Account created,", StringComparison.Ordinal))
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    code = "VERIFICATION_EMAIL_FAILED",
                    message = "Tài khoản đã được tạo nhưng chưa gửi được email xác thực. Hãy kiểm tra danh sách trước khi thử lại."
                });
            return StatusCode(result.StatusCode, new { code = "CREATE_LEARNER_FAILED", message = result.Error });
        }

        var learner = result.Value!;
        return CreatedAtAction(nameof(AdminLearnersController.Detail), "AdminLearners",
            new { learnerId = learner.MaNguoiDung },
            new AdminLearnerAccountDto(learner.MaNguoiDung, learner.HoTen, learner.Email,
                phone, learner.TrangThai));
    }

    [HttpPut("{learnerId:int}")]
    public async Task<IActionResult> Update(int learnerId, [FromBody] AdminUpdateLearnerRequest request,
        CancellationToken ct)
    {
        var name = request.FullName?.Trim() ?? string.Empty;
        var phone = NormalizePhone(request.PhoneNumber);
        var errors = ValidateProfile(name, phone);
        if (errors.Count > 0) return ValidationProblem(errors);

        var learner = await db.NguoiDung.FirstOrDefaultAsync(user =>
            user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN", ct);
        if (learner is null) return LearnerNotFound();

        if (learner.HoTen != name || learner.SoDienThoai != phone)
        {
            learner.HoTen = name;
            learner.SoDienThoai = phone!;
            await db.SaveChangesAsync(ct);
        }

        return Ok(Account(learner));
    }

    [HttpPatch("{learnerId:int}/status")]
    public async Task<IActionResult> ChangeStatus(int learnerId,
        [FromBody] AdminChangeLearnerStatusRequest request, CancellationToken ct)
    {
        var target = request.Status?.Trim().ToUpperInvariant();
        if (target is not ("HOAT_DONG" or "BI_KHOA"))
            return BadRequest(new { code = "INVALID_STATUS", message = "Chỉ có thể khóa hoặc mở khóa tài khoản." });

        var learner = await db.NguoiDung.FirstOrDefaultAsync(user =>
            user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN", ct);
        if (learner is null) return LearnerNotFound();
        if (learner.TrangThai == "CHO_XAC_THUC")
            return Conflict(new
            {
                code = "PENDING_VERIFICATION",
                message = "Tài khoản đang chờ xác thực email nên không thể khóa hoặc mở khóa."
            });
        if (learner.TrangThai == target) return Ok(Account(learner));

        learner.TrangThai = target;
        await db.SaveChangesAsync(ct);
        return Ok(Account(learner));
    }

    [HttpDelete("{learnerId:int}")]
    public async Task<IActionResult> Delete(int learnerId, CancellationToken ct)
    {
        var learner = await db.NguoiDung.FirstOrDefaultAsync(user =>
            user.MaNguoiDung == learnerId && user.VaiTro == "HOC_VIEN", ct);
        if (learner is null) return LearnerNotFound();

        var blockers = await DeleteBlockers(learnerId, ct);
        if (blockers.Count > 0)
            return Conflict(new AdminDeleteLearnerBlockedDto(
                "LEARNER_HAS_HISTORY",
                "Không thể xóa học viên vì tài khoản đang có dữ liệu lịch sử cần được bảo toàn.",
                blockers));

        var avatarReference = learner.AnhDaiDien;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.XacThucOTP.RemoveRange(await db.XacThucOTP.Where(item => item.MaNguoiDung == learnerId).ToListAsync(ct));
            var credential = await db.XacThucDangNhap.FirstOrDefaultAsync(item => item.MaNguoiDung == learnerId, ct);
            if (credential is not null) db.XacThucDangNhap.Remove(credential);
            db.NguoiDung.Remove(learner);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Conflict(new AdminDeleteLearnerBlockedDto(
                "LEARNER_HAS_HISTORY",
                "Không thể xóa học viên vì vẫn còn dữ liệu liên quan.",
                ["Dữ liệu liên quan vừa thay đổi; hãy tải lại hồ sơ và thử lại."]));
        }

        if (AvatarStorage.IsCustomReference(learnerId, avatarReference))
        {
            try { await avatars.DeleteAsync(avatarReference!, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning("Could not remove the deleted learner's avatar ({ErrorType}).", error.GetType().Name);
            }
        }
        return NoContent();
    }

    private async Task<List<string>> DeleteBlockers(int learnerId, CancellationToken ct)
    {
        var blockers = new List<string>();
        if (await db.DangKyGoiHoc.AsNoTracking().AnyAsync(item => item.MaNguoiDung == learnerId, ct))
            blockers.Add("Đăng ký gói hoặc khóa học");
        if (await db.KetQuaLamBai.AsNoTracking().AnyAsync(item => item.MaHocVien == learnerId, ct))
            blockers.Add("Kết quả thi và bài luyện");
        if (await db.TienDoBuocLoTrinh.AsNoTracking().AnyAsync(item => item.MaNguoiDung == learnerId, ct))
            blockers.Add("Tiến độ lộ trình");
        if (await db.TienDoTuVung.AsNoTracking().AnyAsync(item => item.MaNguoiDung == learnerId, ct))
            blockers.Add("Tiến độ từ vựng");
        if (await db.LanLuyenTuVung.AsNoTracking().AnyAsync(item => item.MaNguoiDung == learnerId, ct))
            blockers.Add("Lịch sử luyện từ vựng");
        if (await db.DeThi.AsNoTracking().AnyAsync(item =>
            item.MaNguoiSoan == learnerId || item.MaNguoiDuocGiao == learnerId, ct))
            blockers.Add("Đề thi được soạn hoặc được giao");
        if (await db.KhoaHoc.AsNoTracking().AnyAsync(item => item.MaAdminNoiDung == learnerId, ct) ||
            await db.BaiGiang.AsNoTracking().AnyAsync(item => item.MaAdminNoiDung == learnerId, ct))
            blockers.Add("Nội dung khóa học hoặc bài giảng");
        return blockers;
    }

    private IActionResult ValidationProblem(Dictionary<string, string> errors) =>
        BadRequest(new { code = "VALIDATION_FAILED", message = "Thông tin học viên chưa hợp lệ.", errors });

    private static Dictionary<string, string> ValidateProfile(string name, string? phone)
    {
        var errors = new Dictionary<string, string>();
        if (name.Length is < 1 or > 64) errors["fullName"] = "Họ tên phải có từ 1 đến 64 ký tự.";
        if (phone is not null && (phone.Length > 16 || !PhonePattern().IsMatch(phone)))
            errors["phoneNumber"] = "Số điện thoại phải có 9–15 chữ số và có thể bắt đầu bằng +.";
        return errors;
    }

    private static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static AdminLearnerAccountDto Account(NguoiDung learner) =>
        new(learner.MaNguoiDung, learner.HoTen, learner.Email, learner.SoDienThoai,
            learner.TrangThai);

    private IActionResult LearnerNotFound() =>
        NotFound(new { code = "LEARNER_NOT_FOUND", message = "Không tìm thấy học viên." });
}
