import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import { practiceRead } from "../../services/practiceRead";
import { answerViewed, canNavigateTo, checked, restoredFeedback } from "./practiceFlow.mjs";
import PracticeHistoryDashboard from "./PracticeHistoryDashboard";
import DocumentRenderer from "../exam/DocumentRenderer";
import { examMediaBlob } from "../../services/examApi";
import "../../styles/exam.css";
import "../../styles/vocabulary.css";
import "../../styles/practice-parts.css";

const api = "/api/practice";
const historyPageSize = 8;
const parts = ["Photographs", "Question-Response", "Conversations", "Talks", "Incomplete Sentences", "Text Completion", "Reading Comprehension"];

export default function PracticePartsPage() {
  const { token } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const routeAttemptId = Number(searchParams.get("attempt")) || null;
  const routeReviewId = Number(searchParams.get("review")) || null;
  const [available, setAvailable] = useState(null);
  const [part, setPart] = useState(5);
  const [difficulty, setDifficulty] = useState(1);
  const [preset, setPreset] = useState("SHORT");
  const [current, setCurrent] = useState(0);
  const [attemptData, setAttempt] = useState(null);
  const [feedback, setFeedback] = useState({});
  const [summary, setSummary] = useState(null);
  const [history, setHistory] = useState(null);
  const [historyPart, setHistoryPart] = useState(0);
  const [historySort, setHistorySort] = useState("NEWEST");
  const [historyPage, setHistoryPage] = useState(1);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState("");
  const [historyRetry, setHistoryRetry] = useState(0);
  const [activeAttempts, setActiveAttempts] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [questionBusy, setQuestionBusy] = useState({});
  const startLock = useRef(false);
  const questionLocks = useRef(new Set());
  const loadedAttempt = useRef(null);
  const loadedReview = useRef(null);
  const attempt = (routeAttemptId && attemptData?.attemptId === routeAttemptId && !attemptData.isReview) ||
    (routeReviewId && attemptData?.attemptId === routeReviewId && attemptData.isReview) ||
    (routeReviewId && summary?.attemptId === routeReviewId) ? attemptData : null;

  function showAttempt(data) {
    loadedAttempt.current = data.attemptId;
    setAttempt(data);
    setFeedback(restoredFeedback(data.questions || []));
    const firstUnchecked = (data.questions || []).findIndex(q => !q.isChecked);
    setCurrent(firstUnchecked < 0 ? Math.max(0, data.questions.length - 1) : firstUnchecked);
  }

  function lockQuestion(id) {
    if (questionLocks.current.has(id)) return false;
    questionLocks.current.add(id);
    setQuestionBusy(old => ({ ...old, [id]: true }));
    return true;
  }

  function unlockQuestion(id) {
    questionLocks.current.delete(id);
    setQuestionBusy(old => ({ ...old, [id]: false }));
  }

  const loadAvailability = useCallback(() => practiceRead(api + "/availability", token)
    .then(x => setAvailable(x.parts || [])).catch(() => { setAvailable([]); setError("Không thể tải số lượng bài luyện hợp lệ."); }), [token]);
  const loadActiveList = useCallback(() => practiceRead(api + "/active-list", token)
    .then(x => setActiveAttempts(x.items || [])).catch(() => { setActiveAttempts([]); setError("Không thể tải bài luyện đang làm."); }), [token]);
  useEffect(() => {
    let live = true;
    if (routeAttemptId) {
      if (loadedAttempt.current !== routeAttemptId) {
        setBusy(true);
        practiceRead(`${api}/${routeAttemptId}`, token).then(data => { if (live) showAttempt(data); })
          .catch(() => { if (live) setError("Không thể khôi phục bài luyện đang làm."); })
          .finally(() => { if (live) setBusy(false); });
      }
    } else if (routeReviewId) {
      if (loadedReview.current !== routeReviewId) {
        setBusy(true);
        practiceRead(`${api}/history/${routeReviewId}`, token).then(data => {
          if (live) { loadedReview.current = routeReviewId; setAttempt(data); setCurrent(0); setFeedback({}); setSummary(null); }
        }).catch(() => { if (live) setError("Không thể tải bài đã luyện."); })
          .finally(() => { if (live) setBusy(false); });
      }
    } else {
      loadedAttempt.current = null; loadedReview.current = null;
      loadAvailability(); loadActiveList();
    }
    return () => { live = false; };
  }, [token, routeAttemptId, routeReviewId, loadAvailability, loadActiveList]);

  useEffect(() => {
    if (routeAttemptId || routeReviewId) return;
    let live = true;
    const query = new URLSearchParams({ page: String(historyPage), pageSize: String(historyPageSize), sort: historySort });
    if (historyPart) query.set("part", String(historyPart));
    practiceRead(`${api}/history?${query}`, token).then(data => {
      if (live) { setHistory(data); setHistoryError(""); setHistoryLoading(false); }
    }).catch(() => {
      if (live) { setHistoryError("HISTORY_LOAD_FAILED"); setHistoryLoading(false); }
    });
    return () => { live = false; };
  }, [token, routeAttemptId, routeReviewId, historyPart, historySort, historyPage, historyRetry]);

  async function start() {
    if (startLock.current) return;
    startLock.current = true; setBusy(true); setError(""); setFeedback({}); setSummary(null);
    try {
      const data = await apiRequest(api + "/start", { method: "POST", body: { part, difficulty, preset }, token });
      showAttempt(data);
      setSearchParams({ attempt: String(data.attemptId) });
    } catch (e) {
      setError(e.data?.message || "Không thể bắt đầu bài luyện. Vui lòng thử lại.");
      if (e.data?.code === "PRACTICE_ACTIVE_EXISTS") loadActiveList();
    }
    finally { startLock.current = false; setBusy(false); }
  }

  async function check(question) {
    const id = question.questionOccurrenceId;
    const selectedOption = feedback[id]?.selectedOption;
    if (!selectedOption || feedback[id]?.checked || !lockQuestion(id)) return;
    try {
      const result = await apiRequest(`${api}/${attempt.attemptId}/questions/${id}/check`,
        { method: "POST", body: { selectedOption }, token });
      setFeedback(current => ({ ...current, [id]: { ...result, checked: true, result: true } }));
    } catch (e) { setError(e.data?.message || "Không thể kiểm tra đáp án. Vui lòng thử lại."); }
    finally { unlockQuestion(id); }
  }

  async function seeAnswer(questionId) {
    if (!lockQuestion(questionId)) return;
    try {
      const existing = feedback[questionId];
      const result = existing?.reveal ? existing :
        await apiRequest(`${api}/${attempt.attemptId}/questions/${questionId}/answer`, { token });
      setFeedback(current => ({ ...current, [questionId]: answerViewed(current[questionId], result) }));
    } catch (e) { setError(e.data?.message || "Không thể xem đáp án. Vui lòng thử lại."); }
    finally { unlockQuestion(questionId); }
  }

  async function redo(questionId) {
    if (!lockQuestion(questionId)) return;
    try {
      await apiRequest(`${api}/${attempt.attemptId}/questions/${questionId}/redo`, { method: "POST", token });
      setFeedback(current => ({ ...current, [questionId]: { checked: false, selectedOption: null } }));
    } catch (e) { setError(e.data?.message || "Không thể làm lại câu này. Vui lòng thử lại."); }
    finally { unlockQuestion(questionId); }
  }

  async function finish() {
    if (busy || questionLocks.current.size > 0) return;
    setBusy(true);
    try {
      setSummary(await apiRequest(`${api}/${attempt.attemptId}/finish`, { method: "POST", token }));
      loadedReview.current = attempt.attemptId;
      setSearchParams({ review: String(attempt.attemptId) }, { replace: true });
    }
    catch (e) { setError(e.data?.message || "Không thể hoàn thành bài luyện. Vui lòng thử lại."); }
    finally { setBusy(false); }
  }

  function review(id) {
    setError("");
    setSearchParams({ review: String(id) });
  }

  const questions = attempt?.questions || [];
  const activeReview = attempt?.isReview;
  const counts = available?.find(x => x.part === part)?.difficulties || [];
  const selectedAvailability = counts.find(x => x.difficulty === difficulty);
  const canStart = selectedAvailability?.presets?.find(x => x.preset === preset)?.available === true;
  const activeQuestion = questions[current];
  const activeGroup = attempt?.groups?.find(g => g.groupId === activeQuestion?.groupId);
  const currentUnlocked = activeQuestion && (checked(activeQuestion, feedback) ||
    feedback[activeQuestion.questionOccurrenceId]?.viewed);
  const leave = () => { setAttempt(null); setSummary(null); setHistoryLoading(true); setSearchParams({}); };
  return <SiteLayout className="vocab-page"><SessionBoundary><div className="site-container practice-page practice-parts">
    <div className="page-heading"><span><Link to="/practice">Luyện tập</Link> / TOEIC theo Part</span>
      <h1>Luyện tập TOEIC theo Part</h1><p>Luyện không giới hạn thời gian, kiểm tra từng câu và xem lại lời giải.</p></div>
    {error && <p className="vocab-error" role="alert">{error}</p>}
    {busy && !attempt && (routeAttemptId || routeReviewId) && <p role="status">Đang tải bài luyện tập…</p>}
    {!attempt && !routeAttemptId && !routeReviewId && <>
      <p><Link to="/practice/vocabulary">Luyện từ vựng</Link></p>
      <section className="vocab-panel"><h2>Bài đang làm</h2>
        {activeAttempts === null ? <p role="status">Đang tải…</p> : activeAttempts.length === 0 ?
          <p>Chưa có bài luyện nào đang làm.</p> : activeAttempts.map(row =>
            <article className="practice-part-history" key={row.attemptId}>
              <span>Part {row.part} · {row.checkedQuestions}/{row.totalQuestions} câu đã kiểm tra · {new Date(row.startedAt).toLocaleString("vi-VN")}</span>
              <button type="button" className="outline-button" onClick={() => { setError(""); setSearchParams({ attempt: String(row.attemptId) }); }}>Tiếp tục</button>
            </article>)}</section>
      <section className="vocab-panel"><h2>Chọn bài luyện</h2>
        {available === null && <p role="status">Đang tải số lượng câu hỏi và nhóm hợp lệ…</p>}
        <label>Part<select value={part} onChange={e => { setPart(Number(e.target.value)); setPreset("SHORT"); }}>{parts.map((name, i) =>
          <option key={i + 1} value={i + 1}>Part {i + 1} · {name}</option>)}</select></label>
        <label>Độ khó<select value={difficulty} onChange={e => { setDifficulty(Number(e.target.value)); setPreset("SHORT"); }}>{[1,2,3].map(x =>
          <option key={x} value={x}>Mức {x} · {counts.find(c => c.difficulty === x)?.validUnitCount || 0} {counts.find(c => c.difficulty === x)?.unitType === "GROUP" ? "nhóm" : "câu"} hợp lệ</option>)}</select></label>
        <fieldset className="practice-part-presets"><legend>Mức luyện tập</legend>
          {(selectedAvailability?.presets || []).map(option => <label key={option.preset}>
            <input type="radio" name="practice-preset" value={option.preset} checked={preset === option.preset}
              disabled={!option.available} onChange={() => setPreset(option.preset)} />
            {option.preset} · {option.targetUnitCount} {selectedAvailability.unitType === "GROUP" ? "nhóm" : "câu"}
          </label>)}
        </fieldset>
        {!canStart && <p>Chưa đủ nội dung hợp lệ cho mức luyện tập này.</p>}
        <button className="primary-button" disabled={busy || !canStart} onClick={start}>Bắt đầu luyện</button>
      </section>
      <PracticeHistoryDashboard history={history} loading={historyLoading} error={historyError}
        partFilter={historyPart} sort={historySort} page={historyPage}
        onPartChange={value => { if (value === historyPart) return;
          setHistoryPart(value); setHistoryPage(1); setHistoryLoading(true); setHistoryError(""); }}
        onSortChange={value => { if (value === historySort) return;
          setHistorySort(value); setHistoryPage(1); setHistoryLoading(true); setHistoryError(""); }}
        onPageChange={value => { if (value === historyPage) return;
          setHistoryPage(value); setHistoryLoading(true); setHistoryError(""); }}
        onRetry={() => { setHistoryLoading(true); setHistoryError(""); setHistoryRetry(value => value + 1); }}
        onReview={review} />
    </>}
    {!attempt && (routeAttemptId || routeReviewId) && !busy && <button type="button" className="outline-button" onClick={leave}>← Quay lại chọn bài</button>}
    {attempt && !activeReview && !summary && <>
      <button className="vocab-text-button" onClick={leave}>← Rời bài luyện</button>
      <h2>Part {attempt.part} · {attempt.preset ? `${attempt.preset} · ` : ""}{attempt.selectedUnitCount} {attempt.part === 3 || attempt.part === 4 || attempt.part === 6 || attempt.part === 7 ? "nhóm" : "câu"} · {attempt.actualQuestionCount} câu hỏi</h2>
      {activeGroup && <PracticeGroup key={activeGroup.groupId} group={activeGroup} part={attempt.part} token={token}
        revealedContext={questions.filter(q => q.groupId === activeGroup.groupId)
          .map(q => feedback[q.questionOccurrenceId]?.reveal?.context).find(Boolean)} />}
      {activeQuestion && <PracticeQuestion key={activeQuestion.questionOccurrenceId} question={activeQuestion}
        transcriptContext={activeGroup?.context}
        value={feedback[activeQuestion.questionOccurrenceId]?.selectedOption} result={feedback[activeQuestion.questionOccurrenceId]}
        pending={!!questionBusy[activeQuestion.questionOccurrenceId]} onSelect={value =>
          setFeedback(old => ({ ...old, [activeQuestion.questionOccurrenceId]: { ...old[activeQuestion.questionOccurrenceId], selectedOption: value } }))} onCheck={() => check(activeQuestion)}
        onSeeAnswer={() => seeAnswer(activeQuestion.questionOccurrenceId)} onRedo={() => redo(activeQuestion.questionOccurrenceId)}
        onNext={current < questions.length - 1 && currentUnlocked ? () => setCurrent(current + 1) : null} />}
      <QuestionNavigation current={current} total={questions.length} onChange={setCurrent}
        canPrevious={current > 0} canNext={!!currentUnlocked && canNavigateTo(questions, feedback, current + 1)} />
      <button className="primary-button" disabled={busy || Object.values(questionBusy).some(Boolean)} onClick={finish}>Hoàn thành bài luyện</button>
    </>}
    {routeReviewId === summary?.attemptId && <section className="vocab-panel"><h2>Kết quả luyện tập</h2>
      <p>Tổng: {summary.total} · Đã trả lời: {summary.attempted} · Đúng: {summary.correct} · Sai: {summary.incorrect}</p>
      <p>Chưa trả lời: {summary.unanswered} · Chính xác: {summary.accuracy}%</p>
      <button className="primary-button" onClick={leave}>Luyện tiếp</button></section>}
    {activeReview && <><button className="vocab-text-button" onClick={leave}>← Lịch sử</button>
      <h2>Xem lại bài · Part {attempt.part}</h2>
      {activeGroup && <PracticeGroup key={activeGroup.groupId} group={activeGroup} part={attempt.part} token={token} review />}
      {activeQuestion && <PracticeQuestion key={activeQuestion.questionOccurrenceId} question={activeQuestion}
        transcriptContext={activeGroup?.context} review />}
      <QuestionNavigation current={current} total={questions.length} onChange={setCurrent}
        canPrevious={current > 0} canNext={current < questions.length - 1} /></>}
  </div></SessionBoundary></SiteLayout>;
}

