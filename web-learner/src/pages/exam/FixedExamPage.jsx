import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleDateString("vi-VN") : "—";

export default function FixedExamPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [exams, setExams] = useState(null);
  const [expanded, setExpanded] = useState(null);
  const [page, setPage] = useState(1);
  const [attempts, setAttempts] = useState([]);
  const [hasMore, setHasMore] = useState(false);
  const [loadingAttempts, setLoadingAttempts] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.fixedSummary(token).then(data => { if (live) setExams(data); })
      .catch(() => { if (live) setError("Không thể tải danh sách đề và lịch sử."); });
    return () => { live = false; };
  }, [token]);
  useEffect(() => {
    if (expanded == null) return;
    let live = true;
    examApi.history(token, { mode: "FIXED", examId: expanded, page }).then(data => {
      if (!live) return;
      setAttempts(old => page === 1 ? data.items : [...old, ...data.items]);
      setHasMore(data.hasMore);
      setLoadingAttempts(false);
    }).catch(() => { if (live) { setError("Không thể tải các lượt thi của đề này."); setLoadingAttempts(false); } });
    return () => { live = false; };
  }, [expanded, page, token]);
  async function start(examId) {
    if (pending) return;
    setPending(true); setError("");
    try {
      const row = await examApi.start("FIXED", examId, token);
      navigate(`/exam/${row.attemptId}`);
    } catch (e) { setError(e.data?.message || "Không thể bắt đầu đề thi."); }
    finally { setPending(false); }
  }
  function toggle(examId) {
    if (expanded === examId) { setExpanded(null); return; }
    setAttempts([]); setHasMore(false); setPage(1); setError(""); setLoadingAttempts(true); setExpanded(examId);
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Thi thử / Đề soạn sẵn</span><h1>Đề soạn sẵn</h1>
      <p>Chọn đề để làm bài hoặc xem mọi lượt thi trước đây của từng đề.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!exams && !error && <p role="status">Đang tải danh sách đề…</p>}
    {exams?.length === 0 && <p>Chưa có đề soạn sẵn.</p>}
    <div className="exam-card-list">{exams?.map(exam => <article className="exam-card fixed-exam-card" key={exam.examId}><div>
      <h2>{exam.examName}</h2><p>200 câu · {exam.duration} phút · {exam.completedAttempts} lượt đã hoàn thành</p>
      {exam.bestScore != null && <p>Điểm cao nhất: {exam.bestScore}/990</p>}
      {exam.activeAttemptId && <p><Link to={`/exam/${exam.activeAttemptId}`}>Tiếp tục bài chưa hoàn thành</Link></p>}
      {expanded === exam.examId && <div className="fixed-attempts">
        {loadingAttempts && <p role="status">Đang tải các lượt thi…</p>}
        {!loadingAttempts && attempts.length === 0 && <p>Chưa có lượt thi đã hoàn thành.</p>}
        {attempts.map(row => <div className="fixed-attempt-row" key={row.attemptId}>
          <span>{date(row.startedAt)} · {row.totalScore ?? "—"}/990</span>
          <div className="action-row"><Link to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
            <Link to={`/exam/${row.attemptId}/review`}>Xem lại bài</Link></div></div>)}
        {hasMore && <button className="outline-button" type="button" disabled={loadingAttempts}
          onClick={() => { setLoadingAttempts(true); setPage(value => value + 1); }}>Tải thêm</button>}
      </div>}
    </div><div className="exam-card-actions">
      {exam.examStatus === "OPEN" && exam.duration === 120 && <button className="primary-button" type="button" disabled={pending}
        onClick={() => start(exam.examId)}>{exam.activeAttemptId ? "Tiếp tục" : "Bắt đầu thi"}</button>}
      <button className="outline-button" type="button" onClick={() => toggle(exam.examId)}>
        {expanded === exam.examId ? "Ẩn các lượt thi" : "Xem các lượt thi"}</button>
    </div></article>)}</div>
  </div></SiteLayout>;
}
