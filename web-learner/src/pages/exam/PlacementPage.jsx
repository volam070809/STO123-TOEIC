import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import PlacementAnalysisView from "./PlacementAnalysisView";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";
const remaining = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");

export default function PlacementPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState(null);
  const [history, setHistory] = useState(null);
  const [historyRows, setHistoryRows] = useState([]);
  const [historyPage, setHistoryPage] = useState(0);
  const [historyMore, setHistoryMore] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [analysisLoading, setAnalysisLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [recommendations, setRecommendations] = useState([]);
  const [coursesLoaded, setCoursesLoaded] = useState(false);
  const [target, setTarget] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.state(token), placementApi.summary(token)]).then(([current, past]) => {
      if (!live) return;
      setState(current); setHistory(past);
    }).catch(() => { if (live) setError("Không thể tải lộ trình học."); });
    const timer = setInterval(() => setTick(n => n + 1), 1000);
    return () => { live = false; clearInterval(timer); };
  }, [token]);

  async function showAnalysis() {
    if (analysisLoading || result) return;
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

  async function loadHistory() {
    if (historyLoading || (historyPage > 0 && !historyMore)) return;
    setHistoryLoading(true); setError("");
    try {
      const next = historyPage + 1;
      const data = await placementApi.history(token, next);
      setHistoryRows(old => [...old, ...data.items]);
      setHistoryPage(next); setHistoryMore(data.hasMore);
      if (next === 1) requestAnimationFrame(() => document.getElementById("placement-history")?.scrollIntoView({ behavior: "smooth", block: "start" }));
    } catch { setError("Không thể tải lịch sử kiểm tra đầu vào."); }
    finally { setHistoryLoading(false); }
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
      <p>7 Part · tối đa 200 câu · 120 phút · Có thể làm lại để cập nhật kết quả</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && !error && <p>Đang tải lộ trình học…</p>}
    {hasActive && <section className="exam-card placement-action-card"><div><span className="placement-eyebrow">Tiếp tục hành trình</span><h2>Bài kiểm tra đầu vào đang làm</h2>
      <p>7 Part · {state.totalQuestions} câu · Đã trả lời {state.answered} / {state.totalQuestions}</p>
      <p>{state.isPaused ? "Đang tạm dừng · " : ""}Thời gian còn lại: {remaining(state.remainingSeconds)}</p></div>
      <Link className="primary-button" to={`/exam/${state.attemptId}`}>Tiếp tục kiểm tra</Link></section>}
    {!hasActive && state && <section className="exam-card placement-action-card"><div><span className="placement-eyebrow">Đánh giá năng lực</span><h2>{history?.completedCount ? "Kiểm tra lại năng lực" : "Bắt đầu kiểm tra đầu vào"}</h2>
      <p>Kết quả 7 Part giúp đánh giá năng lực và chọn khóa học phù hợp.</p></div>
      <button className="primary-button" type="button" disabled={pending} onClick={start}>
        {history?.completedCount ? "Làm lại kiểm tra đầu vào" : "Bắt đầu kiểm tra đầu vào"}</button></section>}
    {history?.completedCount > 0 && !result && <section className="exam-card placement-latest-card"><div><span className="placement-eyebrow">Lần kiểm tra gần nhất</span><h2>Kết quả gần nhất</h2>
      <p>{date(history.latestCompletedAt)} · {history.currentStage ? `Giai đoạn ${history.currentStage}` : "Chưa có đánh giá trình độ"}</p></div>
      <button className="outline-button" type="button" disabled={analysisLoading} onClick={showAnalysis}>
        {analysisLoading ? "Đang tải…" : "Xem phân tích"}</button></section>}
    {result && <PlacementAnalysisView analysis={result} courses={recommendations} target={target}
      onTargetChange={value => { setTarget(value); setRecommendations(old => old.map(course =>
        ({ ...course, recommended: false, recommendationOrder: null }))); }}
      onTargetSubmit={confirmTarget} pending={pending} showAttemptHeader
      coursesLoaded={coursesLoaded && !analysisLoading} />}
    {result?.stage && !coursesLoaded && !analysisLoading &&
      <button type="button" className="outline-button" onClick={reloadCourses}>Thử tải lại khóa học</button>}
    {history?.completedCount > 0 && <section id="placement-history" className="exam-card placement-history-card"><div><h2>Lịch sử kiểm tra đầu vào</h2>
      <p>Đã thực hiện {history.completedCount} lần · Lần gần nhất: {date(history.latestCompletedAt)}</p>
      <p>Trình độ hiện tại: {history.currentStage ? `Giai đoạn ${history.currentStage}` : "Chưa có đánh giá trình độ"}</p>
      <button className="outline-button" type="button" disabled={historyLoading || historyPage > 0} onClick={loadHistory}>
        {historyLoading ? "Đang tải…" : historyPage ? "Lịch sử đã mở" : "Xem lịch sử"}</button>
      {historyPage > 0 && <div className="placement-history-list">{historyRows.map(row =>
        <div key={row.attemptId}><span><strong>Lần {row.number}</strong> · {date(row.completedAt)}
          {row.stage ? ` · Giai đoạn ${row.stage}` : " · Chưa lưu kết quả phân loại"}</span>
          <span className="action-row"><Link to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
            <Link to={`/exam/${row.attemptId}/review`}>Xem lại bài</Link></span></div>)}
        {historyMore && <button className="outline-button" type="button" disabled={historyLoading} onClick={loadHistory}>
          {historyLoading ? "Đang tải…" : "Xem thêm lần kiểm tra"}</button>}</div>}</div></section>}
    <section><Link className="outline-button" to="/courses">Khám phá tất cả khóa học</Link></section>
  </div></SiteLayout>;
}
