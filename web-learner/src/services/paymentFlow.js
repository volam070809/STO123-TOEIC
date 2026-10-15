export function paymentIdFromSearch(search) {
  const params = new URLSearchParams(search);
  const values = params.getAll("paymentId");
  if (values.length !== 1 || !/^[1-9]\d{0,9}$/.test(values[0])) return null;
  const id = Number(values[0]);
  return Number.isSafeInteger(id) ? id : null;
}

export function validatedCheckoutUrl(value) {
  if (typeof value !== "string") return null;
  try {
    const url = new URL(value);
    if (url.protocol !== "https:" || url.hostname !== "sandbox.vnpayment.vn" ||
        url.port || url.username || url.password || url.hash ||
        url.pathname !== "/paymentv2/vpcpay.html" ||
        url.searchParams.getAll("vnp_TxnRef").length !== 1 ||
        url.searchParams.getAll("vnp_SecureHash").length !== 1 ||
        !/^[A-Za-z0-9]{1,100}$/.test(url.searchParams.get("vnp_TxnRef") || "") ||
        !/^[a-fA-F0-9]{128}$/.test(url.searchParams.get("vnp_SecureHash") || "")) return null;
    return value;
  } catch { return null; }
}

export function paymentState(status, enrollmentStatus) {
  if (status === "THANH_CONG" && enrollmentStatus !== "DANG_SU_DUNG" &&
      enrollmentStatus !== "HET_HAN") return "reconciliation";
  if (status === "THANH_CONG") return "success";
  if (status === "THAT_BAI") return "failed";
  if (status === "CHO_XU_LY") return "pending";
  return "error";
}

export function paymentError(error) {
  if (error?.status === 401 || error?.status === 403) return "unauthorized";
  if (error?.status === 404) return "not-found";
  return "network-error";
}

export function checkoutErrorMessage(error) {
  const messages = {
    CHECKOUT_DISABLED: "Thanh toán VNPay chưa được mở. Bạn chưa bị ghi nhận giao dịch mới.",
    PACKAGE_UNAVAILABLE: "Gói học không còn khả dụng.",
    PACKAGE_ALREADY_ACTIVE: "Bạn đang sử dụng gói học này.",
    PAYMENT_ALREADY_PENDING: "Một khóa học đã chọn có giao dịch đang chờ xác minh.",
    INVALID_COURSE_SELECTION: "Danh sách khóa học không hợp lệ. Vui lòng chọn lại.",
    COURSE_UNAVAILABLE: "Một khóa học không còn mở bán hoặc chưa có giá. Vui lòng chọn lại.",
    COURSE_ALREADY_ACTIVE: "Bạn đã có quyền học đối với một khóa đã chọn. Vui lòng chọn lại.",
    QUOTE_CHANGED: "Giá khóa học đã thay đổi. Vui lòng kiểm tra báo giá mới.",
  };
  return messages[error?.data?.code] || (error?.message === "INVALID_CHECKOUT_URL" ?
    "Liên kết thanh toán không hợp lệ. Vui lòng thử lại sau." :
    "Không thể tạo yêu cầu thanh toán. Vui lòng thử lại.");
}

export function startPaymentPolling({ load, paymentId, onView, schedule = setTimeout,
  cancel = clearTimeout, intervalMs = 4_000, maxRequests = 10 }) {
  let live = true;
  let timer;
  let requests = 0;
  onView({ kind: "loading", polling: true });

  async function check() {
    requests += 1;
    try {
      const response = await load();
      if (!live) return;
      if (response?.maGiaoDich !== paymentId) {
        onView({ kind: "network-error", polling: false });
        return;
      }
      const kind = paymentState(response.trangThai, response.trangThaiDangKy);
      const polling = kind === "pending" && requests < maxRequests;
      onView({ kind, polling });
      if (polling) timer = schedule(check, intervalMs);
    } catch (error) {
      if (live) onView({ kind: paymentError(error), polling: false });
    }
  }

  check();
  return () => { live = false; cancel(timer); };
}
