const PHONE_PATTERN = /^\+?[0-9]{9,15}$/;

export function profileDraft(user) {
  return { hoTen: user?.hoTen ?? "", soDienThoai: user?.soDienThoai ?? "" };
}

export function prepareProfileUpdate(user, draft) {
  const hoTen = String(draft.hoTen ?? "").trim();
  const soDienThoai = String(draft.soDienThoai ?? "").trim() || null;
  const payload = { hoTen, soDienThoai };
  let error = "";
  if (!hoTen || hoTen.length > 64) error = "Họ tên phải có từ 1 đến 64 ký tự.";
  else if (soDienThoai && !PHONE_PATTERN.test(soDienThoai))
    error = "Số điện thoại phải có 9–15 chữ số, có thể bắt đầu bằng +.";
  return {
    payload,
    error,
    changed: hoTen !== user.hoTen || soDienThoai !== (user.soDienThoai || null),
  };
}

export function applyProfileUpdate(current, details) {
  return current?.maNguoiDung === details.maNguoiDung ? {
    ...current, hoTen: details.hoTen, soDienThoai: details.soDienThoai,
  } : current;
}
