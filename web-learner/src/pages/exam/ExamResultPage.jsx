import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi, placementApi } from "../../services/examApi";
import { examNavigation } from "./examNavigation";
import "../../styles/exam.css";

export default function ExamResultPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.result(attemptId, token).then(data => { if (live) setResult(data); })
      .catch(e => { if (live) setError(e.data?.code === "ATTEMPT_NOT_FINALIZED" ?
        "Bài thi chưa kết thúc." : "Không thể tải kết quả."); });
    return () => { live = false; };
  }, [attemptId, token]);
  const tested = result?.parts.filter(p => p.stats.total > 0) || [];
  const partMock = result?.source === "PART";
  const navigation = examNavigation(result);
  async function retry() {
    if (retrying || result?.source !== "PLACEMENT") return;
    setRetrying(true); setError("");
    try {
      const started = await placementApi.start(token);
      navigate(`/exam/${started.attemptId}`);
    } catch { setError("Không thể bắt đầu lại bài thi."); }
    finally { setRetrying(false); }
  }
  return <SiteLayout><div className="site-container exam-result">
    <div className="page-heading"><span>{result?.source === "PLACEMENT" ? "Lộ trình học" : "Thi thử"} / Kết quả</span>
      <h1>{result?.examName || "Kết quả bài thi"}</h1>
      {result?.examCode && <p>{result.examCode}</p>}</div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!result && !error && <p>Đang tải kết quả…</p>}
    {result && <>
      {partMock ? <section className="exam-score"><p>Kết quả Part {tested[0]?.part}</p>
        <strong>{tested[0]?.stats.correct} / {tested[0]?.stats.total}</strong>
        <p>{tested[0]?.stats.percentage}% câu đúng</p></section> :
        <section className="exam-score"><p>Điểm TOEIC ước tính</p>
          <strong>{result.totalScore ?? "—"} <small>/ 990</small></strong>
          <div className="exam-score-sections"><div>Listening <b>{result.listeningScore ?? "—"} / 495</b>
            <small>{result.listening.correct} / {result.listening.total} câu đúng</small></div>
            <div>Reading <b>{result.readingScore ?? "—"} / 495</b>
              <small>{result.reading.correct} / {result.reading.total} câu đúng</small></div></div></section>}
      <section><h2>Kết quả câu hỏi</h2><div className="exam-stat-grid">
        <div><b>{result.overall.correct} / {result.overall.total}</b><span>Đúng</span></div>
        <div><b>{result.overall.incorrect} / {result.overall.total}</b><span>Sai</span></div>
        <div><b>{result.overall.unanswered} / {result.overall.total}</b><span>Chưa trả lời</span></div>
      </div></section>
      <section><h2>Theo từng Part</h2><div className="exam-part-list">
        {tested.map(row => <div key={row.part}><strong>Part {row.part}</strong>
          <span>{row.stats.correct}/{row.stats.total} đúng</span><span>{row.stats.percentage}%</span></div>)}</div></section>
      <div className="exam-navigation">
        <Link className="outline-button" to={navigation.back}>← Quay lại</Link>
        {result.source === "PLACEMENT" && <Link className="primary-button" to={`/exam/${attemptId}/review`}>Xem lại bài</Link>}
        {result.source === "PLACEMENT" &&
          <button className="outline-button" type="button" disabled={retrying} onClick={retry}>
            Làm lại kiểm tra</button>}
        {navigation.root !== navigation.back && <Link className="outline-button" to={navigation.root}>
          {navigation.rootLabel}</Link>}
      </div>
    </>}
  </div></SiteLayout>;
}
