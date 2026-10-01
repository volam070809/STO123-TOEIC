import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import ExamContent from "./ExamContent";
import "../../styles/exam.css";

const filters = [["ALL", "Tất cả"], ["CORRECT", "Đúng"], ["INCORRECT", "Sai"],
  ["UNANSWERED", "Chưa trả lời"], ...Array.from({ length: 7 }, (_, i) => [`PART_${i + 1}`, `P${i + 1}`])];

export default function ExamReviewPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const [review, setReview] = useState(null);
  const [filter, setFilter] = useState("ALL");
  const [current, setCurrent] = useState(1);
  const [navOpen, setNavOpen] = useState(() => window.matchMedia("(min-width: 801px)").matches);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.review(attemptId, token).then(data => { if (live) setReview(data); })
      .catch(e => { if (live) setError(e.data?.code === "ATTEMPT_NOT_FINALIZED" ?
        "Bài thi chưa kết thúc." : "Không thể tải phần xem lại."); });
    return () => { live = false; };
  }, [attemptId, token]);
  const items = useMemo(() => review ? [
    ...review.groups.flatMap(group => group.questions.map(question => ({ question, group }))),
    ...review.independentQuestions.map(question => ({ question, group: null }))
  ].sort((a, b) => a.question.order - b.question.order) : [], [review]);
  const filtered = items.filter(({ question }) => filter === "ALL" ||
    question.status === filter || filter === `PART_${question.part}`);
  const focused = filtered.find(({ question }) => question.order === current) || filtered[0];
  const focusedIndex = filtered.indexOf(focused);

  return <SiteLayout><div className="site-container exam-review">
    <div className="page-heading"><span>{review?.source === "PLACEMENT" ? "Phân lớp" : "Thi thử"} / Xem lại</span><h1>Xem lại bài thi</h1></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!review && !error && <p>Đang tải phần xem lại…</p>}
    {review && <>
      <div className="exam-filters" aria-label="Lọc câu hỏi">
        {filters.map(([value, label]) => <button key={value} type="button"
          className={filter === value ? "selected" : ""} aria-pressed={filter === value}
          onClick={() => setFilter(value)}>{label}</button>)}
      </div>
      <div className="exam-layout"><main>
        {focused ? <>
          <p className="exam-review-count">Câu {focused.question.order} · {focusedIndex + 1}/{filtered.length} trong bộ lọc</p>
          <ExamContent key={focused.question.attemptQuestionId}
            group={focused.group ? { ...focused.group, questions: [focused.question] } : null}
            independentQuestion={focused.group ? null : focused.question}
            attemptId={attemptId} token={token} review />
          <div className="exam-step">
            <button className="outline-button" disabled={focusedIndex <= 0}
              onClick={() => setCurrent(filtered[focusedIndex - 1].question.order)}>Câu trước</button>
            <button className="outline-button" disabled={focusedIndex >= filtered.length - 1}
              onClick={() => setCurrent(filtered[focusedIndex + 1].question.order)}>Câu tiếp</button>
          </div>
        </> : <p>Không có câu hỏi trong bộ lọc này.</p>}
      </main><aside className="exam-navigator exam-review-navigator">
        <details open={navOpen} onToggle={event => setNavOpen(event.currentTarget.open)}>
          <summary>Điều hướng 1–{items.length}</summary>
          <div className="exam-number-grid">{items.map(({ question }) => {
            const inFilter = filtered.some(item => item.question.attemptQuestionId === question.attemptQuestionId);
            return <button key={question.attemptQuestionId} type="button" disabled={!inFilter}
              className={[question.order === focused?.question.order && "current",
                question.status?.toLowerCase()].filter(Boolean).join(" ")}
              aria-label={`Câu ${question.order}, ${question.status}`}
              onClick={() => { setCurrent(question.order); if (window.innerWidth <= 800) setNavOpen(false); }}>
              {question.order}</button>;
          })}</div>
        </details>
      </aside></div>
      <Link className="outline-button" to={review.source === "PLACEMENT" ? "/placement" :
        `/exam/${attemptId}/result`}>Về kết quả</Link>
    </>}
  </div></SiteLayout>;
}
