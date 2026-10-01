import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import ExamContent from "./ExamContent";
import "../../styles/exam.css";

export default function ExamReviewPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const [review, setReview] = useState(null);
  const [filter, setFilter] = useState("ALL");
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.review(attemptId, token).then(data => { if (live) setReview(data); })
      .catch(e => { if (live) setError(e.data?.code === "ATTEMPT_NOT_FINALIZED" ?
        "Bài thi chưa kết thúc." : "Không thể tải phần xem lại."); });
    return () => { live = false; };
  }, [attemptId, token]);
  const items = useMemo(() => review ? [
    ...review.groups.map(group => ({ order: Math.min(...group.questions.map(q => q.order)), group })),
    ...review.independentQuestions.map(question => ({ order: question.order, question }))
  ].sort((a, b) => a.order - b.order) : [], [review]);
  function visible(question) { return filter === "ALL" || question.status === filter || filter === `PART_${question.part}`; }
  return <SiteLayout><div className="site-container exam-review">
    <div className="page-heading"><span>Thi thử / Xem lại</span><h1>Xem lại bài thi</h1></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!review && !error && <p>Đang tải phần xem lại…</p>}
    {review && <><div className="exam-filters" aria-label="Lọc câu hỏi">
      {[["ALL", "Tất cả"], ["CORRECT", "Đúng"], ["INCORRECT", "Sai"], ["UNANSWERED", "Chưa trả lời"],
        ...Array.from({ length: 7 }, (_, i) => [`PART_${i + 1}`, `Part ${i + 1}`])].map(([value, label]) =>
        <button key={value} type="button" className={filter === value ? "selected" : ""} onClick={() => setFilter(value)}>{label}</button>)}</div>
      {items.map(item => {
        if (item.group) {
          const questions = item.group.questions.filter(visible);
          return questions.length ? <ExamContent key={item.group.groupId} group={{ ...item.group, questions }}
            attemptId={attemptId} token={token} review /> : null;
        }
        return visible(item.question) ? <ExamContent key={item.question.attemptQuestionId}
          independentQuestion={item.question} attemptId={attemptId} token={token} review /> : null;
      })}
      <Link className="outline-button" to={`/exam/${attemptId}/result`}>Về kết quả</Link>
    </>}
  </div></SiteLayout>;
}
