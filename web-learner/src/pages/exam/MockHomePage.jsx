import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const clock = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");

export default function MockHomePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    examApi.summary(token).then(data => { if (live) setSummary(data); })
      .catch(() => { if (live) setError("Không thể tải tóm tắt thi thử."); });
    return () => { live = false; };
  }, [token]);
  async function startRandom() {
    if (starting.current) return;
    starting.current = true; setPending(true); setError("");
    try {
      const row = await examApi.start("RANDOM", null, token);
      navigate(`/exam/${row.attemptId}`);
    } catch (e) { setError(e.data?.message || "Không thể bắt đầu bài thi."); }
    finally { starting.current = false; setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Học tập</span><h1>Thi thử TOEIC</h1>
      <p>Chọn đề soạn sẵn, đề ngẫu nhiên toàn bài hoặc thi theo từng Part.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!summary && !error && <p role="status">Đang tải tóm tắt thi thử…</p>}
    {summary?.active && <section className="exam-card"><div><h2>Tiếp tục bài thi</h2>
      <p>{summary.active.name} · {summary.active.answered}/{summary.active.totalQuestions} câu · Còn {clock(summary.active.remainingSeconds)}</p></div>
      <Link className="primary-button" to={`/exam/${summary.active.attemptId}`}>Tiếp tục</Link></section>}
    <section className="mock-mode-grid" aria-label="Hình thức thi thử">
      <article className="mock-mode-card"><span>01</span><h2>Đề soạn sẵn</h2><p>Chọn bộ đề TOEIC do STO123 chuẩn bị.</p>
        <Link className="outline-button" to="/mock-test/fixed">Xem danh sách đề</Link></article>
      <article className="mock-mode-card"><span>02</span><h2>Đề ngẫu nhiên toàn bài</h2><p>Thi đầy đủ 7 Part với bộ câu hỏi được sinh mới.</p>
        <button className="primary-button" type="button" disabled={pending} onClick={startRandom}>Sinh đề tự động</button></article>
      <article className="mock-mode-card"><span>03</span><h2>Thi theo Part</h2><p>Chọn riêng Part 1–7 để kiểm tra.</p>
        <Link className="outline-button" to="/mock-test/parts">Chọn Part</Link></article>
    </section>
    <section className="exam-card"><div><h2>Lịch sử</h2>
      <p>{summary ? `${summary.completedCount} lượt thi đã hoàn thành` : "Xem các lượt thi đã hoàn thành"}</p></div>
      <Link className="outline-button" to="/mock-test/history">Xem lịch sử</Link></section>
  </div></SiteLayout>;
}
