import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import SessionBoundary from "../components/auth/SessionBoundary";
import SiteLayout from "../layouts/SiteLayout";
import { examApi, placementApi } from "../services/examApi";

function LearnerHome({ user }) {
  const { token } = useAuth();
  const [mock, setMock] = useState(null);
  const [placement, setPlacement] = useState(null);
  useEffect(() => {
    let live = true;
    examApi.summary(token).then(value => { if (live) setMock(value); }).catch(() => {});
    placementApi.summary(token).then(value => { if (live) setPlacement(value); }).catch(() => {});
    return () => { live = false; };
  }, [token]);
  return <div className="site-container">
    <div className="page-heading"><span>Học viên / Tổng quan</span><h1>Chào mừng, {user.hoTen}</h1>
      <p>Chọn hoạt động học tiếp theo.</p></div>
    <section className="home-hero"><span className="small-badge">HỌC TOEIC CÙNG STO123</span>
      <h2>Tiếp tục hành trình học của bạn</h2>
      <p>Luyện theo chủ đề, kiểm tra năng lực và theo dõi từng lượt thi đã hoàn thành.</p>
      <div className="action-row"><Link className="primary-button" to="/courses">Tiếp tục học</Link>
        <Link className="outline-button" to="/mock-test">Thi thử TOEIC</Link></div></section>
    {mock?.active && <section className="home-hero"><span className="small-badge">BÀI ĐANG LÀM</span>
      <h2>{mock.active.name}</h2>
      <p>Đã trả lời {mock.active.answered}/{mock.active.totalQuestions} câu. Thời gian còn lại được giữ khi rời bài.</p>
      <Link className="primary-button" to={`/exam/${mock.active.attemptId}`}>Tiếp tục bài thi</Link></section>}
    {placement?.currentStage && <section className="home-progress-card">
      <div><span className="small-badge">LỘ TRÌNH HIỆN TẠI</span><h2>Giai đoạn {placement.currentStage}</h2>
        <p>Từ kết quả kiểm tra đầu vào gần nhất. Xem khóa học phù hợp với mục tiêu của bạn.</p></div>
      <Link className="outline-button" to="/placement">Xem lộ trình</Link></section>}
    <h2 className="section-title">Học và luyện tập</h2>
    <div className="home-access-grid">
      <article className="access-card"><span className="access-number green">01</span><h3>Kiểm tra đầu vào</h3>
        <p>{placement?.completedCount ? `${placement.completedCount} lần đã hoàn thành` : "Xác định trình độ hiện tại và lộ trình học."}</p>
        <Link className="primary-button" to="/placement">Xem lộ trình học</Link></article>
      <article className="access-card"><span className="access-number orange">02</span><h3>Thi thử TOEIC</h3>
        <p>{mock?.completedCount ? `${mock.completedCount} lần thi đã hoàn thành` : "Chọn đề có sẵn, đề ngẫu nhiên hoặc thi theo Part."}</p>
        <Link className="primary-button" to="/mock-test">Mở thi thử</Link></article>
      <article className="access-card"><span className="access-number purple">03</span><h3>Luyện từ vựng</h3>
        <p>Học theo chủ đề và xem lại mọi lần luyện đã lưu.</p>
        <Link className="outline-button" to="/practice/vocabulary">Bắt đầu luyện</Link></article>
      <article className="access-card"><span className="access-number green">04</span><h3>Từ vựng</h3>
        <p>Khám phá danh sách và chủ đề từ vựng.</p>
        <Link className="outline-button" to="/vocabulary">Mở từ vựng</Link></article>
      <article className="access-card"><span className="access-number orange">05</span><h3>Khóa học</h3>
        <p>Xem nội dung và lộ trình học hiện có.</p>
        <Link className="outline-button" to="/courses">Xem khóa học</Link></article>
    </div>
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
