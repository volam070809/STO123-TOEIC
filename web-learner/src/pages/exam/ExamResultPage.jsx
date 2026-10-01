import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

export default function ExamResultPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const [result, setResult] = useState(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.result(attemptId, token).then(data => { if (live) setResult(data); })
      .catch(e => { if (live) setError(e.data?.code === "ATTEMPT_NOT_FINALIZED" ?
        "Bài thi chưa kết thúc." : "Không thể tải kết quả."); });
    return () => { live = false; };
  }, [attemptId, token]);
  return <SiteLayout><div className="site-container exam-result">
    <div className="page-heading"><span>{result?.source === "PLACEMENT" ? "Phân lớp" : "Thi thử"} / Kết quả</span><h1>{result?.examName || "Kết quả bài thi"}</h1></div>
    {error && <p className="exam-error" role="alert">{error} <Link to={`/exam/${attemptId}`}>Mở bài thi</Link></p>}
    {!result && !error && <p>Đang tải kết quả…</p>}
    {result && <>
      <section className="exam-score"><p>Điểm TOEIC ước tính</p>
        <strong>{result.totalScore ?? "—"} <small>/ 990</small></strong>
        <div className="exam-score-sections"><div>Listening <b>{result.listeningScore ?? "—"} / 495</b>
          <small>Raw: {result.listening.correct} / {result.listening.total} câu đúng</small></div>
          <div>Reading <b>{result.readingScore ?? "—"} / 495</b>
            <small>Raw: {result.reading.correct} / {result.reading.total} câu đúng</small></div></div></section>
      <section><h2>Kết quả câu hỏi</h2><div className="exam-stat-grid">
        <div><b>{result.overall.correct}</b><span>Đúng</span></div>
        <div><b>{result.overall.incorrect}</b><span>Sai</span></div>
        <div><b>{result.overall.unanswered}</b><span>Bỏ trống</span></div>
        <div><b>{Math.floor(result.timeUsedSeconds / 60)} phút</b><span>Thời gian làm bài</span></div>
      </div></section>
      <section><h2>Theo từng Part</h2><div className="exam-part-list">
        {result.parts.map(row => <div key={row.part}><strong>Part {row.part}</strong><span>{row.stats.correct}/{row.stats.total} đúng</span>
          <span>{row.stats.percentage}%</span></div>)}</div></section>
      <div className="action-row"><Link className="primary-button" to={`/exam/${attemptId}/review`}>Xem lại bài</Link>
        <Link className="outline-button" to={result.source === "PLACEMENT" ? "/placement" : "/mock-test"}>
          {result.source === "PLACEMENT" ? "Phân lớp" : "Thi thử"}</Link></div>
    </>}
  </div></SiteLayout>;
}
