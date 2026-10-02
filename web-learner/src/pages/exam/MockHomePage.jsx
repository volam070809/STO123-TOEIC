import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const timeLeft = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");
export default function MockHomePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [history, setHistory] = useState(null);
  const [active, setActive] = useState(null);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const [showParts, setShowParts] = useState(false);
  const [, setTick] = useState(0);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    Promise.all([examApi.history(token), examApi.active(token)]).then(([past, current]) => {
      if (live) { setHistory(past); setActive(current); }
    }).catch(() => { if (live) setError("Không thể tải thông tin thi thử."); });
    const timer = setInterval(() => setTick(n => n + 1), 1000);
    return () => { live = false; clearInterval(timer); };
  }, [token]);
  async function start(source, examId = null, part = null) {
    if (starting.current) return;
    starting.current = true; setPending(true); setError("");
    try {
      const result = await examApi.start(source, examId, token, part);
      navigate(`/exam/${result.attemptId}`);
    } catch (e) { setError(e.data?.message || "Không thể bắt đầu bài thi. Vui lòng thử lại."); }
    finally { starting.current = false; setPending(false); }
  }
  const random = history?.attempts.filter(row => row.mode === "RANDOM") || [];
  const byPart = Array.from({ length: 7 }, (_, i) => ({ part: i + 1,
    rows: history?.attempts.filter(row => row.mode === "PART" && row.part === i + 1) || [] }))
    .filter(group => group.rows.length);
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Học tập</span><h1>Thi thử TOEIC</h1>
      <p>Chọn đề soạn sẵn, đề ngẫu nhiên toàn bài hoặc thi theo từng Part.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {active && <section className="exam-score"><h2>Bài đang làm</h2><h3>{active.name}</h3>
      <p>Bạn đang có một bài thi chưa hoàn thành.</p>
      <p>{active.part ? `Part ${active.part}` : "7 Part"} · {active.totalQuestions} câu · Đã trả lời {active.answered}/{active.totalQuestions}</p>
      <p>{active.isPaused ? "Đang tạm dừng · " : ""}Thời gian còn lại: <strong>{timeLeft(active.remainingSeconds)}</strong></p>
      <Link className="primary-button" to={`/exam/${active.attemptId}`}>Tiếp tục làm bài</Link></section>}
    <section><h2>Chọn hình thức thi</h2><div className="mock-mode-grid">
      <article className="mock-mode-card"><span>01 · Đề soạn sẵn</span><h3>Đề soạn sẵn</h3>
        <p>Các bộ đề TOEIC được hệ thống chuẩn bị.</p>
        <Link className="outline-button" to="/mock-test/fixed">Xem danh sách đề</Link></article>
      <article className="mock-mode-card"><span>02 · Toàn bài</span><h3>Đề ngẫu nhiên</h3>
        <p>Tạo bài thi 7 Part từ ngân hàng câu hỏi.</p>
        <button className="primary-button" type="button" disabled={pending}
          onClick={() => start("RANDOM")}>Bắt đầu thi</button></article>
      <article className="mock-mode-card"><span>03 · Theo Part</span><h3>Thi theo Part</h3>
        <p>Kiểm tra riêng từng TOEIC Part.</p>
        <button className="outline-button" type="button" onClick={() => setShowParts(!showParts)}>Chọn Part</button></article>
    </div>
      {showParts && <div className="mock-choice-panel"><h3>Chọn Part để thi thử</h3>
        <button className="outline-button" type="button" onClick={() => setShowParts(false)}>← Quay lại chọn hình thức</button>
        <div className="mock-part-grid">{Array.from({ length: 7 }, (_, index) => <button key={index}
          className="outline-button" type="button" disabled={pending}
          onClick={() => start("PART", null, index + 1)}>Part {index + 1}</button>)}</div></div>}
    </section>
    <section><h2>Lịch sử / thống kê</h2>
      {!history && !error && <p>Đang tải lịch sử…</p>}
      {history && <div className="exam-card-list">
        <article className="exam-card"><div><h3>Đề soạn sẵn</h3>
          <p>{history.fixedExams.reduce((sum, exam) => sum + exam.completedAttempts, 0)} lượt đã thi</p></div>
          <Link className="outline-button" to="/mock-test/fixed">Xem danh sách đề</Link></article>
        {random.length > 0 && <article className="exam-card"><div><h3>Đề ngẫu nhiên toàn bài</h3>
          <p>Đã thi {random.length} lần · Điểm cao nhất: {Math.max(...random.map(row => row.totalScore ?? 0))}/990</p>
          <p>Lần gần nhất: {random[0].totalScore ?? "—"}/990</p>
          <Link to="/mock-test/history/random">Xem lịch sử</Link></div></article>}
        {byPart.map(group => <article className="exam-card" key={group.part}><div><h3>Part {group.part}</h3>
          <p>Đã thi {group.rows.length} lần · Tốt nhất: {Math.max(...group.rows.map(row => row.percentage ?? 0))}%</p>
          <p>Lần gần nhất: {group.rows[0].correct}/{group.rows[0].totalQuestions} đúng · {group.rows[0].percentage}%</p>
          <Link to={`/mock-test/history/part/${group.part}`}>Xem lịch sử Part {group.part}</Link></div></article>)}
      </div>}
    </section>
  </div></SiteLayout>;
}
