import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import PlacementAnalysisView from "./PlacementAnalysisView";
import PlacementIcon from "./PlacementIcon";
import { courseEntryReference, courseStage } from "./coursePresentationData";
import { verifiedCurrentStage } from "./placementAttemptAnalysis";
import "../../styles/exam.css";
import "../../styles/placement.css";
import heroImage from "../../assets/placement/placement-hero.svg";

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

function PlacementHero({ primary, secondary }) {
  return <section className="placement-overview-hero" aria-labelledby="placement-hero-title">
    <div className="placement-overview-copy">
      <span className="placement-hero-badge"><PlacementIcon name="sparkle" size={17} /> Đánh giá năng lực · Phân lớp KNN</span>
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

function PlacementVisualSections() {
  return <>
    <section className="placement-journey" aria-labelledby="placement-journey-title">
      <div className="placement-section-heading"><span>Quy trình đơn giản</span><h2 id="placement-journey-title">Từ bài kiểm tra đến lộ trình học</h2></div>
      <div className="placement-journey-grid">
        {[
          { icon: "file", number: "01", title: "Làm bài kiểm tra", text: "Trả lời câu hỏi ở 7 Part trong 120 phút. Tiến độ được lưu khi bạn tạm rời bài." },
          { icon: "chart", number: "02", title: "Xem phân tích KNN", text: "Xem tỷ lệ đúng từng Part và giai đoạn được phân lớp từ kết quả của bạn." },
          { icon: "route", number: "03", title: "Chọn lộ trình học", text: "Đặt mục tiêu, luyện Part cần cải thiện và xem các khóa học đang mở." },
        ].map(step => <article key={step.number} className="placement-journey-card">
          <div className="placement-step-top"><span className="placement-icon-tile"><PlacementIcon name={step.icon} size={27} /></span><b>{step.number}</b></div>
          <h3>{step.title}</h3><p>{step.text}</p>
        </article>)}
      </div>
    </section>
    <section className="placement-prep" id="placement-prep" aria-labelledby="placement-prep-title">
      <div className="placement-prep-guide"><div className="placement-section-heading"><span>Trước khi bắt đầu</span>
        <h2 id="placement-prep-title">Chuẩn bị cho bài kiểm tra</h2><p>Một vài bước giúp bạn làm bài tập trung hơn.</p></div>
        <div className="placement-guide-list">
          <div><span className="placement-icon-tile"><PlacementIcon name="headphones" /></span><div><strong>Kiểm tra tai nghe</strong><p>Nghe thử âm thanh và chọn nơi yên tĩnh trước khi bắt đầu Listening.</p></div></div>
          <div><span className="placement-icon-tile"><PlacementIcon name="clock" /></span><div><strong>Sắp xếp 120 phút</strong><p>Đồng hồ chạy trong lúc làm bài. Bạn có thể tạm dừng và tiếp tục theo trạng thái hệ thống.</p></div></div>
          <div><span className="placement-icon-tile"><PlacementIcon name="flag" /></span><div><strong>Đánh dấu câu cần xem lại</strong><p>Dùng bảng điều hướng câu hỏi để quay lại trước khi nộp.</p></div></div>
        </div>
      </div>
      <div className="placement-structure"><div className="placement-structure-head"><span className="placement-icon-tile"><PlacementIcon name="layers" size={27} /></span>
        <div><span>Cấu trúc TOEIC</span><h3>7 Part · 200 câu</h3></div></div>
        <div className="placement-structure-group is-listening"><div className="placement-structure-label"><PlacementIcon name="headphones" /><strong>Listening</strong><span>100 câu · Part 1–4</span></div>
          {partStructure.slice(0, 4).map(PartStructureRow)}</div>
        <div className="placement-structure-group is-reading"><div className="placement-structure-label"><PlacementIcon name="book" /><strong>Reading</strong><span>100 câu · Part 5–7</span></div>
          {partStructure.slice(4).map(PartStructureRow)}</div>
      </div>
    </section>
    <section className="placement-parts-overview" aria-labelledby="placement-parts-title"><div className="placement-section-heading"><span>Phạm vi đánh giá</span>
      <h2 id="placement-parts-title">Bảy Part trong một bài kiểm tra</h2></div>
      <div className="placement-parts-overview-grid">{partStructure.map(item => <article key={item.part} className={item.part < 5 ? "is-listening" : "is-reading"}>
        <span className="placement-icon-tile"><PlacementIcon name={item.icon} size={19} /></span><div><strong>Part {item.part}</strong>
          <span>{item.name}</span></div><b>{item.count} câu</b></article>)}</div>
    </section>
  </>;
}

function PartStructureRow(item) {
  return <div className="placement-structure-row" key={item.part}><span>Part {item.part}</span><div aria-hidden="true"><i style={{ width: `${item.count / 54 * 100}%` }} /></div><b>{item.count}</b></div>;
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
    <PlacementVisualSections />
    <section className="placement-public-roadmap" aria-labelledby="public-roadmap-title"><div className="placement-section-heading"><span>Sau bài kiểm tra</span>
      <h2 id="public-roadmap-title">Lộ trình học theo giai đoạn</h2><p>Đây là giới thiệu chung; giai đoạn cá nhân chỉ xuất hiện sau khi phân lớp KNN.</p></div>
      <div className="placement-public-roadmap-grid">{[1, 2, 3].map(stage => <div key={stage}>
        <span className="placement-icon-tile"><PlacementIcon name="route" /></span><small>Giai đoạn {stage}</small>
        <strong>{courseStage[stage].name}</strong><span>{courseEntryReference(stage)}</span>
      </div>)}</div><Link className="outline-button" to="/courses">Khám phá khóa học <PlacementIcon name="arrow" size={17} /></Link>
    </section>
  </div></SiteLayout>;
}

function LearnerPlacementPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const analysisTab = searchParams.get("tab") === "analysis";
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
  }, [token, retryLoad]);

  useEffect(() => {
    if (!analysisTab || !history?.completedCount) return;
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
  }, [analysisTab, history?.completedCount, token, analysisRetry]);

  function showAnalysis() { setSearchParams({ tab: "analysis" }); }

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
  const heroTarget = hasActive ? "#placement-active" : history?.completedCount ? "#placement-latest" : "#placement-start";
  return <SiteLayout><div className="site-container exam-home placement-page">
    <nav className="placement-view-tabs" aria-label="Các thẻ kiểm tra đầu vào">
      <button type="button" aria-current={!analysisTab ? "page" : undefined} onClick={() => setSearchParams({})}>
        <PlacementIcon name="file" size={18} /> Tổng quan</button>
      <button type="button" aria-current={analysisTab ? "page" : undefined} disabled={history && !history.completedCount}
        onClick={showAnalysis}><PlacementIcon name="chart" size={18} /> Phân tích kết quả</button>
    </nav>
    {analysisTab ? <>
      <div className="placement-analysis-tab-header"><div><span className="placement-eyebrow"><PlacementIcon name="chart" size={17} /> Phân tích năng lực</span>
        <h1>Kết quả kiểm tra đầu vào</h1><p>Chỉ hiển thị kết quả hoàn thành gần nhất của bạn.</p></div></div>
      {error && <p className="exam-error" role="alert">{error}</p>}
      {!state && !error && <p role="status">Đang tải kết quả gần nhất…</p>}
      {!state && error && <button className="outline-button" type="button" onClick={() => { setError(""); setRetryLoad(value => value + 1); }}>Thử tải lại</button>}
      {state && history && !history.completedCount && <section className="exam-card placement-analysis-empty"><span className="placement-icon-tile"><PlacementIcon name="chart" /></span>
        <div><h2>Chưa có kết quả để phân tích</h2><p>Hoàn thành bài kiểm tra đầu vào để xem tỷ lệ đúng từng Part và giai đoạn KNN.</p></div>
        <button className="primary-button" type="button" disabled={pending} onClick={start}>Bắt đầu kiểm tra</button></section>}
      {analysisLoading && !result && <p role="status">Đang tải phân tích KNN…</p>}
      {!analysisLoading && !result && error && state && <button className="outline-button" type="button"
        onClick={() => { setError(""); setAnalysisRetry(value => value + 1); }}>Thử tải lại phân tích</button>}
      {result && <PlacementAnalysisView analysis={result} courses={recommendations} target={target}
        onTargetChange={value => { setTarget(value); setTargetFeedback(null); setRecommendations(old => old.map(course =>
          ({ ...course, recommended: false, recommendationOrder: null }))); }}
        onTargetSubmit={confirmTarget} pending={pending} showAttemptHeader targetFeedback={targetFeedback}
        coursesState={coursesState === "idle" && analysisLoading ? "loading" : coursesState}
        onCoursesRetry={reloadCourses} onRetake={hasActive ? undefined : start} retakePending={pending} />}
    </> : <>
    <PlacementHero primary={<a className="primary-button" href={heroTarget}>{hasActive ? "Xem bài đang làm" : history?.completedCount ? "Xem kết quả gần nhất" : "Bắt đầu đánh giá"} <PlacementIcon name="arrow" size={18} /></a>}
      secondary={<a className="outline-button" href="#placement-prep">Xem cách chuẩn bị</a>} />
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && error && <button className="outline-button" type="button" onClick={() => { setError(""); setRetryLoad(value => value + 1); }}>Thử tải lại</button>}
    {!state && !error && <p>Đang tải lộ trình học…</p>}
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
    {history?.completedCount > 0 && <section className="exam-card placement-latest-card" id="placement-latest"><span className="placement-icon-tile"><PlacementIcon name="award" size={27} /></span><div><span className="placement-eyebrow">Kết quả gần nhất</span>
      <h2>{verifiedStage ? `Giai đoạn ${verifiedStage} · KNN đề xuất` : latestStatus === "loading" ? "Đang xác minh giai đoạn KNN…" :
        latestStatus === "error" ? "Chưa thể tải giai đoạn KNN" : "Chưa có kết quả phân lớp KNN"}</h2>
      <p>Hoàn thành {date(history.latestCompletedAt)}</p>
      {latestMetrics?.result?.overall?.total > 0 && <div className="placement-latest-metric"><strong>{latestMetrics.result.overall.correct}/{latestMetrics.result.overall.total} câu đúng</strong>
        <div className="placement-bar" role="progressbar" aria-label="Tỷ lệ câu đúng lần gần nhất"
          aria-valuemin="0" aria-valuemax="100" aria-valuenow={Math.round(100 * latestMetrics.result.overall.correct / latestMetrics.result.overall.total)}>
          <span style={{ width: `${100 * latestMetrics.result.overall.correct / latestMetrics.result.overall.total}%` }} /></div></div>}</div>
      <div className="placement-latest-actions">
        <button className="outline-button" type="button" disabled={analysisLoading} onClick={showAnalysis}>
          {analysisLoading ? "Đang tải…" : "Xem phân tích"}</button>
        {!hasActive && <button className="primary-button" type="button" disabled={pending} onClick={start}>
          {pending ? "Đang bắt đầu…" : "Làm lại kiểm tra"}</button>}
        <Link className="outline-button" to="/courses">Xem lộ trình</Link>
      </div></section>}
    <PlacementVisualSections />
    </>}
  </div></SiteLayout>;
}
