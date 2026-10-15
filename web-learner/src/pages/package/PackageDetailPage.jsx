import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { packageApi } from "../../services/packageApi";
import { paymentApi } from "../../services/paymentApi";
import { checkoutErrorMessage, validatedCheckoutUrl } from "../../services/paymentFlow";
import "../../styles/package.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

export default function PackageDetailPage() {
  const { id } = useParams();
  const { token } = useAuth();
  const busyRef = useRef(false);
  const [view, setView] = useState({ token: null, id: null, item: null, error: "" });
  const [checkout, setCheckout] = useState({ busy: false, message: "" });
  const packageId = /^[1-9]\d{0,9}$/.test(id || "") ? Number(id) : null;

  useEffect(() => {
    if (!packageId) return;
    let live = true;
    packageApi.detail(packageId, token).then(item => {
      if (live) setView({ token, id, item, error: "" });
    }).catch(error => {
      if (live) setView({ token, id, item: null, error: error.status === 404 ?
        "Gói học không còn khả dụng." : "Không thể tải thông tin gói học." });
    });
    return () => { live = false; };
  }, [id, packageId, token]);

  async function createPayment() {
    if (busyRef.current || !packageId || !token) return;
    busyRef.current = true;
    setCheckout({ busy: true, message: "" });
    try {
      const response = await paymentApi.create(packageId, token);
      const url = validatedCheckoutUrl(response?.checkoutUrl);
      if (!Number.isSafeInteger(response?.maGiaoDich) || response.maGiaoDich <= 0 || !url)
        throw new Error("INVALID_CHECKOUT_URL");
      window.location.assign(url);
    } catch (error) {
      setCheckout({ busy: false, message: checkoutErrorMessage(error) });
      busyRef.current = false;
    }
  }

  const current = view.token === token && view.id === id ? view : { item: null, error: "" };
  const item = current.item;
  return <SiteLayout><main className="site-container package-page">
    <Link className="package-back" to="/packages">← Tất cả gói học</Link>
    {!packageId && <p role="alert" className="package-alert">Mã gói học không hợp lệ.</p>}
    {current.error && <p role="alert" className="package-alert">{current.error}</p>}
    {packageId && !item && !current.error && <p role="status">Đang tải gói học…</p>}
    {item && <div className="package-detail">
      <section className="package-detail-main"><span className="package-eyebrow">GÓI HỌC STO123</span>
        <h1>{item.name}</h1><p>{item.description || "Thông tin gói học đang được cập nhật."}</p>
        <h2>Khóa học trong gói</h2>
        {item.courses.length ? <ul className="package-courses">{item.courses.map(course =>
          <li key={course.courseId}><span>{course.name}</span><Link to={`/courses/${course.courseId}`}>Xem đề cương</Link></li>)}</ul>
          : <p>Chưa có khóa học đang mở trong gói này.</p>}
      </section>
      <aside className="package-purchase"><h2>Thông tin gói</h2>
        <p className="package-price">{money.format(item.price)}</p>
        <p>Thời hạn sử dụng: <strong>{item.validDays} ngày</strong></p>
        {item.isPurchased && !item.isActive &&
          <p className="package-note">Bạn đã từng sử dụng gói này; hiện không có quyền học còn hiệu lực.</p>}
        {item.isActive ? <p className="package-active" role="status">Bạn đang sử dụng gói học này.</p>
          : <button type="button" className="primary-button package-action" disabled={checkout.busy}
              onClick={createPayment}>{checkout.busy ? "Đang tạo yêu cầu…" : "Thanh toán qua VNPay"}</button>}
        {checkout.message && <p className="package-alert" role="alert">{checkout.message}</p>}
        <p className="package-note">Quyền học chỉ được kích hoạt sau khi hệ thống xác nhận thanh toán.</p>
      </aside>
    </div>}
  </main></SiteLayout>;
}
