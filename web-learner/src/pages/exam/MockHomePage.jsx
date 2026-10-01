import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

export default function MockHomePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [fixed, setFixed] = useState(null);
  const [history, setHistory] = useState([]);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const starting = useRef(false);
  useEffect(() => {
    let live = true;
    Promise.all([examApi.fixed(token), examApi.history(token)]).then(([exams, attempts]) => {
      if (live) { setFixed(exams); setHistory(attempts); }
    }).catch(() => { if (live) setError("Không thể tải danh sách đề thi."); });
    return () => { live = false; };
  }, [token]);
  async function start(source, examId = null) {
    if (starting.current) return;
    starting.current = true;
    setPending(true);
    setError("");
    try {
      const result = await examApi.start(source, examId, token);
      navigate("/exam/" + result.attemptId);
    } catch (e) {
      setError(e.data?.code === "KHONG_DU_DU_LIEU_TAO_DE" ? "Không đủ dữ liệu để tạo đề thi." :
        "Không thể bắt đầu đề thi. Vui lòng thử lại.");
    } finally { starting.current = false; setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Thi thử</span><h1>Thi thử TOEIC</h1><p>200 câu · 7 Part · 120 phút</p></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    <section><h2>Đề thi có sẵn</h2>
      {fixed === null ? <p>Đang tải đề thi…</p> : fixed.length === 0 ? <p>Chưa có đề thi có sẵn.</p> :
        <div className="exam-card-list">{fixed.map(exam => <article className="exam-card" key={exam.examId}>
          <div><h3>{exam.examName}</h3><p>200 câu · 120 phút · Đang mở</p></div>
          <button className="primary-button" disabled={pending} onClick={() => start("FIXED", exam.examId)}>Bắt đầu</button>
        </article>)}</div>}</section>
    <section><h2>Đề thi ngẫu nhiên</h2><article className="exam-card"><div><h3>Tạo từ ngân hàng STO123</h3>
      <p>200 câu · 7 Part · 120 phút</p></div>
      <button className="primary-button" disabled={pending} onClick={() => start("RANDOM")}>Tạo đề thi ngẫu nhiên</button></article></section>
    <section><h2>Lịch sử thi thử</h2>{history.length === 0 ? <p>Chưa có lượt làm bài.</p> :
      <div className="exam-card-list">{history.map(row => <article className="exam-card" key={row.attemptId}>
        <div><h3>Lượt #{row.attemptId}</h3><p>{new Date(row.startedAt).toLocaleString("vi-VN")} · {row.status}</p></div>
        <Link className="outline-button" to={row.status === "DANG_LAM" ? "/exam/" + row.attemptId : "/exam/" + row.attemptId + "/result"}>
          {row.status === "DANG_LAM" ? "Tiếp tục" : "Xem kết quả"}</Link>
      </article>)}</div>}</section>
  </div></SiteLayout>;
}
