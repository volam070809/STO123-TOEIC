import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";
import "../../styles/learner-journey.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";

export default function MockHistoryDetailPage() {
  const { token } = useAuth();
  const [response, setResponse] = useState(null);
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.history(token, { page }).then(data => {
      if (live) setResponse(old => page === 1 ? data : { ...data, items: [...(old?.items || []), ...data.items] });
    }).catch(() => { if (live) setError("Không thể tải lịch sử thi thử."); });
    return () => { live = false; };
  }, [page, token, retry]);
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span><Link to="/mock-test">Thi thử</Link> / Lịch sử</span><h1>Lịch sử thi thử</h1>
      <p>Điểm và thống kê của các đề đã hoàn thành.</p></div>
    {error && <div className="exam-error" role="alert"><p>{error}</p><button type="button" className="outline-button" onClick={() => { setError(""); setPage(1); setResponse(null); setRetry(value => value + 1); }}>Thử lại</button></div>}
    {!response && !error && <p role="status">Đang tải lịch sử…</p>}
    {response?.items.length === 0 && <div className="mock-history-empty"><h2>Chưa có kết quả thi thử</h2><p>Hoàn thành một đề thi để xem kết quả gần nhất tại đây.</p><Link className="primary-button" to="/mock-test">Chọn đề thi →</Link></div>}
    <div className="mock-history-grid mock-history-journey">{response?.items.map(row => <article className="mock-history-card" key={row.attemptId}>
      <div><small>{row.completedAt ? "HOÀN THÀNH" : "BẮT ĐẦU"} {date(row.completedAt || row.startedAt)}</small><h2>{row.name}</h2></div>
      <p><strong>{row.totalScore ?? "—"}</strong><span> / 990 điểm TOEIC ước tính</span></p>
      <Link className="outline-button" to={`/exam/${row.attemptId}/result`}>Xem kết quả →</Link>
    </article>)}</div>
    {response?.hasMore && <button type="button" className="outline-button" onClick={() => setPage(x => x + 1)}>
      Xem thêm</button>}
  </div></SiteLayout>;
}