export function QuestionNavigation({ current, total, onChange, canPrevious, canNext }) {
  return <nav className="practice-part-navigation" aria-label="Chuyển câu hỏi">
    <button type="button" className="outline-button" disabled={!canPrevious} onClick={() => onChange(current - 1)}>← Câu trước</button>
    <span>Câu {current + 1} / {total}</span>
    <button type="button" className="outline-button" disabled={!canNext} onClick={() => onChange(current + 1)}>Câu tiếp →</button>
  </nav>;
}

export function PracticeGroup({ group, part, token, revealedContext }) {
  return <section className="practice-part-source">
    {group.audioUrl && <PracticeAudio endpoint={group.audioUrl} token={token} />}
    {group.imageUrl && <div className={part === 1 ? "practice-part-one-image" : undefined}>
      <PracticeImage endpoint={group.imageUrl} token={token} alt="Hình minh họa" /></div>}
    {part > 2 && (group.context || revealedContext) && <p className="practice-part-context">{group.context || revealedContext}</p>}
    {group.documents?.map(document => <DocumentRenderer key={document.order} document={document} token={token}
      ImageComponent={PracticeImage} />)}
  </section>;
}

export function PracticeQuestion({ question: q, transcriptContext, value, result, onSelect, onCheck, onSeeAnswer, onRedo, onNext, pending = false, review = false }) {
  const revealed = result?.reveal;
  const lettersOnly = q.part <= 2;
  const isChecked = !!result?.checked;
  const visibleTranscript = review ? q : revealed;
  const choices = [["A", revealed?.phuongAnA ?? q.phuongAnA], ["B", revealed?.phuongAnB ?? q.phuongAnB],
    ["C", revealed?.phuongAnC ?? q.phuongAnC], ["D", revealed?.phuongAnD ?? q.phuongAnD]]
    .filter(([letter, text]) => lettersOnly ? q.part !== 2 || letter !== "D" : !!text);
  return <article className="practice-part-question">
    <h3>Câu {q.thuTu}</h3>
    {q.part > 2 && (revealed?.noiDung ?? q.noiDung) && <p>{revealed?.noiDung ?? q.noiDung}</p>}
    <div className="practice-part-options">{choices.map(([letter, text]) => <label key={letter}>
      <input type="radio" name={`q-${q.questionOccurrenceId}`} checked={(review ? q.selectedOption : value) === letter} disabled={review || isChecked || pending}
        onChange={() => onSelect?.(letter)} /> <b>{letter}.</b> {!lettersOnly && text}
    </label>)}</div>
    {lettersOnly && visibleTranscript && <div className="practice-part-transcript"><h4>Nội dung nghe</h4>
      {(transcriptContext || visibleTranscript.context) ? <p>{transcriptContext || visibleTranscript.context}</p> : <>
        {visibleTranscript.noiDung && <p>{visibleTranscript.noiDung}</p>}
        {[["A", visibleTranscript.phuongAnA], ["B", visibleTranscript.phuongAnB],
          ["C", visibleTranscript.phuongAnC], ["D", visibleTranscript.phuongAnD]]
          .filter(([, text]) => !!text).map(([letter, text]) => <p key={letter}>{letter}. {text}</p>)}
      </>}
    </div>}
    {review ? <div className="practice-feedback"><p>Đã chọn: {q.selectedOption || "Chưa trả lời"} · Đáp án đúng: {q.correctOption}</p>
      {q.explanation && <p>Giải thích: {q.explanation}</p>}</div> : <>
      {(result?.viewed || isChecked) &&
      <div className="practice-feedback">
        <p>{isChecked ? <><b>{result.isCorrect ? "Đúng" : "Sai"}</b> · Đã chọn: {result.selectedOption}</> : "Đã xem đáp án"} · Đáp án đúng: {result.correctOption}</p>
        {result.explanation && <p>Giải thích: {result.explanation}</p>}
        <div className="practice-part-actions"><button type="button" className="outline-button" disabled={pending} onClick={onRedo}>Làm lại câu này</button>
          <button type="button" className="vocab-text-button" disabled={pending} onClick={onSeeAnswer}>Xem đáp án</button>
          {onNext && <button type="button" className="primary-button" disabled={pending} onClick={onNext}>Câu tiếp →</button>}</div>
      </div>}
      {!isChecked && !result?.viewed &&
      <div className="practice-part-actions">
        <button type="button" className="vocab-text-button" disabled={pending} onClick={onSeeAnswer}>Xem đáp án</button>
        <button type="button" className="practice-check-button" disabled={!value || pending} onClick={onCheck}>Kiểm tra câu này</button>
      </div>}
      {!isChecked && result?.viewed && <button type="button" className="practice-check-button" disabled={!value || pending} onClick={onCheck}>Kiểm tra câu này</button>}</>}
  </article>;
}

