import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { courseStage } from "./coursePresentation";
import "../../styles/exam.css";

const levels = { NEEDS_IMPROVEMENT: "Cần cải thiện", DEVELOPING: "Đang phát triển", GOOD: "Tốt" };
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
      if (outcome.targetScore) {
        const courses = await placementApi.courses(token);
        setRecommendations(courses.filter(c => c.recommended));
      }
    } catch (e) { setError(e.data?.message || "Không thể tải phân tích kết quả."); }
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
    try {
      const started = await placementApi.start(token);
      navigate(`/exam/${started.attemptId}`);
    } catch (e) { setError(e.data?.message || "Không thể bắt đầu bài kiểm tra đầu vào."); }
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
      setRecommendations(courses.filter(c => c.recommended));
    } catch (e) { setError(e.data?.message || "Không thể lưu điểm mục tiêu."); }
    finally { setPending(false); }
  }

  const hasActive = state?.status === "DANG_LAM" || state?.status === "BO_DO";
  const summary = result?.summary;
  const strongest = result?.parts?.find(p => p.part === summary?.strongestPart);
  const lowest = result?.parts?.find(p => p.part === summary?.weakestPart);
  const strong = result?.parts?.filter(p => p.percentage >= 65).map(p => `Part ${p.part}`) || [];
  const improve = result?.parts?.filter(p => p.percentage < 40).map(p => `Part ${p.part}`) || [];
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Lộ trình học</span><h1>Kiểm tra đầu vào và lộ trình học</h1>
      <p>7 Part · tối đa 200 câu · 120 phút · Có thể làm lại để cập nhật kết quả</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && !error && <p>Đang tải lộ trình học…</p>}
    {hasActive && <section className="exam-card"><div><h2>Bài kiểm tra đầu vào đang làm</h2>
      <p>7 Part · {state.totalQuestions} câu · Đã trả lời {state.answered} / {state.totalQuestions}</p>
      <p>{state.isPaused ? "Đang tạm dừng · " : ""}Thời gian còn lại: {remaining(state.remainingSeconds)}</p></div>
      <Link className="primary-button" to={`/exam/${state.attemptId}`}>Tiếp tục kiểm tra</Link></section>}
    {!hasActive && state && <section className="exam-card"><div><h2>{history?.completedCount ? "Kiểm tra lại năng lực" : "Bắt đầu kiểm tra đầu vào"}</h2>
      <p>Kết quả 7 Part giúp đánh giá năng lực và chọn khóa học phù hợp.</p></div>
      <button className="primary-button" type="button" disabled={pending} onClick={start}>
        {history?.completedCount ? "Làm lại kiểm tra đầu vào" : "Bắt đầu kiểm tra đầu vào"}</button></section>}
    {history?.completedCount > 0 && !result && <section className="exam-card"><div><h2>Kết quả gần nhất</h2>
      <p>{date(history.latestCompletedAt)} · {history.currentStage ? `Giai đoạn ${history.currentStage}` : "Chưa có đánh giá trình độ"}</p></div>
      <button className="outline-button" type="button" disabled={analysisLoading} onClick={showAnalysis}>
        {analysisLoading ? "Đang tải…" : "Xem phân tích"}</button></section>}
    {result && <>
      <section id="placement-analysis" className="exam-score"><h2>Kết quả kiểm tra đầu vào</h2>
        <p>{date(result.result.finishedAt)} · {result.result.overall.correct} / {result.result.overall.total} câu đúng</p>
        <Link className="outline-button" to={`/exam/${result.result.attemptId}/review`}>Xem lại bài</Link></section>
      <section><h2>Tổng quan năng lực</h2><div className="placement-summary">
        <div><span>Tỷ lệ năng lực trung bình</span><strong>{summary.overallPercentage}%</strong></div>
        <div><span>Listening</span><strong>{summary.listeningPercentage}%</strong></div>
        <div><span>Reading</span><strong>{summary.readingPercentage}%</strong></div>
      </div><p className="exam-card-note">Các tỷ lệ này là đánh giá nội bộ của STO123, không phải điểm TOEIC chính thức.</p></section>
      <section><h2>Listening / Reading</h2><div className="placement-summary">
        {[{ name: "Listening", value: summary.listeningPercentage }, { name: "Reading", value: summary.readingPercentage }]
          .map(item => <div key={item.name}><strong>{item.name} · {item.value}%</strong>
            <div className="placement-average"><span style={{ width: `${item.value}%` }} /></div></div>)}</div></section>
      <section><h2>Kết quả P1–P7</h2><div className="placement-parts">{result.parts.map(part =>
        <div key={part.part}><div className="placement-part-label"><strong>Part {part.part}</strong>
          <span>{part.correct}/{part.total} đúng · {part.percentage}% · {levels[part.level]}</span></div>
          <div className="placement-bar"><span className={part.level.toLowerCase()} style={{ width: `${part.percentage}%` }} /></div>
        </div>)}</div></section>
      <section><h2>Part mạnh / Part cần cải thiện</h2>
        <p>Mạnh nhất: Part {strongest.part} · {strongest.percentage}%</p>
        <p>{lowest.percentage < 40 ? "Cần cải thiện nhất" : "Kết quả thấp nhất hiện tại"}: Part {lowest.part} · {lowest.percentage}%</p>
        {!improve.length && <p>Không có Part nào nằm trong nhóm cần cải thiện nghiêm trọng.</p>}
        <p>{strong.length ? `Bạn có kết quả tốt ở ${strong.join(" và ")}. ` : ""}
          {improve.length ? `${improve.join(" và ")} nên được ưu tiên cải thiện. ` : ""}
          {summary.listeningPercentage > summary.readingPercentage ? "Listening hiện tốt hơn Reading." :
            summary.readingPercentage > summary.listeningPercentage ? "Reading hiện tốt hơn Listening." :
              "Listening và Reading hiện cân bằng."}</p></section>
      <section className="exam-score"><h2>Trình độ hiện tại</h2>
        <strong>{result.stage ? `Giai đoạn ${result.stage}` : "Chưa thể xác định giai đoạn"}</strong>
        <p>{result.stage ? `Năng lực hiện tại phù hợp với ${courseStage[result.stage]?.name}.` :
          "Kết quả bài làm đã được lưu; đề xuất hợp lệ trước đó không bị thay đổi."}</p></section>
      {result.stage && <section><h2>Mục tiêu TOEIC của bạn</h2>
        <p>Chọn số điểm bạn muốn hướng tới để hệ thống đề xuất khóa học phù hợp.</p>
        <form className="exam-target-form" onSubmit={confirmTarget}>
          <label htmlFor="placement-target">Điểm TOEIC mục tiêu</label>
          <input id="placement-target" type="number" min="10" max="990" required value={target}
            onChange={event => { setTarget(event.target.value); setRecommendations([]); }} />
          <button className="primary-button" type="submit" disabled={pending}>Xem khóa học đề xuất</button>
        </form></section>}
      {result.stage && result.targetScore != null && recommendations.length > 0 && <section>
        <h2>{recommendations.length === 1 ? "Khóa học đề xuất" : "Lộ trình khóa học đề xuất"}</h2>
        <div className="learning-path">{recommendations.map((course, index) => <article className="learning-path-step" key={course.courseId}>
          <span className="learning-path-number" aria-hidden="true">{index + 1}</span>
          <div className="learning-path-content">
            <div className="learning-path-labels"><span className="small-badge">Giai đoạn {course.stage}</span>
              {course.stage === result.stage && <span className="learning-path-indicator">Trình độ hiện tại</span>}
              {index === recommendations.length - 1 && <span className="learning-path-indicator">Hướng tới mục tiêu</span>}</div>
            <h3>{course.name}</h3>
            <p>Khoảng điểm TOEIC: {courseStage[course.stage]?.range || "Theo trình độ của bạn"}</p>
            <p>{course.stage === result.stage ? "Phù hợp để bắt đầu từ trình độ hiện tại." :
              index === recommendations.length - 1 ? `Giúp hướng tới mục tiêu ${result.targetScore} điểm.` :
                "Bước tiếp theo trong lộ trình đề xuất."}</p>
            <Link className="outline-button" to={`/courses/${course.courseId}`}>Xem khóa học</Link>
          </div>
        </article>)}</div></section>}
    </>}
    {history?.completedCount > 0 && <section id="placement-history" className="exam-card"><div><h2>Lịch sử kiểm tra đầu vào</h2>
      <p>Đã thực hiện {history.completedCount} lần · Lần gần nhất: {date(history.latestCompletedAt)}</p>
      <p>Trình độ hiện tại: {history.currentStage ? `Giai đoạn ${history.currentStage}` : "Chưa có đánh giá trình độ"}</p>
      <button className="outline-button" type="button" disabled={historyLoading || historyPage > 0} onClick={loadHistory}>
        {historyLoading ? "Đang tải…" : historyPage ? "Lịch sử đã mở" : "Xem lịch sử"}</button>
      {historyPage > 0 && <div className="placement-history-list">{historyRows.map(row =>
        <div key={row.attemptId}><span>Lần {row.number} · {date(row.completedAt)}
          {row.stage ? ` · Giai đoạn ${row.stage}` : " · Chưa lưu kết quả phân loại"}</span>
          <span className="action-row"><Link to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
            <Link to={`/exam/${row.attemptId}/review`}>Xem lại bài</Link></span></div>)}
        {historyMore && <button className="outline-button" type="button" disabled={historyLoading} onClick={loadHistory}>
          {historyLoading ? "Đang tải…" : "Xem thêm lần kiểm tra"}</button>}</div>}</div></section>}
    <section><Link className="outline-button" to="/courses">Khám phá tất cả khóa học</Link></section>
  </div></SiteLayout>;
}
