export function authError(error, fallback = "Đã xảy ra lỗi. Vui lòng thử lại.") {
  if (error?.network) return error.message;
  if (error?.status === 500) return "Đã xảy ra lỗi máy chủ. Vui lòng thử lại.";
  if (error?.message === "Incorrect current password.") return "Mật khẩu hiện tại không đúng.";
  if (error?.message === "invalid or expired OTP") return "Mã OTP không đúng hoặc đã hết hạn.";
  if (error?.message === "Account not found.") return "Không tìm thấy tài khoản này.";
  if (error?.message === "Email is already registered.") return "Email này đã được sử dụng.";
  const validation = error?.data?.errors;
  if (validation && typeof validation === "object") {
    const fields = Object.values(validation).flat().filter(Boolean).map(translateError).filter(Boolean);
    if (fields.length) return fields.join(" ");
    return "Thông tin chưa hợp lệ. Vui lòng kiểm tra lại.";
  }
  return translateError(error?.data?.message) || fallback;
}
function translateError(message) {
  if (!message || /^HTTP \d+$/.test(message)) return "";
  const known = {
    "Name and email are required.": "Vui lòng nhập họ tên và email.",
    "Name must be at most 64 characters.": "Họ tên tối đa 64 ký tự.",
    "A valid email address is required.": "Vui lòng nhập email hợp lệ.",
    "Phone number must be at most 16 characters.": "Số điện thoại tối đa 16 ký tự.",
    "Password must be at least 8 characters long.": "Mật khẩu cần ít nhất 8 ký tự.",
    "Password must contain at least one letter.": "Mật khẩu cần ít nhất một chữ cái.",
    "Password must contain at least one digit.": "Mật khẩu cần ít nhất một chữ số.",
    "The Email field is required.": "Vui lòng nhập email.",
    "The Password field is required.": "Vui lòng nhập mật khẩu.",
    "The Otp field is required.": "Vui lòng nhập mã OTP.",
    "The NewPassword field is required.": "Vui lòng nhập mật khẩu mới.",
    "The CurrentPassword field is required.": "Vui lòng nhập mật khẩu hiện tại."
  };
  return known[message] || "";
}