function PracticeAudio({ endpoint, token }) {
  const [url, setUrl] = useState("");
  const [status, setStatus] = useState("loading");
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(endpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); setStatus("ready"); }
    }).catch(() => { if (!controller.signal.aborted) setStatus("error"); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token, retry]);
  if (status === "error") return <div role="alert">Không thể tải âm thanh. <button type="button" className="outline-button"
    onClick={() => { setStatus("loading"); setRetry(value => value + 1); }}>Thử lại</button></div>;
  return status === "ready" && url ? <audio controls preload="metadata" src={url}
    onError={() => setStatus("error")} /> : <p role="status">Đang tải âm thanh…</p>;
}

function PracticeImage({ endpoint, token, alt }) {
  const [url, setUrl] = useState("");
  const [status, setStatus] = useState("loading");
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let live = true;
    let objectUrl = "";
    examMediaBlob(endpoint, token).then(blob => {
      if (live) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); setStatus("ready"); }
    }).catch(() => { if (live) setStatus("error"); });
    return () => { live = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token, retry]);
  if (status === "error") return <p role="alert">Không thể tải hình ảnh. <button type="button" className="outline-button"
    onClick={() => { setStatus("loading"); setRetry(value => value + 1); }}>Thử lại</button></p>;
  return status === "ready" && url ? <img className="exam-image" src={url} alt={alt}
    onError={() => setStatus("error")} /> : <p role="status">Đang tải hình ảnh…</p>;
}
