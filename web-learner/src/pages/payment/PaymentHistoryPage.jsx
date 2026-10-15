import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { paymentApi } from "../../services/paymentApi";
import { paymentState } from "../../services/paymentFlow";
import "../../styles/payment.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });
const date = new Intl.DateTimeFormat("vi-VN", { dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh" });
const labels = {
  pending: "Đang chờ xác minh", success: "Thành công", failed: "Thất bại",
  reconciliation: "Cần đối soát quyền học", error: "Cần kiểm tra",
};

export default function PaymentHistoryPage() {
  const { token } = useAuth();
  const [view, setView] = useState({ token: null, rows: null, error: false });
  useEffect(() => {
    if (!token) return;
    let live = true;
    paymentApi.history(token).then(rows => {
      if (live) setView({ token, rows, error: false });
    }).catch(() => { if (live) setView({ token, rows: null, error: true }); });
    return () => { live = false; };
  }, [token]);
  const current = view.token === token ? view : { rows: null, error: false };

  return <SiteLayout><main className="site-container payment-page">
    <div className="payment-card"><h1>Lịch sử thanh toán</h1>
      <p>Trạng thái được lấy từ hệ thống xác nhận thanh toán.</p>
      {current.error && <p role="alert">Không thể tải lịch sử thanh toán. Vui lòng thử lại.</p>}
      {!current.rows && !current.error && <p role="status">Đang tải lịch sử…</p>}
      {current.rows?.length === 0 && <p>Bạn chưa có giao dịch thanh toán.</p>}
      {current.rows?.length > 0 && <div className="payment-history-scroll"><table className="payment-history-table">
        <thead><tr><th>Giao dịch</th><th>Gói học</th><th>Số tiền</th><th>Ngày tạo</th><th>Trạng thái</th></tr></thead>
        <tbody>{current.rows.map(row => <tr key={row.maGiaoDich}>
          <td><Link to={`/payment/result?paymentId=${row.maGiaoDich}`}>#{row.maGiaoDich}</Link></td>
          <td>{row.tenGoiHoc}</td><td>{money.format(row.soTien)}</td>
          <td>{date.format(new Date(`${row.ngayTao}+07:00`))}</td>
          <td>{labels[paymentState(row.trangThai, row.trangThaiDangKy)]}</td>
        </tr>)}</tbody>
      </table></div>}
    </div>
  </main></SiteLayout>;
}
