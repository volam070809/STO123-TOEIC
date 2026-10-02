import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import "../../styles/exam.css";

const date = value => value ? new Date(value).toLocaleString("vi-VN") : "—";
const labels = { ALL: "Tất cả", FIXED: "Đề có sẵn", RANDOM: "Đề ngẫu nhiên", PART: "Thi theo Part" };

export default function MockHistoryDetailPage({ kind = "all" }) {
  const { part } = useParams();
  const { token } = useAuth();
  const [filter, setFilter] = useState(() => ({
    mode: kind === "random" ? "RANDOM" : kind === "part" ? "PART" : "ALL",
    part: kind === "part" ? Number(part) : null,
    examId: null
  }));
  const [response, setResponse] = useState(null);
  const [exams, setExams] = useState(null);
  const [examError, setExamError] = useState("");
  const [loadingMore, setLoadingMore] = useState(false);
  const historyRef = useRef(null);
  const requestKey = `${token}:${filter.mode}:${filter.part ?? ""}:${filter.examId ?? ""}`;
  const needsExam = filter.mode === "FIXED" && filter.examId == null;
  const current = response?.key === requestKey ? response : null;
  const rows = current?.items || [];
  const loading = !needsExam && !current;

  useEffect(() => {
    if (filter.mode !== "FIXED" || exams) return;
    let live = true;
    examApi.fixedSummary(token).then(data => { if (live) setExams(data.filter(exam => exam.completedAttempts > 0)); })
      .catch(() => { if (live) setExamError("Không thể tải danh sách đề có lịch sử."); });
    return () => { live = false; };
  }, [filter.mode, exams, token]);

  useEffect(() => {
    if (needsExam) return;
    let live = true;
    examApi.history(token, { mode: filter.mode, part: filter.mode === "PART" ? filter.part : null,
      examId: filter.mode === "FIXED" ? filter.examId : null })
      .then(data => { if (live) setResponse({ key: requestKey, ...data, error: "" }); })
      .catch(() => { if (live) setResponse({ key: requestKey, items: [], total: 0, hasMore: false,
        page: 1, error: "Không thể tải lịch sử thi." }); });
    return () => { live = false; };
  }, [filter.mode, filter.part, filter.examId, needsExam, requestKey, token]);

  function changeFilter(next) {
    setFilter(next);
    requestAnimationFrame(() => historyRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }));
  }

  async function loadMore() {
    if (loadingMore || !current?.hasMore) return;
    setLoadingMore(true);
    try {
      const data = await examApi.history(token, { mode: filter.mode,
        part: filter.mode === "PART" ? filter.part : null,
        examId: filter.mode === "FIXED" ? filter.examId : null, page: current.page + 1 });
      setResponse(old => old?.key === requestKey ? { key: requestKey, ...data,
        items: [...old.items, ...data.items], error: "" } : old);
    } catch {
      setResponse(old => old?.key === requestKey ? { ...old, error: "Không thể tải thêm lịch sử thi." } : old);
    } finally { setLoadingMore(false); }
  }

  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span><Link to="/mock-test">Thi thử</Link> / Lịch sử</span>
      <h1>Lịch sử thi thử</h1><p>Chọn loại bài để xem từng lần thi đã hoàn thành.</p></div>
    <div className="exam-navigation"><Link className="outline-button" to="/mock-test">← Quay lại Thi thử</Link></div>
    <section className="mock-history-section" ref={historyRef}>
      <div className="mock-history-filters" role="group" aria-label="Loại lịch sử">
        {Object.entries(labels).map(([value, label]) => <button key={value} type="button"
          className={filter.mode === value ? "primary-button" : "outline-button"}
          aria-pressed={filter.mode === value} onClick={() => changeFilter({ mode: value, part: null, examId: null })}>{label}</button>)}
      </div>
      {filter.mode === "PART" && <div className="mock-history-child">
        <label htmlFor="mock-part-history">Thi theo Part</label>
        <select id="mock-part-history" value={filter.part ?? ""}
          onChange={event => changeFilter({ mode: "PART", part: event.target.value ? Number(event.target.value) : null, examId: null })}>
          <option value="">Tất cả Part</option>
          {Array.from({ length: 7 }, (_, index) => index + 1).map(value =>
            <option key={value} value={value}>Part {value}</option>)}
        </select></div>}
      {filter.mode === "FIXED" && <div className="mock-history-child">
        <label htmlFor="mock-fixed-history">Đề có sẵn</label>
        <select id="mock-fixed-history" value={filter.examId ?? ""} disabled={!exams}
          onChange={event => changeFilter({ mode: "FIXED", part: null, examId: event.target.value ? Number(event.target.value) : null })}>
          <option value="">Chọn đề</option>
          {exams?.map(exam => <option key={exam.examId} value={exam.examId}>{exam.examName} · {exam.completedAttempts} lần thi</option>)}
        </select>
        {!exams && !examError && <small>Đang tải danh sách đề…</small>}
        {exams?.length === 0 && <small>Chưa có lượt thi đề có sẵn.</small>}
        {examError && <p className="exam-error" role="alert">{examError}</p>}</div>}
      {needsExam && <p>Chọn một đề để xem các lần thi.</p>}
      {loading && <p role="status">Đang tải lịch sử…</p>}
      {current?.error && <p className="exam-error" role="alert">{current.error}</p>}
      {current && !current.error && rows.length === 0 && <p>Chưa có bài thi đã kết thúc.</p>}
      {rows.length > 0 && <p>Hiển thị {rows.length} / {current.total} lần thi</p>}
      <div className="mock-history-grid">{rows.map(row => <article className="mock-history-card" key={row.attemptId}>
        <h2>{row.name}</h2><p>{date(row.startedAt)}</p>
        {row.mode === "PART" ? <p>{row.correct} / {row.totalQuestions} câu đúng · {row.percentage}%</p> :
          <p>Điểm TOEIC ước tính: {row.totalScore ?? "—"} / 990</p>}
        <div className="action-row"><Link className="outline-button" to={`/exam/${row.attemptId}/result`}>Xem kết quả</Link>
          <Link className="outline-button" to={`/exam/${row.attemptId}/review`}>Xem lại bài</Link></div>
      </article>)}</div>
      {current?.hasMore && <button type="button" className="outline-button" disabled={loadingMore} onClick={loadMore}>
        {loadingMore ? "Đang tải…" : "Xem thêm lần thi"}</button>}
    </section>
  </div></SiteLayout>;
}
