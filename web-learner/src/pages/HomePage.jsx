import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import SessionBoundary from "../components/auth/SessionBoundary";
import SiteLayout from "../layouts/SiteLayout";
import { examApi, placementApi } from "../services/examApi";
import "../styles/home.css";

function LearnerHome({ user }) {
  const { token } = useAuth();
  const [mock, setMock] = useState(null);
  const [placement, setPlacement] = useState(null);
  useEffect(() => {
    let live = true;
    examApi.catalog(token).then(value => { if (live) setMock(value); }).catch(() => {});
    placementApi.summary(token).then(value => { if (live) setPlacement(value); }).catch(() => {});
    return () => { live = false; };
  }, [token]);
  return <div className="site-container home-content home-learner">
    <div className="page-heading"><span>Học viên / Tổng quan</span><h1>Chào mừng, {user.hoTen}</h1>
      <p>Chọn hoạt động học tiếp theo.</p></div>
    <section className="home-hero home-learner-hero">
      <div className="home-lesson-photo"><img src="/images/home/learner-study.webp" alt="" width="1000" height="667" /><span>Học đúng trọng tâm</span></div>
      <div className="home-hero-content"><span className="small-badge">HỌC TOEIC CÙNG STO123</span>
      <h2>Tiếp tục hành trình học của bạn</h2>
      <p>Luyện theo chủ đề, kiểm tra năng lực và theo dõi từng lượt thi đã hoàn thành.</p>
      <div className="action-row"><Link className="primary-button" to="/courses">Tiếp tục học</Link>
        <Link className="outline-button" to="/mock-test">Thi thử TOEIC</Link></div></div></section>
    {mock?.find(exam => exam.state === "IN_PROGRESS") && <section className="home-resume-card">
      <span className="small-badge">BÀI ĐANG LÀM</span>
      <h2>{mock.find(exam => exam.state === "IN_PROGRESS").examName}</h2>
      <Link className="primary-button" to={`/exam/${mock.find(exam => exam.state === "IN_PROGRESS").attemptId}`}>
        Tiếp tục bài thi</Link></section>}
    {placement?.currentStage && <section className="home-progress-card">
      <div><span className="small-badge">LỘ TRÌNH HIỆN TẠI</span><h2>Giai đoạn {placement.currentStage}</h2>
        <p>Từ kết quả kiểm tra đầu vào gần nhất. Xem khóa học phù hợp với mục tiêu của bạn.</p></div>
      <Link className="outline-button" to="/placement">Xem lộ trình</Link></section>}
    <div className="home-section-heading"><h2 className="section-title">Học và luyện tập</h2><p>Chọn hoạt động phù hợp để tiếp tục học hôm nay.</p></div>
    <div className="home-access-grid">
      <article className="access-card home-learner-card"><div className="home-access-photo"><img src="/images/home/placement-card.webp" alt="" loading="lazy" /></div><div className="home-learner-card-body"><h3>Kiểm tra đầu vào</h3>
        <p>{placement?.completedCount ? `${placement.completedCount} lần đã hoàn thành` : "Xác định trình độ hiện tại và lộ trình học."}</p>
        <Link className="primary-button" to="/placement">Xem lộ trình học</Link></div></article>
      <article className="access-card home-learner-card"><div className="home-access-photo"><img src="/images/home/mock-card.webp" alt="" loading="lazy" /></div><div className="home-learner-card-body"><h3>Thi thử TOEIC</h3>
        <p>{mock?.some(exam => exam.state === "COMPLETED") ?
          `${mock.filter(exam => exam.state === "COMPLETED").length} đề đã hoàn thành` : "Chọn một đề thi thử để bắt đầu."}</p>
        <Link className="primary-button" to="/mock-test">Mở thi thử</Link></div></article>
      <article className="access-card home-learner-card"><div className="home-access-photo"><img src="/images/home/practice-card.webp" alt="" loading="lazy" /></div><div className="home-learner-card-body"><h3>Luyện tập</h3>
        <p>Luyện từ vựng, luyện TOEIC theo Part và xem lại các lượt luyện đã lưu.</p>
        <Link className="outline-button" to="/practice">Bắt đầu luyện</Link></div></article>
      <article className="access-card home-learner-card"><div className="home-access-photo"><img src="/images/home/vocabulary-card.webp" alt="" loading="lazy" /></div><div className="home-learner-card-body"><h3>Học từ vựng</h3>
        <p>Khám phá danh sách và chủ đề từ vựng.</p>
        <Link className="outline-button" to="/vocabulary">Mở từ vựng</Link></div></article>
      <article className="access-card home-learner-card"><div className="home-access-photo"><img src="/images/home/courses-card.webp" alt="" loading="lazy" /></div><div className="home-learner-card-body"><h3>Khóa học</h3>
        <p>Xem nội dung và lộ trình học hiện có.</p>
        <Link className="outline-button" to="/courses">Xem khóa học</Link></div></article>
    </div>
  </div>;
}

