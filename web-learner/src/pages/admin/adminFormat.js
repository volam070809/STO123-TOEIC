export const learnerStatuses = {
  HOAT_DONG: "Hoạt động",
  CHO_XAC_THUC: "Chờ xác thực",
  BI_KHOA: "Bị khóa",
};

export const courseStatuses = {
  DANG_MO: "Đang mở",
  DA_DONG: "Đã đóng",
};

const statusLabels = {
  ...learnerStatuses,
  ...courseStatuses,
  CHO_THANH_TOAN: "Chờ thanh toán",
  DANG_SU_DUNG: "Đang sử dụng",
  HET_HAN: "Hết hạn",
  CHO_XU_LY: "Chờ xử lý",
  THANH_CONG: "Thành công",
  THAT_BAI: "Thất bại",
  DANG_LAM: "Đang làm",
  DA_NOP: "Đã nộp",
  BO_DO: "Bỏ dở",
  HET_GIO: "Hết giờ",
  MOCK: "Thi thử",
  PLACEMENT: "Xếp lớp",
  PRACTICE: "Luyện tập",
};

export function statusLabel(status) {
  return statusLabels[status] ?? status?.replaceAll("_", " ") ?? "—";
}

export function statusTone(status) {
  if (["HOAT_DONG", "DANG_MO", "DANG_SU_DUNG", "THANH_CONG", "HOAN_THANH", "DA_NOP"].includes(status)) return "success";
  if (["BI_KHOA", "DA_DONG", "THAT_BAI", "HUY"].includes(status)) return "danger";
  return "warning";
}

export function formatDate(value, includeTime = false) {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("vi-VN", includeTime ?
    { dateStyle: "short", timeStyle: "short" } : { dateStyle: "short" }).format(date);
}

export function formatMoney(value) {
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 })
    .format(Number(value) || 0);
}
