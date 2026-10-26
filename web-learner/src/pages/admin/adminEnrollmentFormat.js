export const enrollmentStatuses = {
  CHO_THANH_TOAN: "Chờ thanh toán",
  DANG_SU_DUNG: "Đang sử dụng",
  HET_HAN: "Hết hạn",
};

export const paymentStateLabels = {
  SUCCESS: "Đã thanh toán",
  PENDING: "Đang chờ xử lý",
  FAILED: "Thanh toán thất bại",
  NONE: "Chưa có giao dịch",
};

export const accessStateLabels = {
  ACTIVE: "Đang có quyền truy cập",
  EXPIRED: "Quyền truy cập đã hết hạn",
  PAYMENT_PENDING: "Chờ thanh toán",
  PAYMENT_FAILED: "Thanh toán không thành công",
  NOT_ACTIVE: "Chưa có quyền truy cập",
};

export function purchaseTypeLabel(value) {
  return value === "PACKAGE" ? "Gói học" : value === "DIRECT_COURSES" ? "Mua khóa học trực tiếp" : "—";
}

export function paymentStateLabel(value) { return paymentStateLabels[value] ?? value ?? "—"; }
export function accessStateLabel(value) { return accessStateLabels[value] ?? value ?? "—"; }
export function paymentTone(value) { return value === "SUCCESS" ? "success" : value === "FAILED" ? "danger" : "warning"; }
export function accessTone(value) { return value === "ACTIVE" ? "success" : ["EXPIRED", "PAYMENT_FAILED"].includes(value) ? "danger" : "warning"; }
