import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import SessionBoundary from "../components/auth/SessionBoundary";
import SiteLayout from "../layouts/SiteLayout";
import { useContentRevision } from "../services/contentRevisions";
import { examApi, placementApi } from "../services/examApi";
import "../styles/home.css";

const homeFeatures = [
  { key: "placement", title: "Thi đầu vào", route: "/placement", image: "/images/home/placement-card.webp",
    description: "Xác định trình độ hiện tại và định hướng lộ trình học.", action: "Xem bài thi", primary: true },
  { key: "mock", title: "Thi thử TOEIC", route: "/mock-test", image: "/images/home/mock-card.webp",
    description: "Làm quen cấu trúc và thời gian của một bài thi TOEIC.", action: "Mở thi thử", primary: true },
  { key: "practice", title: "Luyện tập", route: "/practice", image: "/images/home/practice-card.webp",
    description: "Luyện TOEIC theo Part và củng cố kiến thức theo từng câu.", action: "Bắt đầu luyện" },
  { key: "vocabulary", title: "Học từ vựng", route: "/vocabulary", image: "/images/home/vocabulary-card.webp",
    description: "Khám phá danh sách và chủ đề từ vựng.", action: "Mở từ vựng" },
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
    return <article className={`access-card home-learner-card home-feature-${feature.key}`} key={feature.key}>
      <div className="home-access-photo"><img src={feature.image} alt="" loading="lazy" /></div>
      <div className="home-learner-card-body"><h3>{feature.title}</h3><p>{description}</p>
        <Link className={feature.primary ? "primary-button" : "outline-button"} to={feature.route}>{feature.action}</Link></div>
    </article>;
  })}</div>;
}

function LearnerHome({ user }) {
  const { token } = useAuth();
  const contentRevision = useContentRevision("EXAMS", "COURSES", "ACCESS");
  const [mock, setMock] = useState(null);
  const [placement, setPlacement] = useState(null);
  useEffect(() => {
    let live = true;
    examApi.catalog(token).then(value => { if (live) setMock(value); }).catch(() => {});
    placementApi.summary(token).then(value => { if (live) setPlacement(value); }).catch(() => {});
    return () => { live = false; };
  }, [token, contentRevision]);
  return <div className="site-container home-content home-learner">
    <div className="page-heading"><span>Học viên / Tổng quan</span><h1>Chào mừng, {user.hoTen}</h1>
      <p>Chọn hoạt động học tiếp theo.</p></div>
    <section className="home-hero home-learner-hero">
      <div className="home-lesson-photo"><img src="/images/home/learner-study.webp" alt="" width="1000" height="667" /><span>Học đúng trọng tâm</span>
        <div className="home-photo-note" aria-hidden="true"><i />Hành trình TOEIC của bạn <strong>{placement?.currentStage ? `Giai đoạn ${placement.currentStage}` : "Tiếp tục khám phá"}</strong></div></div>
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
      <Link className="outline-button" to="/placement">Xem kết quả thi đầu vào</Link></section>}
    <div className="home-section-heading"><h2 className="section-title">Học và luyện tập</h2><p>Chọn hoạt động phù hợp để tiếp tục học hôm nay.</p></div>
    <HomeFeatureGrid learner placement={placement} mock={mock}/>
  </div>;
}

function GuestHome() {
  return <div className="site-container home-content home-learner home-visitor">
    <div className="page-heading"><span>STO123 / Tổng quan</span><h1>Học TOEIC cùng STO123</h1>
      <p>Khám phá lộ trình, nội dung học và các hình thức luyện tập hiện có.</p></div>
    <section className="home-hero home-learner-hero"><div className="home-lesson-photo"><img src="/images/home/learner-study.webp" alt="" width="1000" height="667" fetchPriority="high" /><span>Học đúng trọng tâm</span>
      <div className="home-photo-note" aria-hidden="true"><i />Hành trình TOEIC của bạn <strong>Bắt đầu khám phá</strong></div></div>
      <div className="home-hero-content"><span className="small-badge">HỌC TOEIC CÙNG STO123</span>
        <h2>Bắt đầu hành trình học của bạn</h2><p>Khám phá khóa học, học thử từ vựng và làm quen với các hoạt động luyện TOEIC.</p>
        <div className="action-row"><Link className="primary-button" to="/vocabulary">Học thử từ vựng</Link>
          <Link className="outline-button" to="/register">Tạo tài khoản</Link></div></div>
    </section>
    <section className="home-progress-card"><div><span className="small-badge">THI ĐẦU VÀO TOEIC</span>
      <h2>Xây dựng hành trình học phù hợp</h2><p>Đăng nhập để thi đầu vào, nhận giai đoạn đề xuất và lưu tiến độ học của bạn.</p></div>
      <Link className="outline-button" to="/login">Đăng nhập để bắt đầu</Link></section>
    <div className="home-section-heading"><h2 className="section-title">Học và luyện tập</h2><p>Khám phá các hoạt động học TOEIC hiện có trên STO123.</p></div>
    <HomeFeatureGrid/>
    <p className="home-note">Bạn có thể học thử một số từ vựng và xem thông tin công khai. Đăng nhập để luyện tập, thi và lưu tiến độ học.</p>
  </div>;
}

export default function HomePage() {
  const { user } = useAuth();
  const adminHome = user?.vaiTro === "ADMIN_QUAN_LY" ? "/admin" :
    user?.vaiTro === "ADMIN_NOI_DUNG" ? "/admin/content" : null;
  return <SiteLayout className="home-page"><SessionBoundary>{adminHome ?
    <div className="site-container home-content"><div className="page-heading"><span>STO123 / Xem trước</span>
      <h1>Chào mừng, {user.hoTen}</h1><p>Bạn đang xem trang công khai với quyền quản trị chỉ đọc.</p>
      <Link className="primary-button" to={adminHome}>Về trang quản trị</Link></div>
      <HomeFeatureGrid /></div> : user ? <LearnerHome user={user} /> : <GuestHome />}</SessionBoundary></SiteLayout>;
}

export { GuestHome, HomeFeatureGrid, LearnerHome };
