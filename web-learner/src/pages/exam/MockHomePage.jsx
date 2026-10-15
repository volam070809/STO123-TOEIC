import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

export default function MockHomePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [exams, setExams] = useState(null);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(null);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    examApi.catalog(token).then(data => { if (live) setExams(data); })
      .catch(() => { if (live) setError("Không thể tải danh sách đề thi thử."); });
    return () => { live = false; };
  }, [token]);
  async function start(examId) {
    if (starting.current) return;
    starting.current = true; setPending(examId); setError("");
    if (import.meta.env.DEV) performance.mark("mock-start-click");
    try {
      const row = await examApi.start(examId, token);
      navigate(`/exam/${row.attemptId}`);
    } catch (e) {
      if (import.meta.env.DEV) performance.clearMarks("mock-start-click");
      setError(e.data?.message || "Không thể bắt đầu đề thi.");
    }
    finally { starting.current = false; setPending(null); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Học tập</span><h1>Thi thử TOEIC</h1>
      <p>Chọn một đề thi để bắt đầu. Mỗi đề chỉ được hoàn thành một lần.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/">← Quay lại</Link>
      <Link className="outline-button" to="/mock-test/history">Lịch sử thi</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!exams && !error && <p role="status">Đang tải danh sách đề…</p>}
    {exams?.length === 0 && <p>Chưa có đề thi thử đang mở.</p>}
    <div className="exam-card-list">{exams?.map(exam => <article className="exam-card fixed-exam-card" key={exam.examId}>
      <div><span className="mock-exam-code">{exam.examCode}</span><h2>{exam.examName}</h2>
        <p className="mock-exam-meta"><span>200 câu</span><span>{exam.duration} phút</span></p>
        {exam.state === "COMPLETED" && <p className="mock-exam-status is-complete">Đã hoàn thành · {exam.totalScore ?? "—"}/990</p>}
        {exam.state === "IN_PROGRESS" && <p className="mock-exam-status is-progress">Đang làm</p>}
        {exam.state === "NOT_STARTED" && <p className="mock-exam-status">Chưa bắt đầu</p>}</div>
      <div className="exam-card-actions">
        {exam.state === "NOT_STARTED" && <button className="primary-button" type="button"
          disabled={pending !== null} onClick={() => start(exam.examId)}>
          {pending === exam.examId ? "Đang bắt đầu…" : "Bắt đầu thi"}</button>}
        {exam.state === "IN_PROGRESS" && <Link className="primary-button" to={`/exam/${exam.attemptId}`}>
          Tiếp tục làm bài</Link>}
        {exam.state === "COMPLETED" && <Link className="outline-button" to={`/exam/${exam.attemptId}/result`}>
          Xem kết quả</Link>}
      </div>
    </article>)}</div>
  </div></SiteLayout>;
}
