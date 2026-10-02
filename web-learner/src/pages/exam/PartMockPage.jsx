import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const clock = value => value == null ? "—" : [Math.floor(value / 3600), Math.floor(value % 3600 / 60), value % 60]
  .map(n => String(n).padStart(2, "0")).join(":");

export default function PartMockPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [parts, setParts] = useState(null);
  const [error, setError] = useState("");
  const [pendingPart, setPendingPart] = useState(null);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    examApi.parts(token).then(data => { if (live) setParts(data); })
      .catch(() => { if (live) setError("Không thể tải trạng thái từng Part."); });
    return () => { live = false; };
  }, [token]);
  async function start(part) {
    if (starting.current) return;
    starting.current = true; setPendingPart(part); setError("");
    try {
      const row = await examApi.start("PART", null, token, part);
      navigate(`/exam/${row.attemptId}`);
    } catch (e) { setError(e.data?.message || `Không thể bắt đầu Part ${part}.`); }
    finally { starting.current = false; setPendingPart(null); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Thi thử / Theo Part</span><h1>Chọn Part để thi thử</h1>
      <p>Mỗi Part có lượt làm và lịch sử riêng.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!parts && !error && <p role="status">Đang tải các Part…</p>}
    <div className="mock-part-cards">{parts?.map(row => <article className="exam-card" key={row.part}>
      <div><h2>Part {row.part}</h2><p>{row.totalQuestions} câu hỏi</p>
        {row.activeAttemptId && <p>{row.answered}/{row.totalQuestions} đã trả lời · Còn {clock(row.remainingSeconds)}</p>}</div>
      <div className="exam-card-actions"><button className="primary-button" type="button" disabled={pendingPart !== null}
        onClick={() => start(row.part)}>{row.activeAttemptId ? "Tiếp tục" : pendingPart === row.part ? "Đang mở…" : `Thi Part ${row.part}`}</button>
        <Link className="outline-button" to={`/mock-test/history/part/${row.part}`}>Xem các lượt thi</Link></div>
    </article>)}</div>
  </div></SiteLayout>;
}
