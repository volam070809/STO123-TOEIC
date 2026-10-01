import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const score = value => value == null ? "—" : `${new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 2 }).format(value)}/990`;
const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";

export default function MockHomePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [history, setHistory] = useState(null);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    examApi.history(token).then(data => { if (live) setHistory(data); })
      .catch(() => { if (live) setError("Không thể tải danh sách đề thi."); });
    return () => { live = false; };
  }, [token]);
  async function start(examId) {
    if (starting.current) return;
    starting.current = true;
    setPending(true);
    setError("");
    try {
      const result = await examApi.start(examId == null ? "RANDOM" : "FIXED", examId, token);
      navigate("/exam/" + result.attemptId);
    } catch (e) {
      setError(e.data?.code === "KHONG_DU_DU_LIEU_TAO_DE" ? "Không đủ dữ liệu để tạo đề thi." :
        e.data?.code === "INVALID_EXAM_STRUCTURE" ? "Đề thi đã đóng hoặc không hợp lệ." :
          "Không thể bắt đầu đề thi. Vui lòng thử lại.");
    } finally { starting.current = false; setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Thi thử</span><h1>Thi thử TOEIC</h1><p>7 Part · tối đa 200 câu · 120 phút</p></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    <section><h2>Đề thi cố định</h2>
      {!history ? <p>Đang tải đề thi…</p> : history.fixedExams.length === 0 ?
        <p>Chưa có đề thi.</p> : <div className="exam-card-list">
          {history.fixedExams.map(exam => <article className="exam-card" key={exam.examId}>
            <div><h3>{exam.examName}</h3>
              <p>{exam.completedAttempts} lượt hoàn thành · Điểm mới nhất: {score(exam.latestScore)}</p>
              <p>Trung bình: {score(exam.averageScore)} · Cao nhất: {score(exam.bestScore)}</p>
              <p>Hoàn thành gần nhất: {date(exam.latestCompletedAt)}</p>
              {exam.examStatus !== "OPEN" && <p>Đề thi đã đóng; không thể bắt đầu lượt mới.</p>}
            </div>
            <div className="exam-card-actions">
              {exam.activeAttemptId ? <Link className="primary-button" to={`/exam/${exam.activeAttemptId}`}>Tiếp tục</Link> :
                <button className="primary-button" disabled={pending || exam.examStatus !== "OPEN"}
                  title={exam.examStatus !== "OPEN" ? "Đề thi đã đóng" : undefined}
                  onClick={() => start(exam.examId)}>Bắt đầu</button>}
              {exam.completedAttempts > 0 && <span className="exam-card-note">Điểm TOEIC ước tính</span>}
            </div>
          </article>)}
        </div>}</section>
    <section><h2>Đề thi ngẫu nhiên</h2>
      <p>Đề ngẫu nhiên lấy câu hỏi hợp lệ từ cả 7 Part, tối đa 200 câu. Nếu ngân hàng câu hỏi ít hơn, đề sẽ ngắn hơn và không lặp câu hỏi.</p>
      <button type="button" className="outline-button" disabled={pending} onClick={() => start(null)}>Tạo đề ngẫu nhiên</button>
    </section>
    {history && <section><h2>Thống kê thi thử</h2>
      <div className="exam-stat-grid">
        <div><b>{score(history.statistics.averageBetweenFixedExams)}</b><span>Trung bình giữa các đề cố định</span></div>
        <div><b>{score(history.statistics.highestMockScore)}</b><span>Điểm Mock cao nhất</span></div>
        <div><b>{score(history.statistics.latestMockScore)}</b><span>Điểm Mock gần nhất</span></div>
      </div><p className="exam-card-note">Điểm TOEIC ước tính. Chỉ tính lượt đã nộp hoặc hết giờ.</p>
    </section>}
    {history?.randomAttempts.length > 0 && <section><h2>Lượt thi ngẫu nhiên trước đây</h2>
      <div className="exam-card-list">{history.randomAttempts.map(row => <article className="exam-card" key={row.attemptId}>
        <div><h3>Lượt #{row.attemptId}</h3><p>{date(row.startedAt)} · {row.status} · {score(row.totalScore)}</p></div>
        <Link className="outline-button" to={row.status === "DANG_LAM" || row.status === "BO_DO" ?
          `/exam/${row.attemptId}` : `/exam/${row.attemptId}/result`}>
          {row.status === "DANG_LAM" || row.status === "BO_DO" ? "Tiếp tục" : "Xem kết quả"}</Link>
      </article>)}</div>
    </section>}
  </div></SiteLayout>;
}
