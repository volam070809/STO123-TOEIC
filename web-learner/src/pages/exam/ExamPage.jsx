import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import ExamContent from "./ExamContent";
import { examPartInstructions } from "./examPartInstructions";
import "../../styles/exam.css";

const parts = [1, 2, 3, 4, 5, 6, 7];
function clock(seconds) { return `${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`; }

export default function ExamPage() {
  const { attemptId } = useParams();
  const { token, renewToken } = useAuth();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState(null);
  const [error, setError] = useState("");
  const [current, setCurrent] = useState(1);
  const [remaining, setRemaining] = useState(null);
  const [pending, setPending] = useState(false);
  const [saveStates, setSaveStates] = useState({});
  const [navOpen, setNavOpen] = useState(() => window.matchMedia("(min-width: 801px)").matches);
  const queues = useRef(new Map());
  const failedSaves = useRef(new Set());
  const savingCount = useRef(new Map());
  const tokenRef = useRef(token);
  const renewRef = useRef(renewToken);
  useEffect(() => { tokenRef.current = token; }, [token]);
  useEffect(() => { renewRef.current = renewToken; }, [renewToken]);
  useEffect(() => {
    const renew = () => renewRef.current().catch(() => {});
    renew();
    const timer = setInterval(renew, 20 * 60 * 1000);
    const onVisible = () => { if (document.visibilityState === "visible") renew(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => { clearInterval(timer); document.removeEventListener("visibilitychange", onVisible); };
  }, []);
  useEffect(() => {
    let live = true;
    examApi.attempt(attemptId, tokenRef.current).then(data => {
      if (!live) return;
      if (data.status !== "DANG_LAM") navigate(data.source === "PLACEMENT" ? "/placement" :
        `/exam/${attemptId}/result`, { replace: true });
      else setAttempt(data);
    }).catch(() => { if (live) setError("Không thể tải bài thi hoặc bạn không có quyền truy cập."); });
    return () => { live = false; };
  }, [attemptId, navigate]);
  useEffect(() => {
    if (!attempt?.expiresAt) return;
    const update = () => setRemaining(Math.max(0, Math.ceil((Date.parse(attempt.expiresAt) - Date.now()) / 1000)));
    update();
    const timer = setInterval(update, 1000);
    return () => clearInterval(timer);
  }, [attempt?.expiresAt]);
  useEffect(() => {
    if (!attempt || remaining !== 0) return;
    examApi.attempt(attemptId, token).then(data => {
      if (data.status !== "DANG_LAM") navigate(data.source === "PLACEMENT" ? "/placement" :
        `/exam/${attemptId}/result`, { replace: true });
    }).catch(() => {});
  }, [remaining, attempt, attemptId, token, navigate]);
  useEffect(() => {
    if (!attempt) return;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [attempt]);
  const questions = useMemo(() => attempt ? [...attempt.groups.flatMap(g => g.questions), ...attempt.independentQuestions]
    .sort((a, b) => a.order - b.order) : [], [attempt]);
  const selected = questions.find(q => q.order === current);
  const currentPart = selected?.part;
  const group = attempt?.groups.find(g => g.groupId === selected?.groupId || g.questions.some(q => q.attemptQuestionId === selected?.attemptQuestionId));
  const answered = questions.filter(q => q.selectedOption).length;

  function updateQuestion(id, patch) {
    setAttempt(old => ({ ...old,
      groups: old.groups.map(g => ({ ...g, questions: g.questions.map(q => q.attemptQuestionId === id ? { ...q, ...patch } : q) })),
      independentQuestions: old.independentQuestions.map(q => q.attemptQuestionId === id ? { ...q, ...patch } : q)
    }));
  }
  function enqueue(question, work) {
    const id = question.attemptQuestionId;
    const count = (savingCount.current.get(id) || 0) + 1;
    savingCount.current.set(id, count);
    setSaveStates(old => ({ ...old, [id]: "saving" }));
    const next = (queues.current.get(id) || Promise.resolve()).catch(() => {}).then(work);
    queues.current.set(id, next);
    next.then(() => { failedSaves.current.delete(id); }).catch(() => {
      failedSaves.current.add(id);
    }).finally(() => {
      const left = (savingCount.current.get(id) || 1) - 1;
      savingCount.current.set(id, left);
      if (!left) setSaveStates(old => ({ ...old, [id]: failedSaves.current.has(id) ? "error" : "saved" }));
    });
  }
  function answer(question, option) {
    updateQuestion(question.attemptQuestionId, { selectedOption: option });
    enqueue(question, () => examApi.answer(attemptId, question.attemptQuestionId, option, token));
  }
  function flag(question) {
    const next = !question.flagged;
    updateQuestion(question.attemptQuestionId, { flagged: next });
    enqueue(question, () => examApi.flag(attemptId, question.attemptQuestionId, next, token));
  }
  function retry(question) {
    enqueue(question, async () => {
      await examApi.answer(attemptId, question.attemptQuestionId, question.selectedOption, token);
      await examApi.flag(attemptId, question.attemptQuestionId, question.flagged, token);
    });
  }
  async function submit() {
    if (pending) return;
    setPending(true); setError("");
    try {
      await Promise.allSettled([...queues.current.values()]);
      if (failedSaves.current.size) { setError("Một số câu trả lời chưa được lưu. Vui lòng thử lại trước khi nộp."); return; }
      const finished = await examApi.submit(attemptId, token);
      navigate(finished.source === "PLACEMENT" ? "/placement" : `/exam/${attemptId}/result`);
    } catch { setError("Không thể nộp bài. Vui lòng thử lại."); }
    finally { setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-page">
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!attempt ? <p>{error ? <Link to="/mock-test">Về trang thi thử</Link> : "Đang tải bài thi…"}</p> : <>
      <div className="exam-top"><div><h1>{attempt.examName || "Thi thử TOEIC"}</h1><p>Part {currentPart} · Đã trả lời {answered}/{attempt.totalQuestions}</p></div>
        <div className="exam-top-actions"><strong aria-live="polite">{remaining === null ? "--:--" : clock(remaining)}</strong>
          <button className="primary-button" disabled={pending} onClick={submit}>Nộp bài</button></div></div>
      <div className="exam-layout"><main>
        <p className="exam-part-instruction">{examPartInstructions[currentPart]}</p>
        <ExamContent group={group} independentQuestion={!group ? selected : null} attemptId={attemptId} token={token}
          mode={attempt.source === "PRACTICE" ? "PRACTICE" : attempt.source === "PLACEMENT" ? "PLACEMENT" : "MOCK"}
          onAnswer={answer} onFlag={flag} onRetry={retry} saveStates={saveStates} submissionPending={pending} />
        <div className="exam-step"><button className="outline-button" disabled={current <= 1} onClick={() => setCurrent(current - 1)}>Câu trước</button>
          <button className="outline-button" disabled={current >= attempt.totalQuestions} onClick={() => setCurrent(current + 1)}>Câu tiếp</button></div>
      </main><aside className="exam-navigator"><details open={navOpen} onToggle={event => setNavOpen(event.currentTarget.open)}><summary>Điều hướng câu hỏi</summary>
        {parts.map(part => <div key={part}><h3>Part {part}</h3><div className="exam-number-grid">
          {questions.filter(q => q.part === part).map(q => <button type="button" key={q.attemptQuestionId}
            className={[q.order === current && "current", q.selectedOption ? "answered" : "unanswered",
              q.flagged && "flagged"].filter(Boolean).join(" ")}
            aria-label={`Câu ${q.order}${q.selectedOption ? ", đã trả lời" : ", chưa trả lời"}${q.flagged ? ", đã đánh dấu" : ""}`}
            onClick={() => { setCurrent(q.order); if (window.innerWidth <= 800) setNavOpen(false);
              requestAnimationFrame(() => document.getElementById("question-" + q.attemptQuestionId)?.focus()); }}>{q.order}</button>)}</div></div>)}
      </details></aside></div>
    </>}
  </div></SiteLayout>;
}