function GuestHome() {
  return <div className="site-container home-content home-guest">
    <section className="home-hero home-guest-hero"><div className="home-hero-content"><span className="small-badge">HỌC TOEIC HIỆU QUẢ HƠN MỖI NGÀY</span>
      <h1>Chinh phục TOEIC<br />theo cách của bạn</h1>
      <p>Bạn được học thử một danh sách từ vựng và xem cách hệ thống luyện TOEIC hoạt động.</p>
      <div className="action-row"><Link className="primary-button" to="/vocabulary">Học thử từ vựng</Link>
        <Link className="outline-button" to="/login">Đăng nhập</Link><Link className="home-register-link" to="/register">Đăng ký<HomeIcon kind="arrow" /></Link></div>
      <div className="home-benefits"><span><HomeIcon kind="book" />Học theo chủ đề</span><span><HomeIcon kind="practice" />Luyện tập đa dạng</span><span><HomeIcon kind="path" />Theo dõi tiến độ</span></div>
      </div><div className="home-hero-photo"><img src="/images/home/hero-study.webp" alt="" width="1200" height="800" fetchPriority="high" /><span className="home-photo-badge">HỌC HÔM NAY · VỮNG TƯƠNG LAI</span><p>Good English<br />Brighter opportunities</p></div>
    </section>
    <div className="home-section-heading"><h2 className="section-title">Bắt đầu từ một bài học nhỏ</h2><p>Khám phá nội dung học thử và những lợi ích khi có tài khoản STO123.</p></div>
    <div className="home-access-grid">
      <article className="access-card home-photo-card"><div className="home-card-photo"><img src="/images/home/vocabulary-books.webp" alt="" width="700" height="467" loading="lazy" /></div><span className="home-feature-icon green"><HomeIcon kind="book" /></span><h3>1 danh sách từ vựng</h3>
        <p>Học thử Office Essentials gồm 10 từ.</p><Link className="primary-button" to="/vocabulary">Mở danh sách</Link></article>
      <article className="access-card home-photo-card"><div className="home-card-photo"><img src="/images/home/exam-sheet.webp" alt="" width="700" height="467" loading="lazy" /></div><span className="home-feature-icon orange"><HomeIcon kind="exam" /></span><h3>Xem cấu trúc TOEIC</h3>
        <p>Tìm hiểu 7 Part của bài thi Listening &amp; Reading.</p><Link className="outline-button" to="/toeic/structure">Xem cấu trúc</Link></article>
      <article className="access-card home-photo-card"><div className="home-card-photo"><img src="/images/home/study-notes.webp" alt="" width="700" height="394" loading="lazy" /></div><span className="home-feature-icon purple"><HomeIcon kind="path" /></span><h3>Lưu tiến độ cá nhân</h3>
        <p>Đăng nhập để lưu kết quả và lộ trình.</p><Link className="outline-button" to="/login">Đăng nhập</Link></article>
    </div>
    <p className="home-note">Khách có thể xem nội dung mẫu; tài khoản giúp mở toàn bộ chủ đề và lưu tiến độ học.</p>
  </div>;
}

export default function HomePage() {
  const { user } = useAuth();
  return <SiteLayout className="home-page"><SessionBoundary>{user ? <LearnerHome user={user} /> : <GuestHome />}</SessionBoundary></SiteLayout>;
}

function HomeIcon({ kind }) {
  return <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    {kind === "book" && <><path d="M12 5v15M12 5C9 3 5 3 3 4v14c3-1 6-1 9 2 3-3 6-3 9-2V4c-2-1-6-1-9 1Z" /></>}
    {kind === "path" && <><circle cx="6" cy="5" r="2" /><circle cx="18" cy="19" r="2" /><path d="M8 5h7a4 4 0 0 1 0 8H9a3 3 0 0 0 0 6h7" /></>}
    {kind === "exam" && <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 4V2h6v2M9 9h6M9 13h6M9 17h3" /></>}
    {kind === "practice" && <><path d="m4 20 4-1L20 7l-3-3L5 16l-1 4ZM14 7l3 3M4 4h6M4 8h3" /></>}
    {kind === "course" && <><path d="m2 9 10-5 10 5-10 5L2 9ZM6 11v6c4 3 8 3 12 0v-6M22 9v7" /></>}
    {kind === "arrow" && <path d="M4 12h16m-6-6 6 6-6 6" />}
  </svg>;
}
