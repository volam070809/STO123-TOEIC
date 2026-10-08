import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";

export default function MockHistoryDetailPage() {
  const { token } = useAuth();
  const [response, setResponse] = useState(null);
  const [page, setPage] = useState(1);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.history(token, { page }).then(data => {
      if (live) setResponse(old => page === 1 ? data : { ...data, items: [...(old?.items || []), ...data.items] });
    }).catch(() => { if (live) setError("Không thể tải lịch sử thi thử."); });
    return () => { live = false; };
  }, [page, token]);
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Thi thử / Lịch sử</span><h1>Lịch sử thi thử</h1>
      <p>Điểm và thống kê của các đề đã hoàn thành.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!response && !error && <p role="status">Đang tải lịch sử…</p>}
    {response?.items.length === 0 && <p>Chưa có bài thi đã hoàn thành.</p>}
    <div className="mock-history-grid">{response?.items.map(row => <article className="mock-history-card" key={row.attemptId}>
      <p>{row.examCode || "Bài thi cũ"} · Lượt thi #{row.attemptId}</p><h2>{row.name}</h2>
      <p>{date(row.startedAt)}</p><p>Điểm TOEIC: {row.totalScore ?? "—"} / 990</p>
      <Link className="outline-button" to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
    </article>)}</div>
    {response?.hasMore && <button type="button" className="outline-button" onClick={() => setPage(x => x + 1)}>
      Xem thêm</button>}
  </div></SiteLayout>;
}
