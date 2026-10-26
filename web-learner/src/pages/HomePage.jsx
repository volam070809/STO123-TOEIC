import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import SessionBoundary from "../components/auth/SessionBoundary";
import SiteLayout from "../layouts/SiteLayout";
import { examApi, placementApi } from "../services/examApi";
import "../styles/home.css";

const homeFeatures = [
  { key: "placement", title: "Kiểm tra đầu vào", route: "/placement", image: "/images/home/placement-card.webp",
    guestImage: "/images/home/study-notes.webp", description: "Xác định trình độ hiện tại và định hướng lộ trình học.", action: "Xem lộ trình học", primary: true },
  { key: "mock", title: "Thi thử TOEIC", route: "/mock-test", image: "/images/home/mock-card.webp",
    description: "Làm quen cấu trúc và thời gian của một bài thi TOEIC.", action: "Mở thi thử", primary: true },
  { key: "practice", title: "Luyện tập", route: "/practice", image: "/images/home/practice-card.webp",
    description: "Luyện TOEIC theo Part và củng cố kiến thức theo từng câu.", action: "Bắt đầu luyện" },
  { key: "vocabulary", title: "Học từ vựng", route: "/vocabulary", image: "/images/home/vocabulary-card.webp",
    guestImage: "/images/home/vocabulary-books.webp", description: "Khám phá danh sách và chủ đề từ vựng.", action: "Mở từ vựng" },
  { key: "courses", title: "Khóa học", route: "/courses", image: "/images/home/courses-card.webp",
    description: "Xem nội dung và lộ trình học hiện có.", action: "Xem khóa học" },
  { key: "structure", title: "Cấu trúc đề thi TOEIC", route: "/toeic/structure", image: "/images/home/exam-sheet.webp",
    description: "Tìm hiểu 7 Part của bài thi Listening & Reading.", action: "Xem cấu trúc" },
];

function HomeFeatureGrid({ learner = false, placement, mock }) {
  return <div className="home-access-grid home-feature-grid">{homeFeatures.map(feature => {
    let description = feature.description;
    if (learner && feature.key === "placement" && placement?.completedCount)
      description = `${placement.completedCount} lần đã hoàn thành`;
    if (learner && feature.key === "mock") description = mock?.some(exam => exam.state === "COMPLETED")
      ? `${mock.filter(exam => exam.state === "COMPLETED").length} đề đã hoàn thành` : "Chọn một đề thi thử để bắt đầu.";
    if (learner && feature.key === "practice")
      description = "Luyện từ vựng, luyện TOEIC theo Part và xem lại các lượt luyện đã lưu.";
    return <article className="access-card home-learner-card" key={feature.key}>
      <div className="home-access-photo"><img src={!learner && feature.guestImage ? feature.guestImage : feature.image} alt="" loading="lazy" /></div>
      <div className="home-learner-card-body"><h3>{feature.title}</h3><p>{description}</p>
        <Link className={feature.primary ? "primary-button" : "outline-button"} to={feature.route}>{feature.action}</Link></div>
    </article>;
  })}</div>;
}

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
    <HomeFeatureGrid learner placement={placement} mock={mock}/>
  </div>;
}

function GuestHome() {
  return <div className="site-container home-content home-guest">
    <div className="page-heading"><span>STO123 / Tổng quan</span><h1>Học TOEIC cùng STO123</h1>
      <p>Khám phá lộ trình, nội dung học và các hình thức luyện tập hiện có.</p></div>
    <section className="home-hero home-guest-hero"><div className="home-hero-content"><span className="small-badge">HỌC TOEIC HIỆU QUẢ HƠN MỖI NGÀY</span>
      <h1>Chinh phục TOEIC<br />theo cách của bạn</h1>
      <p>Bạn được học thử một danh sách từ vựng và xem cách hệ thống luyện TOEIC hoạt động.</p>
      <div className="action-row"><Link className="primary-button" to="/vocabulary">Học thử từ vựng</Link>
        <Link className="outline-button" to="/login">Đăng nhập</Link><Link className="home-register-link" to="/register">Đăng ký<HomeIcon kind="arrow" /></Link></div>
      <div className="home-benefits"><span><HomeIcon kind="book" />Học theo chủ đề</span><span><HomeIcon kind="practice" />Luyện tập đa dạng</span><span><HomeIcon kind="path" />Theo dõi tiến độ</span></div>
      </div><div className="home-hero-photo"><img src="/images/home/hero-study.webp" alt="" width="1200" height="800" fetchPriority="high" /><span className="home-photo-badge">HỌC HÔM NAY · VỮNG TƯƠNG LAI</span><p>Good English<br />Brighter opportunities</p></div>
    </section>
    <section className="home-progress-card"><div><span className="small-badge">LỘ TRÌNH HỌC TOEIC</span>
      <h2>Xây dựng hành trình học phù hợp</h2><p>Đăng nhập để kiểm tra đầu vào, nhận giai đoạn đề xuất và lưu tiến độ học của bạn.</p></div>
      <Link className="outline-button" to="/login">Đăng nhập để bắt đầu</Link></section>
    <div className="home-section-heading"><h2 className="section-title">Học và luyện tập</h2><p>Khám phá các hoạt động học TOEIC hiện có trên STO123.</p></div>
    <HomeFeatureGrid/>
    <p className="home-note">Bạn có thể xem nội dung công khai; đăng nhập để sử dụng các chức năng dành cho học viên.</p>
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

export { GuestHome, HomeFeatureGrid, LearnerHome };
