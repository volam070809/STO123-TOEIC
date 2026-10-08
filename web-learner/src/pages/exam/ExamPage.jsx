import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi } from "../../services/examApi";
import ExamContent from "./ExamContent";
import { examPartInstructions } from "./examPartInstructions";
import "../../styles/exam.css";

const parts = [1, 2, 3, 4, 5, 6, 7];
function clock(seconds) { return [Math.floor(seconds / 3600), Math.floor(seconds % 3600 / 60), seconds % 60].map(n => String(n).padStart(2, "0")).join(":"); }

export default function ExamPage() {
  const { attemptId } = useParams();
  const { token, renewToken, logout } = useAuth();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState(null);
  const [error, setError] = useState("");
  const [current, setCurrent] = useState(1);
  const [remaining, setRemaining] = useState(null);
  const [timerAnchor, setTimerAnchor] = useState(null);
  const [pending, setPending] = useState(false);
  const [exitTarget, setExitTarget] = useState(null);
  const [saveStates, setSaveStates] = useState({});
  const [navOpen, setNavOpen] = useState(() => window.matchMedia("(min-width: 1051px)").matches);
  const queues = useRef(new Map());
  const failedSaves = useRef(new Set());
  const savingCount = useRef(new Map());
  const tokenRef = useRef(token);
  const renewRef = useRef(renewToken);
  const leaving = useRef(false);
  const checkingExpiry = useRef(false);
  useEffect(() => { tokenRef.current = token; }, [token]);
  useEffect(() => { renewRef.current = renewToken; }, [renewToken]);
  useEffect(() => {
    const renewIfNeeded = () => {
      const expiry = Date.parse(sessionStorage.getItem("expiresAtUtc") || "");
      if (!Number.isFinite(expiry) || expiry - Date.now() < 2 * 60 * 1000)
        renewRef.current().catch(() => {});
    };
    renewIfNeeded();
    const timer = setInterval(renewIfNeeded, 60 * 1000);
    const onVisible = () => { if (document.visibilityState === "visible") renewIfNeeded(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => { clearInterval(timer); document.removeEventListener("visibilitychange", onVisible); };
  }, []);
  useEffect(() => {
    let live = true;
    examApi.resume(attemptId, tokenRef.current).then(data => {
      if (!live) return;
      if (data.status !== "DANG_LAM" && data.status !== "BO_DO")
        navigate(`/exam/${attemptId}/result`, { replace: true });
      else { setAttempt(data); setRemaining(data.remainingSeconds);
        setTimerAnchor({ seconds: data.remainingSeconds, at: Date.now() }); }
    }).catch(() => { if (live) setError("Không thể tải bài thi hoặc bạn không có quyền truy cập."); });
    return () => { live = false; };
  }, [attemptId, navigate]);
  const timerPaused = attempt?.isPaused;
  useEffect(() => {
    if (!timerAnchor || timerPaused) return;
    const timer = setInterval(() => setRemaining(Math.max(0,
      timerAnchor.seconds - Math.floor((Date.now() - timerAnchor.at) / 1000))), 1000);
    return () => clearInterval(timer);
  }, [timerAnchor, timerPaused]);
  const timerActive = Boolean(attempt && !attempt.isPaused);
  useEffect(() => {
    if (!timerActive || remaining !== 0 || checkingExpiry.current) return;
    checkingExpiry.current = true;
    examApi.heartbeat(attemptId, tokenRef.current).then(data => {
      if (data.status !== "DANG_LAM") navigate(`/exam/${attemptId}/result`, { replace: true });
      else { setRemaining(data.remainingSeconds);
        setTimerAnchor({ seconds: data.remainingSeconds, at: Date.now() }); }
    }).catch(() => {}).finally(() => { checkingExpiry.current = false; });
  }, [remaining, timerActive, attemptId, navigate]);
  useEffect(() => {
    if (!timerActive || attempt?.status !== "DANG_LAM") return;
    let live = true;
    const timer = setInterval(async () => {
      try {
        let data = await examApi.heartbeat(attemptId, tokenRef.current);
        if (!live || leaving.current) return;
        if (data.status === "DANG_LAM" && data.isPaused)
          data = await examApi.resume(attemptId, tokenRef.current);
        if (live && !leaving.current) {
          if (data.status !== "DANG_LAM") navigate(`/exam/${attemptId}/result`, { replace: true });
          else {
            if (data.groups) setAttempt(data);
            setRemaining(data.remainingSeconds);
            setTimerAnchor({ seconds: data.remainingSeconds, at: Date.now() });
          }
        }
      } catch { if (live) setError("Không thể đồng bộ đồng hồ. Vui lòng kiểm tra kết nối."); }
    }, 15000);
    return () => { live = false; clearInterval(timer); };
  }, [timerActive, attempt?.status, attemptId, navigate]);
  useEffect(() => {
    if (!attempt) return;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [attempt]);
  useEffect(() => {
    if (!import.meta.env.DEV || !attempt) return;
    const kind = attempt.source === "PLACEMENT" ? "placement" : attempt.source === "FIXED" ? "mock" : null;
    if (!kind || !performance.getEntriesByName(`${kind}-start-click`, "mark").length) return;
    const frame = requestAnimationFrame(() => {
      performance.mark(`${kind}-first-question-usable`);
      const measure = performance.measure(`${kind}-click-to-usable`, `${kind}-start-click`, `${kind}-first-question-usable`);
      console.info(`${kind === "placement" ? "Placement" : "Mock"} click to first usable question: ${measure.duration.toFixed(1)} ms`);
      performance.clearMarks(`${kind}-start-click`);
      performance.clearMarks(`${kind}-first-question-usable`);
      performance.clearMeasures(`${kind}-click-to-usable`);
    });
    return () => cancelAnimationFrame(frame);
  }, [attempt]);
  const questions = useMemo(() => attempt ? [...attempt.groups.flatMap(g => g.questions), ...attempt.independentQuestions]
    .sort((a, b) => a.order - b.order) : [], [attempt]);
  const selected = questions.find(q => q.order === current);
  const currentPart = selected?.part;
  const rootDestination = attempt?.source === "PLACEMENT" ? "/placement" : "/mock-test";
  const backDestination = rootDestination;
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
    if (question.selectedOption === option) return;
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
  async function leave(destination, signOut = false) {
    if (pending || leaving.current) return;
    leaving.current = true; setPending(true); setError("");
    try {
      await Promise.allSettled([...queues.current.values()]);
      if (failedSaves.current.size) { setError("Một số câu trả lời chưa được lưu. Vui lòng thử lại trước khi rời bài."); return; }
      await examApi.pause(attemptId, tokenRef.current);
      if (signOut) logout();
      navigate(destination);
    } catch { setError("Không thể tạm dừng bài thi. Vui lòng thử lại."); }
    finally { leaving.current = false; setPending(false); setExitTarget(null); }
  }
  useEffect(() => {
    if (!attempt || attempt.isPaused) return;
    const onLink = event => {
      const logoutButton = event.target.closest?.("[data-exam-logout]");
      if (logoutButton) {
        event.preventDefault(); event.stopPropagation();
        setExitTarget({ destination: "/", signOut: true });
        return;
      }
      const link = event.target.closest?.("a[href]");
      if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey ||
          event.shiftKey || event.altKey || link.target === "_blank") return;
      const url = new URL(link.href, window.location.href);
      if (url.origin !== window.location.origin || url.pathname === window.location.pathname) return;
      event.preventDefault();
      event.stopPropagation();
      setExitTarget({ destination: url.pathname + url.search + url.hash, signOut: false });
    };
    document.addEventListener("click", onLink, true);
    return () => document.removeEventListener("click", onLink, true);
  });
  async function submit() {
    if (pending) return;
    setPending(true); setError("");
    try {
      await Promise.allSettled([...queues.current.values()]);
      if (failedSaves.current.size) { setError("Một số câu trả lời chưa được lưu. Vui lòng thử lại trước khi nộp."); return; }
      await examApi.submit(attemptId, token);
      navigate(`/exam/${attemptId}/result`);
    } catch { setError("Không thể nộp bài. Vui lòng thử lại."); }
    finally { setPending(false); }
  }
  return <SiteLayout><div className="site-container exam-page">
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!attempt ? !error && <p>Đang tải bài thi…</p> : <>
      <div className="exam-top"><div><h1>{attempt.examName || "Thi thử TOEIC"}</h1><p>Part {currentPart} · Đã trả lời {answered}/{attempt.totalQuestions}</p></div>
        <div className="exam-top-actions"><strong aria-live="polite">{remaining === null ? "--:--:--" : clock(remaining)}</strong>
          <button className="primary-button" disabled={pending} onClick={submit}>Nộp bài</button></div></div>
      <p className="exam-card-note" role="status">Đồng hồ chạy khi bạn ở trong bài thi. Câu trả lời được tự động lưu.</p>
      <div className="exam-navigation">
        {backDestination !== rootDestination && <button className="outline-button" type="button" disabled={pending}
          onClick={() => setExitTarget({ destination: backDestination, signOut: false })}>← Quay lại</button>}
        <button className="outline-button" type="button" disabled={pending}
          onClick={() => setExitTarget({ destination: rootDestination, signOut: false })}>Thoát bài</button>
      </div>
      <div className="exam-layout"><main>
        <p className="exam-part-instruction">{examPartInstructions[currentPart]}</p>
        <ExamContent key={group?.groupId ?? selected?.attemptQuestionId} group={group} independentQuestion={!group ? selected : null} attemptId={attemptId} token={token}
          mode={attempt.source === "PRACTICE" ? "PRACTICE" : attempt.source === "PLACEMENT" ? "PLACEMENT" : "MOCK"}
          onAnswer={answer} onFlag={flag} onRetry={retry} saveStates={saveStates} submissionPending={pending} />
        <div className="exam-step"><button className="outline-button" disabled={current <= 1} onClick={() => setCurrent(current - 1)}>Câu trước</button>
          <button className="outline-button" disabled={current >= attempt.totalQuestions} onClick={() => setCurrent(current + 1)}>Câu tiếp</button></div>
      </main><aside className="exam-navigator"><details open={navOpen} onToggle={event => setNavOpen(event.currentTarget.open)}><summary>Điều hướng câu hỏi</summary>
        {parts.filter(part => questions.some(q => q.part === part)).map(part => <div key={part}><h3>Part {part}</h3><div className="exam-number-grid">
          {questions.filter(q => q.part === part).map(q => <button type="button" key={q.attemptQuestionId}
            className={[q.order === current && "current", q.selectedOption ? "answered" : "unanswered",
              q.flagged && "flagged"].filter(Boolean).join(" ")}
            aria-label={`Câu ${q.order}${q.selectedOption ? ", đã trả lời" : ", chưa trả lời"}${q.flagged ? ", đã đánh dấu" : ""}`}
            onClick={() => { setCurrent(q.order); if (window.innerWidth <= 1050) setNavOpen(false);
              requestAnimationFrame(() => document.getElementById("question-" + q.attemptQuestionId)?.focus()); }}>{q.order}</button>)}</div></div>)}
      </details></aside></div>
    </>}
    {exitTarget && <div className="exam-confirm-backdrop" role="presentation"><div className="exam-confirm" role="dialog" aria-modal="true" aria-labelledby="exam-exit-title">
      <h2 id="exam-exit-title">Bạn có chắc muốn rời khỏi bài?</h2>
      <p>Tiến độ chưa nộp có thể bị mất.</p>
      <div className="exam-navigation">
        <button className="outline-button" type="button" disabled={pending} onClick={() => setExitTarget(null)}>Ở lại làm bài</button>
        <button className="primary-button" type="button" disabled={pending} onClick={() => leave(exitTarget.destination, exitTarget.signOut)}>Thoát bài</button>
      </div>
    </div></div>}
  </div></SiteLayout>;
}
