import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import SessionBoundary from "../components/auth/SessionBoundary";
import SiteLayout from "../layouts/SiteLayout";

function LearnerHome({ user }) {
  return <div className="site-container">
    <div className="page-heading"><span>Web / Học viên</span><h1>Chào mừng, {user.hoTen}</h1>
      <p>Tiếp tục khám phá STO123.</p></div>
    <section className="home-hero"><span className="small-badge">HỌC VIÊN</span>
      <h2>Học thử từ vựng</h2>
      <p>Office Essentials là danh sách từ vựng mẫu gồm 10 từ. Kết quả học thử chưa được lưu.</p>
      <div className="action-row"><Link className="primary-button" to="/vocabulary">Mở danh sách mẫu</Link>
        <Link className="outline-button" to="/profile">Xem hồ sơ</Link></div>
    </section>
  </div>;
}

function GuestHome() {
  return <div className="site-container">
    <div className="page-heading"><span>Web / Khách vãng lai</span><h1>Học TOEIC cùng STO123</h1>
      <p>Khám phá nội dung miễn phí trước khi đăng nhập.</p></div>
    <section className="home-hero"><span className="small-badge">CHẾ ĐỘ KHÁCH</span>
      <h2>Bắt đầu học thử ngay</h2>
      <p>Bạn được học thử một danh sách từ vựng và xem cách hệ thống luyện TOEIC hoạt động.</p>
      <div className="action-row"><Link className="primary-button" to="/vocabulary">Học thử từ vựng</Link>
        <Link className="outline-button" to="/login">Đăng nhập / Đăng ký</Link></div>
    </section>
    <h2 className="section-title">Quyền truy cập của khách</h2>
    <div className="home-access-grid">
      <article className="access-card"><span className="access-number green">01</span><h3>1 danh sách từ vựng</h3>
        <p>Học thử Office Essentials gồm 10 từ.</p><Link className="primary-button" to="/vocabulary">Mở danh sách</Link></article>
      <article className="access-card"><span className="access-number orange">02</span><h3>Xem cấu trúc TOEIC</h3>
        <p>Xem 7 Part và dạng câu hỏi mẫu.</p><button className="outline-button" type="button" disabled>Xem cấu trúc</button></article>
      <article className="access-card"><span className="access-number purple">03</span><h3>Lưu tiến độ cá nhân</h3>
        <p>Đăng nhập để lưu kết quả và lộ trình.</p><Link className="outline-button" to="/login">Đăng nhập</Link></article>
    </div>
    <p className="home-note">Khách có thể xem nội dung mẫu; tài khoản giúp mở toàn bộ chủ đề và lưu tiến độ học.</p>
  </div>;
}

export default function HomePage() {
  const { user } = useAuth();
  return <SiteLayout className="home-page"><SessionBoundary>{user ? <LearnerHome user={user} /> : <GuestHome />}</SessionBoundary></SiteLayout>;
}
