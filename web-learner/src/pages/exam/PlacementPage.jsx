import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import PlacementAnalysisView from "./PlacementAnalysisView";
import { courseEntryReference, courseStage } from "./coursePresentationData";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";
const remaining = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");

export default function PlacementPage() {
  const { isAuthenticated } = useAuth();
  return <SessionBoundary>{isAuthenticated ? <LearnerPlacementPage /> : <GuestPlacementOverview />}</SessionBoundary>;
}

function GuestPlacementOverview() {
  const { requireAuthentication } = useGuestAuthPrompt();
  return <SiteLayout><div className="site-container exam-home placement-page">
    <div className="page-heading placement-heading"><span>Lộ trình học</span>
      <h1>Kiểm tra đầu vào và lộ trình học</h1>
      <p>7 Part · tối đa 200 câu · 120 phút · Đánh giá năng lực để định hướng giai đoạn học phù hợp</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    <section className="exam-card placement-action-card"><div><span className="placement-eyebrow">Đánh giá năng lực</span>
      <h2>Bắt đầu kiểm tra đầu vào</h2>
      <p>Đăng nhập để làm bài, lưu kết quả và nhận phân tích lộ trình cá nhân.</p></div>
      <button className="primary-button" type="button" aria-haspopup="dialog"
        onClick={() => requireAuthentication("/placement")}>Bắt đầu kiểm tra đầu vào</button></section>
    <section aria-labelledby="public-roadmap-title"><h2 id="public-roadmap-title">Lộ trình TOEIC ba giai đoạn</h2>
      <p className="exam-card-note">Thông tin dưới đây là lộ trình chung, không phải kết quả phân lớp cá nhân.</p>
      <div className="placement-summary">{[1, 2, 3].map(stage => <div key={stage}>
        <span className="placement-eyebrow">Giai đoạn {stage}</span>
        <strong>{courseStage[stage].name}</strong><span>{courseEntryReference(stage)}</span>
        <small>{courseStage[stage].description}</small>
      </div>)}</div>
    </section>
    <section><Link className="outline-button" to="/courses">Khám phá tất cả khóa học</Link></section>
  </div></SiteLayout>;
}

function LearnerPlacementPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState(null);
  const [history, setHistory] = useState(null);
  const [analysisLoading, setAnalysisLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [recommendations, setRecommendations] = useState([]);
  const [coursesLoaded, setCoursesLoaded] = useState(false);
  const [target, setTarget] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [retryLoad, setRetryLoad] = useState(0);
  const [, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.state(token), placementApi.summary(token)]).then(([current, past]) => {
      if (!live) return;
      setState(current); setHistory(past);
    }).catch(() => { if (live) setError("Không thể tải lộ trình học."); });
    const timer = setInterval(() => setTick(n => n + 1), 1000);
    return () => { live = false; clearInterval(timer); };
  }, [token, retryLoad]);

  async function showAnalysis() {
    if (analysisLoading) return;
    if (result) {
      document.getElementById("placement-analysis")?.scrollIntoView({ behavior: "smooth", block: "start" });
      return;
    }
    setAnalysisLoading(true); setError("");
    try {
      const outcome = await placementApi.result(token);
      setResult(outcome);
      requestAnimationFrame(() => document.getElementById("placement-analysis")?.scrollIntoView({ behavior: "smooth", block: "start" }));
      setTarget(outcome.targetScore?.toString() ?? "");
      if (outcome.stage) {
        const courses = await placementApi.courses(token);
        setRecommendations(courses);
        setCoursesLoaded(true);
      }
    } catch (e) { setError(e.data?.message || "Không thể tải phân tích kết quả."); }
    finally { setAnalysisLoading(false); }
  }

  async function reloadCourses() {
    if (analysisLoading) return;
    setAnalysisLoading(true); setError("");
    try {
      const courses = await placementApi.courses(token);
      setRecommendations(courses);
      setCoursesLoaded(true);
    } catch { setError("Không thể tải khóa học đang mở. Vui lòng thử lại."); }
    finally { setAnalysisLoading(false); }
  }

  async function start() {
    if (pending) return;
    setPending(true); setError("");
    if (import.meta.env.DEV) performance.mark("placement-start-click");
    try {
      const started = await placementApi.start(token);
      navigate(`/exam/${started.attemptId}`);
    } catch (e) {
      if (import.meta.env.DEV) performance.clearMarks("placement-start-click");
      setError(e.data?.message || "Không thể bắt đầu bài kiểm tra đầu vào.");
    }
    finally { setPending(false); }
  }

  async function confirmTarget(event) {
    event.preventDefault();
    if (pending) return;
    setPending(true); setError("");
    try {
      const saved = await placementApi.target(Number(target), token);
      const courses = await placementApi.courses(token);
      setResult(old => ({ ...old, targetScore: saved.targetScore }));
      setRecommendations(courses);
      setCoursesLoaded(true);
    } catch (e) { setError(e.data?.message || "Không thể lưu điểm mục tiêu."); }
    finally { setPending(false); }
  }

  const hasActive = state?.status === "DANG_LAM" || state?.status === "BO_DO";
  return <SiteLayout><div className="site-container exam-home placement-page">
    <div className="page-heading placement-heading"><span>Lộ trình học</span><h1>Kiểm tra đầu vào và lộ trình học</h1>
      <p>7 Part · 200 câu · 120 phút · Có thể làm lại để cập nhật kết quả</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && error && <button className="outline-button" type="button" onClick={() => { setError(""); setRetryLoad(value => value + 1); }}>Thử tải lại</button>}
    {!state && !error && <p>Đang tải lộ trình học…</p>}
    {hasActive && <section className="exam-card placement-action-card"><div><span className="placement-eyebrow">Bài đang làm</span><h2>Tiếp tục kiểm tra đầu vào</h2>
      <p>7 Part · {state.totalQuestions} câu · Đã trả lời {state.answered} / {state.totalQuestions}</p>
      <div className="placement-bar" role="progressbar" aria-label="Câu đã trả lời" aria-valuemin="0"
        aria-valuemax={state.totalQuestions || 200} aria-valuenow={state.answered || 0}>
        <span style={{ width: `${state.totalQuestions ? 100 * state.answered / state.totalQuestions : 0}%` }} /></div>
      <p>{state.isPaused ? "Đang tạm dừng · " : ""}Thời gian còn lại: {remaining(state.remainingSeconds)}</p></div>
      <Link className="primary-button" to={`/exam/${state.attemptId}`}>Tiếp tục kiểm tra</Link></section>}
    {!hasActive && state && !history?.completedCount && <section className="exam-card placement-action-card"><div><span className="placement-eyebrow">Đánh giá năng lực</span><h2>Bắt đầu kiểm tra đầu vào</h2>
      <p>Kết quả 7 Part giúp đánh giá năng lực và chọn khóa học phù hợp.</p></div>
      <button className="primary-button" type="button" disabled={pending} onClick={start}>
        {pending ? "Đang bắt đầu…" : "Bắt đầu kiểm tra"}</button></section>}
    {history?.completedCount > 0 && <section className="exam-card placement-latest-card"><div><span className="placement-eyebrow">Kết quả gần nhất</span>
      <h2>{history.currentStage ? `Giai đoạn ${history.currentStage}` : "Chưa có kết quả phân lớp"}</h2>
      <p>Hoàn thành {date(history.latestCompletedAt)}</p></div>
      <div className="placement-latest-actions">
        <button className="outline-button" type="button" disabled={analysisLoading} onClick={showAnalysis}>
          {analysisLoading ? "Đang tải…" : "Xem phân tích"}</button>
        {!hasActive && <button className="primary-button" type="button" disabled={pending} onClick={start}>
          {pending ? "Đang bắt đầu…" : "Làm lại kiểm tra"}</button>}
        <Link className="outline-button" to="/courses">Xem lộ trình học</Link>
      </div></section>}
    {result && <PlacementAnalysisView analysis={result} courses={recommendations} target={target}
      onTargetChange={value => { setTarget(value); setRecommendations(old => old.map(course =>
        ({ ...course, recommended: false, recommendationOrder: null }))); }}
      onTargetSubmit={confirmTarget} pending={pending} showAttemptHeader
      coursesLoaded={coursesLoaded && !analysisLoading} />}
    {result?.stage && !coursesLoaded && !analysisLoading &&
      <button type="button" className="outline-button" onClick={reloadCourses}>Thử tải lại khóa học</button>}
    {!history?.completedCount && <section><Link className="outline-button" to="/courses">Khám phá các khóa học</Link></section>}
  </div></SiteLayout>;
}
