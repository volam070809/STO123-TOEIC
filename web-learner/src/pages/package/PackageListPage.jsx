import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { packageApi } from "../../services/packageApi";
import "../../styles/package.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

export default function PackageListPage() {
  const { token } = useAuth();
  const [view, setView] = useState({ token: null, packages: null, error: "" });
  useEffect(() => {
    let live = true;
    packageApi.list(token).then(packages => {
      if (live) setView({ token, packages, error: "" });
    }).catch(() => {
      if (live) setView({ token, packages: null, error: "Không thể tải danh sách gói học." });
    });
    return () => { live = false; };
  }, [token]);
  const current = view.token === token ? view : { packages: null, error: "" };

  return <SiteLayout><main className="site-container package-page">
    <header className="package-heading"><span className="package-eyebrow">STO123 LEARNING</span>
      <h1>Gói học phù hợp với bạn</h1>
      <p>Khám phá khóa học trong từng gói. Giá và thời hạn được cập nhật từ hệ thống.</p></header>
    {current.error && <p role="alert" className="package-alert">{current.error}</p>}
    {!current.packages && !current.error && <p role="status">Đang tải gói học…</p>}
    {current.packages?.length === 0 && <p>Hiện chưa có gói học đang mở.</p>}
    <div className="package-grid">{current.packages?.map(item =>
      <article className="package-card" key={item.packageId}>
        <div className="package-card-top"><span className="package-eyebrow">GÓI HỌC</span>
          {item.isActive ? <span className="package-badge">Đang sử dụng</span> :
            item.isPurchased && <span className="package-badge">Đã từng sử dụng</span>}</div>
        <h2>{item.name}</h2><p className="package-price">{money.format(item.price)}</p>
        <p className="package-duration">Thời hạn {item.validDays} ngày</p>
        <p className="package-description">{item.description || "Thông tin gói học đang được cập nhật."}</p>
        <p className="package-course-count">{item.courses.length} khóa học trong gói</p>
        <Link className="primary-button package-action" to={`/packages/${item.packageId}`}>
          {item.isActive ? "Xem gói đang học" : "Xem chi tiết"}</Link>
      </article>)}</div>
  </main></SiteLayout>;
}
