import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import ExamContent from "./ExamContent";
import "../../styles/exam.css";

const statuses = [["ALL", "Tất cả"], ["CORRECT", "Đúng"], ["INCORRECT", "Sai"], ["UNANSWERED", "Chưa trả lời"]];
const statusName = { CORRECT: "Đúng", INCORRECT: "Sai", UNANSWERED: "Chưa trả lời" };

export default function ExamReviewPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const [review, setReview] = useState(null);
  const [result, setResult] = useState(null);
  const [statusFilter, setStatusFilter] = useState("ALL");
  const [partFilter, setPartFilter] = useState("ALL");
  const [current, setCurrent] = useState(null);
  const [navOpen, setNavOpen] = useState(() => window.matchMedia("(min-width: 1051px)").matches);
  const [error, setError] = useState("");
  const reviewArea = useRef(null);
  useEffect(() => {
    let live = true;
    examApi.review(attemptId, token)
      .then(data => { if (live) { setReview(data); setResult(data.result); } })
      .catch(e => { if (live) setError(e.data?.code === "MOCK_REVIEW_UNAVAILABLE" ?
        "Đề thi thử chỉ hiển thị điểm và thống kê theo Part." :
        e.data?.code === "ATTEMPT_NOT_FINALIZED" ? "Bài thi chưa kết thúc." : "Không thể tải phần xem lại."); });
    return () => { live = false; };
  }, [attemptId, token]);
  const items = useMemo(() => review ? [
    ...review.groups.flatMap(group => group.questions.map(question => ({ question, group }))),
    ...review.independentQuestions.map(question => ({ question, group: null }))
  ].sort((a, b) => a.question.order - b.question.order) : [], [review]);
  const testedParts = result?.parts.filter(p => p.stats.total > 0) || [];
  const filtered = items.filter(({ question }) => (statusFilter === "ALL" || question.status === statusFilter) &&
    (partFilter === "ALL" || question.part === Number(partFilter)));
  const focused = filtered.find(({ question }) => question.order === current) || filtered[0];
  const focusedIndex = filtered.indexOf(focused);
  const navigatorParts = [...new Set(filtered.map(item => item.question.part))];
  const partMock = result?.source === "PART";
  return <SiteLayout><div className="site-container exam-review">
    <div className="page-heading"><span>{review?.source === "PLACEMENT" ? "Lộ trình học" : "Thi thử"} / Xem lại</span>
      <h1>{result?.examName || "Xem lại bài thi"}</h1></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!review && !error && <p>Đang tải phần xem lại…</p>}
    {review && result && <>
      <section className="review-overview"><h2>Thông tin bài thi</h2>
        <p>{new Date(result.startedAt).toLocaleString("vi-VN")} · {result.overall.total} câu · {result.status === "HET_GIO" ? "Hết giờ" : "Đã nộp"}</p>
        <div className="exam-stat-grid"><div><b>{result.overall.correct} / {result.overall.total}</b><span>Đúng</span></div>
          <div><b>{result.overall.incorrect} / {result.overall.total}</b><span>Sai</span></div>
          <div><b>{result.overall.unanswered} / {result.overall.total}</b><span>Chưa trả lời</span></div></div>
        {!partMock && result.totalScore != null && <p>Điểm TOEIC ước tính: <strong>{result.totalScore} / 990</strong></p>}
      </section>
      <section><h2>Kết quả theo Part</h2><div className="review-part-grid">{testedParts.map(row =>
        <button key={row.part} type="button" className={`review-part-card ${partFilter === String(row.part) ? "selected" : ""}`}
          onClick={() => { setPartFilter(String(row.part)); setCurrent(null); reviewArea.current?.scrollIntoView({ behavior: "smooth" }); }}>
          <strong>Part {row.part}</strong><b>{row.stats.correct} / {row.stats.total} đúng</b>
          <span>{row.stats.percentage}%</span><div className="placement-bar"><span style={{ width: `${row.stats.percentage}%` }} /></div>
          <small>Sai: {row.stats.incorrect} · Chưa trả lời: {row.stats.unanswered}</small>
        </button>)}</div></section>
      <section ref={reviewArea}><h2>Bộ lọc</h2><div className="exam-filters" aria-label="Lọc kết quả">
        {statuses.map(([value, label]) => <button key={value} type="button" className={statusFilter === value ? "selected" : ""}
          aria-pressed={statusFilter === value} onClick={() => { setStatusFilter(value); setCurrent(null); }}>{label}</button>)}</div>
        <div className="exam-filters" aria-label="Lọc Part">
          <button type="button" className={partFilter === "ALL" ? "selected" : ""} onClick={() => { setPartFilter("ALL"); setCurrent(null); }}>Tất cả Part</button>
          {testedParts.map(row => <button key={row.part} type="button" className={partFilter === String(row.part) ? "selected" : ""}
            aria-pressed={partFilter === String(row.part)} onClick={() => { setPartFilter(String(row.part)); setCurrent(null); }}>P{row.part}</button>)}</div>
      </section>
      <div className="exam-layout"><main>
        {focused ? <>
          <p className="exam-review-count">Câu {focused.question.order} · {focusedIndex + 1}/{filtered.length} trong bộ lọc</p>
          <ExamContent key={focused.question.attemptQuestionId}
            group={focused.group ? { ...focused.group, questions: [focused.question] } : null}
            independentQuestion={focused.group ? null : focused.question}
            attemptId={attemptId} token={token} review />
          <div className="exam-step"><button className="outline-button" disabled={focusedIndex <= 0}
            onClick={() => setCurrent(filtered[focusedIndex - 1].question.order)}>Câu trước</button>
            <button className="outline-button" disabled={focusedIndex >= filtered.length - 1}
              onClick={() => setCurrent(filtered[focusedIndex + 1].question.order)}>Câu tiếp</button></div>
        </> : <p className="exam-content">Không có câu hỏi phù hợp với bộ lọc này.</p>}
      </main><aside className="exam-navigator exam-review-navigator">
        <details open={navOpen} onToggle={event => setNavOpen(event.currentTarget.open)}>
          <summary>Điều hướng câu hỏi · {filtered.length} câu</summary>
          <p className="review-legend">✓ Đúng · ✕ Sai · – Chưa trả lời</p>
          {navigatorParts.map(part => <details key={part} open={partFilter !== "ALL" || part === navigatorParts[0]}>
            <summary>Part {part} · {filtered.filter(item => item.question.part === part && item.question.status === "CORRECT").length}/{filtered.filter(item => item.question.part === part).length} đúng</summary>
            <div className="exam-number-grid">{filtered.filter(item => item.question.part === part).map(({ question }) =>
              <button key={question.attemptQuestionId} type="button"
                className={[question.order === focused?.question.order && "current", question.status.toLowerCase()].filter(Boolean).join(" ")}
                aria-label={`Câu ${question.order}, ${statusName[question.status]}`}
                onClick={() => { setCurrent(question.order); if (window.innerWidth <= 1050) setNavOpen(false); }}>
                {question.order}<small aria-hidden="true">{question.status === "CORRECT" ? "✓" : question.status === "INCORRECT" ? "✕" : "–"}</small>
              </button>)}</div></details>)}
        </details>
      </aside></div>
      <div className="exam-navigation">
        <Link className="outline-button" to={`/exam/${attemptId}/result`}>← Quay lại kết quả</Link>
        <Link className="outline-button" to={review.source === "PLACEMENT" ? "/placement" : "/mock-test"}>
          {review.source === "PLACEMENT" ? "Về Lộ trình học" : "Về trang Thi thử"}</Link>
      </div>
    </>}
  </div></SiteLayout>;
}
