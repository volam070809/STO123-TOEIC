import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import SiteLayout from "../../layouts/SiteLayout";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import { placementApi } from "../../services/examApi";
import PlacementAnalysisView from "./PlacementAnalysisView";
import PlacementIcon from "./PlacementIcon";
import { verifiedCurrentStage } from "./placementAttemptAnalysis";
import "../../styles/exam.css";
import "../../styles/placement.css";
import "../../styles/learner-journey.css";
import heroImage from "../../assets/placement/placement-hero.svg";
import { useContentRevision } from "../../services/contentRevisions";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";
const remaining = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");
const partStructure = [
  { part: 1, name: "Mô tả tranh", count: 6, icon: "file" },
  { part: 2, name: "Hỏi và đáp", count: 25, icon: "headphones" },
  { part: 3, name: "Hội thoại", count: 39, icon: "headphones" },
  { part: 4, name: "Bài nói ngắn", count: 30, icon: "headphones" },
  { part: 5, name: "Hoàn thành câu", count: 30, icon: "pencil" },
  { part: 6, name: "Hoàn thành đoạn", count: 16, icon: "book" },
  { part: 7, name: "Đọc hiểu", count: 54, icon: "book" },
];
const stageNames = { 1: "Nền tảng", 2: "Phát triển", 3: "Nâng cao" };

function PlacementHero({ primary, secondary }) {
  return <section className="placement-overview-hero" aria-labelledby="placement-hero-title">
    <div className="placement-overview-copy">
      <span className="placement-hero-badge"><PlacementIcon name="sparkle" size={17} /> BÀI KIỂM TRA ĐẦU VÀO</span>
      <h1 id="placement-hero-title">Hiểu năng lực TOEIC.<br /><span>Học đúng lộ trình.</span></h1>
      <p>Hoàn thành bài kiểm tra 7 Part để xem kết quả Nghe, Đọc và giai đoạn học phù hợp với bạn.</p>
      <div className="placement-hero-actions">{primary}{secondary}</div>
      <div className="placement-hero-facts" aria-label="Thông tin bài kiểm tra">
        <span><PlacementIcon name="file" size={20} /><strong>200</strong><small>câu hỏi</small></span>
        <span><PlacementIcon name="clock" size={20} /><strong>120</strong><small>phút</small></span>
        <span><PlacementIcon name="layers" size={20} /><strong>7</strong><small>Part TOEIC</small></span>
      </div>
    </div>
    <div className="placement-overview-art"><img src={heroImage} width="640" height="480"
      alt="Học viên dùng laptop và tai nghe để luyện kỹ năng Nghe, Đọc TOEIC" /></div>
  </section>;
}

export default function PlacementPage() {
  const { isAuthenticated } = useAuth();
  return <SessionBoundary>{isAuthenticated ? <LearnerPlacementPage /> : <GuestPlacementOverview />}</SessionBoundary>;
}

function GuestPlacementOverview() {
  const { requireAuthentication } = useGuestAuthPrompt();
  return <SiteLayout><div className="site-container exam-home placement-page">
    <PlacementHero primary={<button className="primary-button" type="button" aria-haspopup="dialog"
      onClick={() => requireAuthentication("/placement")}>Bắt đầu kiểm tra <PlacementIcon name="arrow" size={18} /></button>}
      secondary={<a className="outline-button" href="#placement-prep">Xem cách chuẩn bị</a>} />
    <details className="learner-disclosure placement-intro-details" id="placement-prep"><summary><span><small>TRƯỚC KHI BẮT ĐẦU</small><strong>Cấu trúc và chuẩn bị</strong><em>7 Part · 200 câu · 120 phút</em></span><DisclosureToggle /></summary>
      <div className="learner-disclosure-body"><div className="placement-intro-columns"><div><h3>Chuẩn bị</h3><p>Chọn nơi yên tĩnh, kiểm tra tai nghe và dành đủ 120 phút. Bạn có thể tạm dừng và tiếp tục bài đang làm.</p></div>
        <div><h3>Cấu trúc bài thi</h3><p>Listening: Part 1–4 · 100 câu. Reading: Part 5–7 · 100 câu.</p></div></div>
        <div className="placement-intro-parts">{partStructure.map(item => <span key={item.part}><b>Part {item.part}</b>{item.name}<small>{item.count} câu</small></span>)}</div></div></details>
  </div></SiteLayout>;
}

