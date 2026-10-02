import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleDateString("vi-VN") : "—";
const status = { DA_NOP: "Đã nộp", HET_GIO: "Hết giờ" };
const remaining = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");
export default function FixedExamPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [history, setHistory] = useState(null);
  const [active, setActive] = useState(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    Promise.all([examApi.history(token), examApi.active(token)]).then(([past, current]) => {
      if (live) { setHistory(past); setActive(current); }
    }).catch(() => { if (live) setError("Không thể tải đề soạn sẵn."); });
    const timer = setInterval(() => setTick(n => n + 1), 1000);
    return () => { live = false; clearInterval(timer); };
  }, [token]);
  async function start(examId) {
    if (pending) return;
    setPending(true); setError("");
    try {
      const started = await examApi.start("FIXED", examId, token);
      navigate(`/exam/${started.attemptId}`);
    } catch (e) { setError(e.data?.message || "Không thể bắt đầu đề thi."); }
    finally { setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span><Link to="/mock-test">Thi thử</Link> / Đề soạn sẵn</span>
      <h1>Đề soạn sẵn</h1><p>Mỗi bộ đề được hiển thị một lần cùng kết quả các lần thi.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!history && !error && <p>Đang tải đề thi…</p>}
    {active && <p className="exam-card-note">Bạn đang có một bài thi chưa hoàn thành: <Link to={`/exam/${active.attemptId}`}>Tiếp tục làm bài</Link>.</p>}
    <div className="exam-card-list">{history?.fixedExams.map(exam => {
      const rows = history.attempts.filter(row => row.examId === exam.examId);
      const ownActive = active?.examId === exam.examId ? active : null;
      return <article className="exam-card" key={exam.examId}><div><h2>{exam.examName}</h2>
        <p>200 câu · 120 phút</p>
        {exam.completedAttempts ? <p>Đã thi: {exam.completedAttempts} lần · Điểm cao nhất: {exam.bestScore ?? "—"}/990 · Lần gần nhất: {exam.latestScore ?? "—"}/990</p> :
          <p>Chưa thi lần nào</p>}
        {ownActive && <p>{ownActive.isPaused ? "Đang tạm dừng" : "Bài đang làm"} · Đã trả lời {ownActive.answered}/{ownActive.totalQuestions} · Còn {remaining(ownActive.remainingSeconds)}</p>}
        {rows.length > 0 && <details><summary>Xem các lần thi</summary><div className="placement-history-list">
          {rows.map((row, index) => <div key={row.attemptId}><span>Lần {rows.length - index} · {date(row.startedAt)} · {status[row.status]} · {row.totalScore ?? "—"}/990</span>
            <Link to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link></div>)}</div></details>}
      </div><div className="exam-card-actions">
        {ownActive ? <Link className="primary-button" to={`/exam/${ownActive.attemptId}`}>Tiếp tục làm bài</Link> :
          <button className="primary-button" type="button" disabled={pending || exam.examStatus !== "OPEN"}
            onClick={() => start(exam.examId)}>{exam.completedAttempts ? "Làm lại đề" : "Bắt đầu thi"}</button>}
      </div></article>;
    })}</div>
  </div></SiteLayout>;
}
