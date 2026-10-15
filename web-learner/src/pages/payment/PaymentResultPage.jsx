import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { usePaymentStatus } from "../../hooks/usePaymentStatus";
import { packageApi } from "../../services/packageApi";
import { placementApi } from "../../services/examApi";
import { paymentIdFromSearch } from "../../services/paymentFlow";
import "../../styles/payment.css";

export default function PaymentResultPage() {
  const { search } = useLocation();
  const paymentId = paymentIdFromSearch(search);
  const { token } = useAuth();
  const { view, refresh } = usePaymentStatus(paymentId, token);
  const [access, setAccess] = useState({ token: null, packages: null, courses: null });

  useEffect(() => {
    if (view.kind !== "success" || !token) return;
    let live = true;
    Promise.all([packageApi.list(token), placementApi.courses(token)]).then(([packages, courses]) => {
      if (live) setAccess({ token, packages, courses });
    }).catch(() => { if (live) setAccess({ token, packages: null, courses: null }); });
    return () => { live = false; };
  }, [view.kind, token]);

  const kind = paymentId ? view.kind : "not-found";
  const activePackages = access.token === token ? access.packages?.filter(item => item.isActive) : null;
  const activeCourses = access.token === token ? access.courses?.filter(item => item.accessStatus === "ACTIVE") : null;

  return <SiteLayout><main className="site-container payment-page">
    <div className="payment-card"><span className="payment-eyebrow">THANH TOÁN VNPay</span>
      <h1>Trạng thái giao dịch</h1>
      {paymentId && <p className="payment-reference">Mã giao dịch STO123: <strong>#{paymentId}</strong></p>}
      {kind === "loading" && <p role="status">Đang kiểm tra trạng thái thanh toán…</p>}
      {kind === "pending" && <div className="payment-message payment-pending" role="status">
        <h2>Đang chờ xác minh thanh toán</h2>
        <p>Hệ thống chưa xác nhận giao dịch. Bạn có thể quay lại trang này sau; mã giao dịch vẫn nằm trong đường dẫn.</p>
        {!view.polling && <p>Đã tạm dừng kiểm tra tự động. Trạng thái thanh toán chưa bị đổi.</p>}
      </div>}
      {kind === "success" && <div className="payment-message payment-success" role="status">
        <h2>Thanh toán đã được xác nhận</h2><p>Gói học đã được kích hoạt theo xác nhận của hệ thống.</p>
        {activePackages && <p>Gói đang sử dụng: {activePackages.length ?
          activePackages.map(item => item.name).join(", ") : "Đang cập nhật quyền học…"}</p>}
        {activeCourses && <p>Khóa học có quyền truy cập: {activeCourses.length}</p>}
      </div>}
      {kind === "reconciliation" && <div className="payment-message payment-pending" role="status">
        <h2>Thanh toán đã được xác nhận, quyền học cần đối soát</h2>
        <p>Giao dịch đã được ghi nhận. Hệ thống chưa kích hoạt thêm gói học vì có đăng ký đang sử dụng. Vui lòng liên hệ hỗ trợ và cung cấp mã giao dịch.</p>
      </div>}
      {kind === "failed" && <div className="payment-message payment-failed" role="status">
        <h2>Thanh toán không thành công</h2><p>Giao dịch đã được hệ thống xác nhận thất bại.</p>
      </div>}
      {kind === "unauthorized" && <p role="alert" className="payment-alert">Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại để xem giao dịch của bạn.</p>}
      {kind === "not-found" && <p role="alert" className="payment-alert">Không tìm thấy giao dịch này trong tài khoản của bạn.</p>}
      {(kind === "network-error" || kind === "error") && <p role="alert" className="payment-alert">Chưa thể tải trạng thái giao dịch. Vui lòng thử lại.</p>}
      {paymentId && (kind === "pending" || kind === "network-error" || kind === "error") &&
        <button type="button" className="primary-button payment-refresh" onClick={refresh}>Kiểm tra lại</button>}
      <div className="payment-links"><Link to="/payment/history">Lịch sử thanh toán</Link><Link to="/packages">Xem gói học</Link><Link to="/courses">Xem khóa học</Link></div>
    </div>
  </main></SiteLayout>;
}