function LearnerPlacementPage() {
  const { token } = useAuth();
  const contentRevision = useContentRevision("EXAMS", "COURSES", "ACCESS");
  const navigate = useNavigate();
  const [state, setState] = useState(null);
  const [history, setHistory] = useState(null);
  const [analysisLoading, setAnalysisLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [latestMetrics, setLatestMetrics] = useState(null);
  const [latestStatus, setLatestStatus] = useState("idle");
  const [recommendations, setRecommendations] = useState([]);
  const [coursesState, setCoursesState] = useState("idle");
  const [target, setTarget] = useState("");
  const [targetFeedback, setTargetFeedback] = useState(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [retryLoad, setRetryLoad] = useState(0);
  const [analysisRetry, setAnalysisRetry] = useState(0);
  const [, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.state(token), placementApi.summary(token)]).then(([current, past]) => {
      if (!live) return;
      setState(current); setHistory(past);
      if (past?.completedCount) {
        setLatestStatus("loading");
        placementApi.result(token).then(outcome => {
          if (live) { setLatestMetrics(outcome); setLatestStatus("ready"); }
        }).catch(() => { if (live) setLatestStatus("error"); });
      }
    }).catch(() => { if (live) setError("Không thể tải lộ trình học."); });
    const timer = setInterval(() => setTick(n => n + 1), 1000);
    return () => { live = false; clearInterval(timer); };
  }, [token, retryLoad, contentRevision]);

  useEffect(() => {
    if (!history?.completedCount) return;
    let live = true;
    Promise.resolve().then(async () => {
      if (!live) return;
      setAnalysisLoading(true);
      setError("");
      try {
        const outcome = await placementApi.result(token);
        if (!live) return;
        setResult(outcome);
        setTarget(outcome.targetScore?.toString() ?? "");
        if (!outcome.stage) { setCoursesState("idle"); return; }
        setCoursesState("loading");
        try { const courses = await placementApi.courses(token);
          if (live) { setRecommendations(courses); setCoursesState("ready"); }
        } catch { if (live) setCoursesState("error"); }
      } catch (e) { if (live) { setError(e.data?.message || "Không thể tải phân tích kết quả."); setCoursesState("idle"); } }
      finally { if (live) setAnalysisLoading(false); }
    });
    return () => { live = false; };
  }, [history?.completedCount, token, analysisRetry, contentRevision]);

  async function reloadCourses() {
    if (analysisLoading) return;
    setAnalysisLoading(true); setError(""); setCoursesState("loading");
    try {
      const courses = await placementApi.courses(token);
      setRecommendations(courses);
      setCoursesState("ready");
    } catch { setCoursesState("error"); }
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
    setPending(true); setError(""); setTargetFeedback(null);
    try {
      const saved = await placementApi.target(Number(target), token);
      setResult(old => ({ ...old, targetScore: saved.targetScore }));
      setLatestMetrics(old => old ? { ...old, targetScore: saved.targetScore } : old);
      setTargetFeedback({ type: "success", text: "Đã lưu điểm mục tiêu." });
      setCoursesState("loading");
      try { setRecommendations(await placementApi.courses(token)); setCoursesState("ready"); }
      catch { setCoursesState("error"); }
    } catch (e) { setTargetFeedback({ type: "error", text: e.data?.message || "Không thể lưu điểm mục tiêu." }); }
    finally { setPending(false); }
  }

  const hasActive = state?.status === "DANG_LAM" || state?.status === "BO_DO";
  const verifiedStage = latestStatus === "ready" ? verifiedCurrentStage(latestMetrics) : null;
  return <SiteLayout><div className="site-container exam-home placement-page">
    {history?.completedCount ? <header className="placement-learner-heading"><span className="placement-eyebrow">KẾT QUẢ GẦN NHẤT</span>
      <h1>Hành trình TOEIC của bạn bắt đầu từ đây</h1><p>Hiểu điểm mạnh của mình và chọn bước học tiếp theo.</p></header> :
      <PlacementHero primary={hasActive ? <a className="primary-button" href="#placement-active">Tiếp tục bài kiểm tra →</a> :
        <button className="primary-button" type="button" disabled={pending} onClick={start}>{pending ? "Đang bắt đầu…" : "Bắt đầu kiểm tra →"}</button>}
        secondary={<a className="outline-button" href="#placement-prep">Xem cách chuẩn bị</a>} />}
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && error && <button className="outline-button" type="button" onClick={() => { setError(""); setRetryLoad(value => value + 1); }}>Thử tải lại</button>}
    {!state && !error && <p role="status">Đang tải kết quả học tập…</p>}
    {hasActive && <section className="exam-card placement-action-card" id="placement-active"><span className="placement-icon-tile"><PlacementIcon name="progress" size={27} /></span><div><span className="placement-eyebrow">Bài đang làm</span><h2>Tiếp tục kiểm tra đầu vào</h2>
      <p>7 Part · {state.totalQuestions} câu · Đã trả lời {state.answered} / {state.totalQuestions}</p>
      <div className="placement-bar" role="progressbar" aria-label="Câu đã trả lời" aria-valuemin="0"
        aria-valuemax={state.totalQuestions || 200} aria-valuenow={state.answered || 0}>
        <span style={{ width: `${state.totalQuestions ? 100 * state.answered / state.totalQuestions : 0}%` }} /></div>
      <p><PlacementIcon name="clock" size={17} /> {state.isPaused ? "Đang tạm dừng · " : ""}Thời gian còn lại: {remaining(state.remainingSeconds)}</p></div>
      <Link className="primary-button" to={`/exam/${state.attemptId}`}>Tiếp tục <PlacementIcon name="arrow" size={17} /></Link></section>}
    {!hasActive && state && !history?.completedCount && <section className="exam-card placement-action-card" id="placement-start"><span className="placement-icon-tile"><PlacementIcon name="file" size={27} /></span><div><span className="placement-eyebrow">Sẵn sàng đánh giá</span><h2>Bắt đầu kiểm tra đầu vào</h2>
      <p>Kết quả 7 Part giúp đánh giá năng lực và chọn khóa học phù hợp.</p></div>
      <button className="primary-button" type="button" disabled={pending} onClick={start}>
        {pending ? "Đang bắt đầu…" : "Bắt đầu kiểm tra"}</button></section>}
    {history?.completedCount > 0 && !result && <section className="exam-card placement-latest-card placement-stage-feature" id="placement-latest"><span className="placement-icon-tile"><PlacementIcon name="award" size={27} /></span><div><span className="placement-eyebrow">GIAI ĐOẠN HỌC CỦA BẠN</span>
      <h2>{verifiedStage ? stageNames[verifiedStage] : latestStatus === "loading" ? "Đang xác minh giai đoạn…" :
        latestStatus === "error" ? "Chưa thể tải giai đoạn KNN" : "Chưa có kết quả phân lớp KNN"}</h2>
      <p>{verifiedStage ? `Giai đoạn ${verifiedStage} · ` : ""}Hoàn thành {date(history.latestCompletedAt)}</p>
      {latestMetrics?.result && <div className="placement-stage-scores"><span>TOEIC ước tính <strong>{latestMetrics.result.totalScore ?? "—"}<small> / 990</small></strong></span>
        <span>Listening <strong>{latestMetrics.result.listening?.correct ?? "—"}/{latestMetrics.result.listening?.total ?? "—"}</strong></span>
        <span>Reading <strong>{latestMetrics.result.reading?.correct ?? "—"}/{latestMetrics.result.reading?.total ?? "—"}</strong></span></div>}</div>
      <div className="placement-latest-actions"><Link className="primary-button" to="/smart-learning">Xem gợi ý học tiếp →</Link></div></section>}
    {history?.completedCount > 0 && analysisLoading && !result && <p role="status">Đang tải phân tích bài kiểm tra…</p>}
    {history?.completedCount > 0 && !analysisLoading && !result && error && <button className="outline-button" type="button" onClick={() => { setError(""); setAnalysisRetry(value => value + 1); }}>Thử tải lại phân tích</button>}
    {result && <PlacementAnalysisView analysis={result} courses={recommendations} target={target}
          onTargetChange={value => { setTarget(value); setTargetFeedback(null); setRecommendations(old => old.map(course => ({ ...course, recommended: false, recommendationOrder: null }))); }}
          onTargetSubmit={confirmTarget} pending={pending} showAttemptHeader targetFeedback={targetFeedback}
          coursesState={coursesState === "idle" && analysisLoading ? "loading" : coursesState}
          onCoursesRetry={reloadCourses} onRetake={hasActive ? undefined : start} retakePending={pending} />}
    {!history?.completedCount && <details className="learner-disclosure placement-intro-details" id="placement-prep"><summary><span><small>TRƯỚC KHI BẮT ĐẦU</small><strong>Cấu trúc và chuẩn bị</strong><em>7 Part · 200 câu · 120 phút</em></span><DisclosureToggle /></summary>
      <div className="learner-disclosure-body"><p>Chuẩn bị tai nghe, chỗ yên tĩnh và đủ thời gian. Bài đang làm có thể tiếp tục sau khi rời trang.</p>
        <div className="placement-intro-parts">{partStructure.map(item => <span key={item.part}><b>Part {item.part}</b>{item.name}<small>{item.count} câu</small></span>)}</div></div></details>}
  </div></SiteLayout>;
}
