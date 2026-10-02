import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";
const status = { DA_NOP: "Đã nộp", HET_GIO: "Hết giờ" };
export default function MockHistoryDetailPage({ kind }) {
  const { part } = useParams();
  const { token } = useAuth();
  const [rows, setRows] = useState(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.history(token).then(data => {
      if (!live) return;
      const filtered = kind === "random" ? data.attempts.filter(row => row.mode === "RANDOM") :
        kind === "part" && Number(part) >= 1 && Number(part) <= 7 ?
          data.attempts.filter(row => row.mode === "PART" && row.part === Number(part)) : null;
      if (filtered) setRows(filtered);
      else setError("Không tìm thấy loại lịch sử thi.");
    }).catch(() => { if (live) setError("Không thể tải lịch sử thi."); });
    return () => { live = false; };
  }, [kind, part, token]);
  const isPart = kind === "part";
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span><Link to="/mock-test">Thi thử</Link> / Lịch sử</span>
      <h1>{isPart ? `Lịch sử thi Part ${part}` : "Lịch sử đề ngẫu nhiên toàn bài"}</h1></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!rows && !error && <p>Đang tải lịch sử…</p>}
    {rows?.length === 0 && <p>Chưa có bài thi đã kết thúc.</p>}
    <div className="mock-history-grid">{rows?.map((row, index) => <article className="mock-history-card" key={row.attemptId}>
      <h2>Lần {rows.length - index}</h2><p>{date(row.startedAt)} · {status[row.status]}</p>
      {isPart ? <p>{row.correct} / {row.totalQuestions} câu đúng · {row.percentage}%</p> :
        <p>{row.totalQuestions} câu · Điểm TOEIC ước tính: {row.totalScore ?? "—"} / 990</p>}
      <Link className="outline-button" to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
    </article>)}</div>
  </div></SiteLayout>;
}
