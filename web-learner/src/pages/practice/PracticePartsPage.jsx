import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import SessionBoundary from "../../components/auth/SessionBoundary";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import { practiceRead } from "../../services/practiceRead";
import { useContentRevision } from "../../services/contentRevisions";
import { answerViewed, canNavigateTo, checked, restoredFeedback } from "./practiceFlow.mjs";
import DocumentRenderer from "../exam/DocumentRenderer";
import PartSevenTranslationView from "../exam/PartSevenTranslationView";
import { examMediaBlob } from "../../services/examApi";
import "../../styles/exam.css";
import "../../styles/vocabulary.css";
import "../../styles/practice-parts.css";
import "../../styles/learner-journey.css";
import "../../styles/practice-parts-studio.css";

const api = "/api/practice";
const partLabels = ["Mô tả tranh", "Hỏi và đáp", "Hội thoại", "Bài nói ngắn", "Hoàn thành câu", "Hoàn thành đoạn văn", "Đọc hiểu"];
const difficultyLabels = { 1: "Dễ", 2: "Vừa", 3: "Khó" };
const presetLabels = { SHORT: "Bài ngắn", MEDIUM: "Bài vừa", LONG: "Bài dài" };

export default function PracticePartsPage() {
  const { token } = useAuth();
  const contentRevision = useContentRevision("PRACTICE");
  const [searchParams, setSearchParams] = useSearchParams();
  const routeAttemptId = Number(searchParams.get("attempt")) || null;
  const queryPart = Number(searchParams.get("part"));
  const suggestedPart = queryPart >= 1 && queryPart <= 7 ? queryPart : null;
  const [available, setAvailable] = useState(null);
  const [selectedPart, setSelectedPart] = useState(null);
  const part = routeAttemptId ? selectedPart ?? 5 : selectedPart ?? suggestedPart ?? 5;
  const [difficulty, setDifficulty] = useState(1);
  const [preset, setPreset] = useState("SHORT");
  const [current, setCurrent] = useState(0);
  const [attemptData, setAttempt] = useState(null);
  const [feedback, setFeedback] = useState({});
  const [summary, setSummary] = useState(null);
  const [stats, setStats] = useState(null);
  const [statsError, setStatsError] = useState("");
  const [statsRetry, setStatsRetry] = useState(0);
  const [activeAttempts, setActiveAttempts] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [questionBusy, setQuestionBusy] = useState({});
  const startLock = useRef(false);
  const questionLocks = useRef(new Set());
  const loadedAttempt = useRef(null);
  const attempt = routeAttemptId && attemptData?.attemptId === routeAttemptId ? attemptData : null;

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
    } else {
      loadedAttempt.current = null;
      loadAvailability(); loadActiveList();
    }
    return () => { live = false; };
  }, [token, routeAttemptId, loadAvailability, loadActiveList, contentRevision]);

  useEffect(() => {
    if (routeAttemptId) return;
    let live = true;
    practiceRead(`${api}/statistics`, token).then(data => {
      if (live) { setStats(data.parts || []); setStatsError(""); }
    }).catch(() => {
      if (live) setStatsError("Không thể tải thống kê luyện tập.");
    });
    return () => { live = false; };
  }, [token, routeAttemptId, statsRetry]);

  useEffect(() => {
    if (searchParams.has("review")) setSearchParams({}, { replace: true });
  }, [searchParams, setSearchParams]);

  async function start() {
    if (startLock.current) return;
    startLock.current = true; setBusy(true); setError(""); setFeedback({}); setSummary(null);
    try {
      const fromRecommendation = searchParams.get("recommended") === "1" && queryPart === part;
      const data = await apiRequest(api + "/start", { method: "POST",
        body: { part, difficulty: selectedDifficulty, preset: selectedPreset, fromRecommendation }, token });
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
      if (attempt.part === 7) {
        try {
          const fresh = await practiceRead(`${api}/${attempt.attemptId}`, token);
          setAttempt(current => current?.attemptId === fresh.attemptId ? { ...current, groups: fresh.groups } : current);
        } catch { /* Đáp án đã được lưu; bản dịch sẽ tải lại khi mở bài luyện. */ }
      }
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
      const groupId = attempt.questions.find(item => item.questionOccurrenceId === questionId)?.groupId;
      if (attempt.part === 7 && groupId) setAttempt(current => current && { ...current,
        groups: current.groups.map(group => group.groupId === groupId ?
          { ...group, translation: null } : group) });
    } catch (e) { setError(e.data?.message || "Không thể làm lại câu này. Vui lòng thử lại."); }
    finally { unlockQuestion(questionId); }
  }

  async function finish() {
    if (busy || questionLocks.current.size > 0) return;
    setBusy(true);
    try {
      const completed = await apiRequest(`${api}/${attempt.attemptId}/finish`, { method: "POST", token });
      setSummary(completed);
      setAttempt(null);
      setSearchParams({}, { replace: true });
      setStatsRetry(value => value + 1);
    }
    catch (e) { setError(e.data?.message || "Không thể hoàn thành bài luyện. Vui lòng thử lại."); }
    finally { setBusy(false); }
  }

  const questions = attempt?.questions || [];
  const counts = available?.find(x => x.part === part)?.difficulties || [];
  const hasReadyPreset = row => row.presets?.some(option => option.available) === true;
  const selectedAvailability = counts.find(row => row.difficulty === difficulty && hasReadyPreset(row)) ||
    counts.find(hasReadyPreset) || counts.find(row => row.difficulty === difficulty) || counts[0];
  const selectedDifficulty = selectedAvailability?.difficulty ?? difficulty;
  const availablePresets = selectedAvailability?.presets?.filter(option => option.available) || [];
  const selectedPreset = availablePresets.some(option => option.preset === preset) ? preset : availablePresets[0]?.preset ?? preset;
  const canStart = availablePresets.length > 0;
  const selectedStats = stats?.find(row => row.part === part);
  const choosePart = nextPart => { setSelectedPart(nextPart); setPreset("SHORT"); };
  const activeQuestion = questions[current];
  const activeGroup = attempt?.groups?.find(g => g.groupId === activeQuestion?.groupId);
  const currentUnlocked = activeQuestion && (checked(activeQuestion, feedback) ||
    feedback[activeQuestion.questionOccurrenceId]?.viewed);
  const leave = () => { setAttempt(null); setSummary(null); setSearchParams({}); };
  return <SiteLayout className="vocab-page" aiContext={attempt && activeQuestion ? { type: "PRACTICE", attemptId: attempt.attemptId, questionId: activeQuestion.questionOccurrenceId } : null}><SessionBoundary><div className="site-container practice-page practice-parts">
    <div className="page-heading practice-hub-heading"><span><Link to="/practice">Luyện tập</Link> / Theo Part</span>
      <h1><span className="practice-heading-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12 6.5c-2.2-1.7-5.1-2.2-8-1.5v12c2.9-.7 5.8-.2 8 1.5 2.2-1.7 5.1-2.2 8-1.5V5c-2.9-.7-5.8-.2-8 1.5Z"/><path d="M12 6.5v12"/></svg></span>Hôm nay bạn muốn luyện gì?</h1>
      <p>Chọn Part và bắt đầu với một bài luyện vừa sức.</p>
      <Link className="practice-back-link" to="/practice">← Các hình thức luyện tập</Link></div>
    {error && <p className="vocab-error" role="alert">{error}</p>}
    {error && !attempt && !routeAttemptId && <button className="outline-button" type="button"
      onClick={() => { setError(""); loadAvailability(); loadActiveList(); setStatsRetry(value => value + 1); }}>Thử tải lại</button>}
    {busy && !attempt && routeAttemptId && <p role="status">Đang tải bài luyện tập…</p>}
    {summary && <section className="practice-complete" role="status"><span className="practice-kicker">HOÀN THÀNH BÀI LUYỆN</span><h2>Part {summary.part} · {partLabels[summary.part - 1]}</h2>
      <div className="practice-complete-score"><strong>{summary.accuracy}%</strong><span>{summary.correct}/{summary.attempted} câu đúng</span></div>
      <p>Bạn có thể chọn một bài mới bất cứ lúc nào. Thống kê Part đã được cập nhật.</p>
      <button className="primary-button" type="button" onClick={() => setSummary(null)}>Tiếp tục luyện tập →</button></section>}
    {!attempt && !routeAttemptId && <div className="practice-parts-hub">
      <div className="practice-parts-main">
        <section className="practice-part-select" aria-labelledby="practice-select-title"><div className="practice-select-heading"><div><span className="practice-kicker">CHỌN NỘI DUNG</span><h2 id="practice-select-title">Bảy Part, hai kỹ năng</h2></div><p>Chạm vào một Part để xem bài luyện phù hợp.</p></div>
          {available === null && <p role="status" className="practice-data-loading">Đang kiểm tra bài luyện hiện có…</p>}
          {[{ label: "Listening", range: "PART 1–4", start: 1, end: 4, icon: "♫" }, { label: "Reading", range: "PART 5–7", start: 5, end: 7, icon: "Aa" }].map(group =>
            <div className="practice-skill-group" key={group.label}><div className="practice-skill-heading"><span className="practice-skill-icon" aria-hidden="true">{group.icon}</span><h3>{group.label}</h3><small>{group.range}</small></div>
              <div className="practice-part-picker" role="group" aria-label={`Chọn Part ${group.label}`}>{partLabels.slice(group.start - 1, group.end).map((name, index) => {
                const number = group.start + index;
                const ready = available?.find(item => item.part === number)?.difficulties?.some(row => row.presets?.some(option => option.available)) === true;
                return <button type="button" key={number} aria-pressed={part === number} disabled={available !== null && !ready} onClick={() => choosePart(number)}>
                  <span className="practice-tile-number">{String(number).padStart(2, "0")}</span><span className="practice-tile-copy"><strong>Part {number}</strong><small>{name}</small></span>
                  {part === number && <span className="practice-tile-check" aria-hidden="true">✓</span>}</button>;
              })}</div></div>)}
        </section>
        <section className="practice-config-panel" aria-labelledby="practice-config-title"><span className="practice-kicker">BÀI LUYỆN CỦA BẠN</span>
          <h2 id="practice-config-title">Part {part} <span>· {partLabels[part - 1]}</span></h2>
          <div className="practice-performance" aria-label={`Kết quả Part ${part}`}>
            {statsError ? <div className="practice-performance-empty" role="alert">{statsError} <button type="button" onClick={() => { setStatsError(""); setStatsRetry(value => value + 1); }}>Thử lại</button></div> :
              stats === null ? <p role="status" className="practice-performance-empty">Đang tải kết quả gần nhất…</p> :
              selectedStats?.completedSessions > 0 ? <><div><small>Lần gần nhất</small><strong>{selectedStats.latestAccuracy}%</strong><span>chính xác</span></div>
                <div><small>Tích lũy</small><strong>{selectedStats.totalCorrect}/{selectedStats.totalQuestions}</strong><span>câu đúng</span></div></> :
              <p className="practice-performance-empty">Chưa có kết quả <span>Hoàn thành một bài để xem tiến bộ của bạn.</span></p>}
          </div>
          <div className="practice-config-fields"><label htmlFor="practice-difficulty">Độ khó</label>
            <select id="practice-difficulty" value={selectedDifficulty} disabled={!counts.length} onChange={event => { setDifficulty(Number(event.target.value)); setPreset("SHORT"); }}>
              {counts.length ? counts.map(row => <option key={row.difficulty} value={row.difficulty} disabled={!hasReadyPreset(row)}>
                {difficultyLabels[row.difficulty] || `Mức ${row.difficulty}`} · {row.validUnitCount} {row.unitType === "GROUP" ? "nhóm" : "câu"} sẵn có</option>) : <option value={difficulty}>Chưa có mức độ</option>}
            </select></div>
          <fieldset className="practice-part-presets"><legend>Độ dài bài luyện</legend>
            {availablePresets.length > 0 ? availablePresets.map(option => <label key={option.preset}>
              <input type="radio" name="practice-preset" value={option.preset} checked={selectedPreset === option.preset} onChange={() => setPreset(option.preset)} />
              <span className="practice-preset-content"><strong>{presetLabels[option.preset] || option.preset}</strong><span>{option.targetUnitCount} {selectedAvailability.unitType === "GROUP" ? "nhóm" : "câu"}</span></span>
            </label>) : <p className="practice-no-presets">{available === null ? "Đang tải các bài luyện…" : "Chưa có bài luyện phù hợp với Part này."}</p>}
          </fieldset>
          <div className="practice-selection-action"><button className="primary-button" disabled={busy || !canStart} onClick={start}>{busy ? "Đang bắt đầu…" : "Bắt đầu luyện tập"}<span aria-hidden="true">→</span></button></div>
        </section>
      </div>
      {activeAttempts?.length > 0 && <section className="practice-active-panel"><div><span className="practice-kicker">TIẾP TỤC</span><h2>Bài đang làm</h2></div><div className="practice-active-list">{activeAttempts.map(row =>
        <article className="practice-part-history" key={row.attemptId}><div className="practice-active-details"><strong>Part {row.part} · {partLabels[row.part - 1]}</strong><span>{row.checkedQuestions}/{row.totalQuestions} câu đã kiểm tra</span></div>
          <button type="button" onClick={() => { setError(""); setSearchParams({ attempt: String(row.attemptId) }); }}>Tiếp tục →</button></article>)}</div></section>}
      <details className="practice-progress-details"><summary><span><small>NHÌN LẠI QUÁ TRÌNH HỌC</small><strong>Tiến bộ của bạn</strong><em>So sánh kết quả và xem thống kê từng Part</em></span><DisclosureToggle /></summary><div className="practice-progress-body">
        {statsError && <div className="practice-progress-error" role="alert">{statsError} <button type="button" onClick={() => { setStatsError(""); setStatsRetry(value => value + 1); }}>Thử lại</button></div>}
        {!stats && !statsError && <p role="status">Đang tải thống kê…</p>}
        {stats && !stats.some(row => row.completedSessions > 0) && <div className="practice-progress-empty"><strong>Chưa có kết quả luyện tập</strong><p>Sau khi hoàn thành bài đầu tiên, tiến bộ của bạn sẽ hiện ở đây.</p></div>}
        {stats?.some(row => row.completedSessions > 0) && <div className="practice-progress-layout"><div className="practice-progress-comparison" role="group" aria-label="So sánh tiến bộ bảy Part">
          {partLabels.map((name, index) => { const row = stats.find(item => item.part === index + 1); const completed = row?.completedSessions > 0;
            return <button type="button" key={name} aria-pressed={part === index + 1} onClick={() => choosePart(index + 1)}><span>Part {index + 1} <small>{name}</small></span>
              <span className="practice-comparison-track" aria-hidden="true"><i style={{ width: completed ? `${Math.max(0, Math.min(100, Number(row.overallAccuracy) || 0))}%` : "0%" }} /></span>
              <strong>{completed ? `${row.overallAccuracy}%` : "Chưa luyện"}</strong></button>; })}</div>
          <div className="practice-progress-focus"><span className="practice-kicker">CHI TIẾT PART {part}</span><h3>{partLabels[part - 1]}</h3>
            {selectedStats?.completedSessions > 0 ? <><div className="practice-progress-metrics"><div><small>Lượt hoàn thành</small><strong>{selectedStats.completedSessions}</strong></div>
              <div><small>Câu đã làm</small><strong>{selectedStats.totalQuestions}</strong></div><div><small>Câu đúng</small><strong>{selectedStats.totalCorrect}</strong></div>
              <div><small>Chính xác tích lũy</small><strong>{selectedStats.overallAccuracy}%</strong></div><div><small>Lần gần nhất</small><strong>{selectedStats.latestAccuracy}%</strong></div></div>
              <p>{selectedStats.lastPracticedAt ? `Luyện gần nhất: ${new Date(selectedStats.lastPracticedAt).toLocaleString("vi-VN")}` : ""}</p></> : <p className="practice-progress-empty-note">Chưa có kết quả hoàn thành cho Part này.</p>}
          </div></div>}
      </div></details>
    </div>}
    {!attempt && routeAttemptId && !busy && <button type="button" className="outline-button" onClick={leave}>← Quay lại chọn bài</button>}
    {attempt && !summary && <div className="practice-session" data-part={attempt.part}>
      <div className="practice-session-top"><div>
      <h2>Part {attempt.part} · {attempt.preset ? `${attempt.preset} · ` : ""}{attempt.selectedUnitCount} {attempt.part === 3 || attempt.part === 4 || attempt.part === 6 || attempt.part === 7 ? "nhóm" : "câu"} · {attempt.actualQuestionCount} câu hỏi</h2>
      <p>Câu {current + 1} / {questions.length}</p></div>
      <button className="vocab-text-button" onClick={leave}>← Rời bài luyện</button></div>
      <div className="practice-session-layout"><div className="practice-session-content"><div className="practice-session-material">
      {activeGroup && <PracticeGroup key={activeGroup.groupId} group={activeGroup} part={attempt.part} token={token}
        revealedContext={questions.filter(q => q.groupId === activeGroup.groupId)
          .map(q => feedback[q.questionOccurrenceId]?.viewed ? feedback[q.questionOccurrenceId]?.reveal?.context : null)
          .find(Boolean)} />}
      {activeQuestion && <PracticeQuestion key={activeQuestion.questionOccurrenceId} question={activeQuestion}
        transcriptContext={activeGroup?.context}
        value={feedback[activeQuestion.questionOccurrenceId]?.selectedOption} result={feedback[activeQuestion.questionOccurrenceId]}
        pending={!!questionBusy[activeQuestion.questionOccurrenceId]} onSelect={value =>
          setFeedback(old => ({ ...old, [activeQuestion.questionOccurrenceId]: { ...old[activeQuestion.questionOccurrenceId], selectedOption: value } }))} onCheck={() => check(activeQuestion)}
        onSeeAnswer={() => seeAnswer(activeQuestion.questionOccurrenceId)} onRedo={() => redo(activeQuestion.questionOccurrenceId)}
        onNext={current < questions.length - 1 && currentUnlocked ? () => setCurrent(current + 1) : null} />}
      </div></div>
      <aside className="practice-session-navigator" aria-label="Tiến độ và điều hướng bài luyện">
        <details open><summary>Điều hướng câu hỏi</summary>
          <h3>Part {attempt.part}</h3>
          <PracticeQuestionNumbers questions={questions} feedback={feedback} current={current} onChange={setCurrent} />
        </details>
      <QuestionNavigation current={current} total={questions.length} onChange={setCurrent}
        canPrevious={current > 0} canNext={!!currentUnlocked && canNavigateTo(questions, feedback, current + 1)} />
      <button className="primary-button" disabled={busy || Object.values(questionBusy).some(Boolean)} onClick={finish}>Hoàn thành bài luyện</button>
      </aside></div>
    </div>}
  </div></SessionBoundary></SiteLayout>;
}

export function PracticeQuestionNumbers({ questions, feedback, current, onChange }) {
  return <ol className="practice-session-numbers" aria-label="Trạng thái từng câu hỏi">{questions.map((question, index) =>
    <li key={question.questionOccurrenceId}>
      <button type="button" aria-current={index === current ? "step" : undefined}
        aria-label={`Câu ${question.thuTu}`}
        data-state={checked(question, feedback) ? ((feedback[question.questionOccurrenceId]?.isCorrect ?? question.isCorrect) ? "correct" : "incorrect") : feedback[question.questionOccurrenceId]?.viewed ? "viewed" : "unchecked"}
        onClick={() => onChange(index)}>
        <span>{question.thuTu}</span><small>{checked(question, feedback) ? ((feedback[question.questionOccurrenceId]?.isCorrect ?? question.isCorrect) ? "Đúng" : "Sai") : feedback[question.questionOccurrenceId]?.viewed ? "Đã xem" : "Chưa kiểm tra"}</small>
      </button>
    </li>)}</ol>;
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
    {part === 7 && <PartSevenTranslationView translation={group.translation} documents={group.documents}
      token={token} ImageComponent={PracticeImage} />}
  </section>;
}

export function PracticeQuestion({ question: q, transcriptContext, value, result, onSelect, onCheck, onSeeAnswer, onRedo, onNext, pending = false, review = false }) {
  const answerVisible = review || !!result?.viewed;
  const revealed = answerVisible ? result?.reveal : null;
  const lettersOnly = q.part <= 2;
  const isChecked = !!result?.checked;
  const visibleTranscript = review ? q : revealed;
  const correctOption = review ? q.correctOption : result?.correctOption;
  const selectedOption = review ? q.selectedOption : result?.selectedOption;
  const choices = [["A", revealed?.phuongAnA ?? q.phuongAnA], ["B", revealed?.phuongAnB ?? q.phuongAnB],
    ["C", revealed?.phuongAnC ?? q.phuongAnC], ["D", revealed?.phuongAnD ?? q.phuongAnD]]
    .filter(([letter, text]) => lettersOnly ? q.part !== 2 || letter !== "D" : !!text);
  return <article className="practice-part-question">
    <h3>Câu {q.thuTu}</h3>
    {q.part > 2 && (revealed?.noiDung ?? q.noiDung) && <p>{revealed?.noiDung ?? q.noiDung}</p>}
    <div className="practice-part-options">{choices.map(([letter, text]) => <label key={letter}
      data-correct={(answerVisible && correctOption === letter) ||
        (!review && isChecked && result.isCorrect && selectedOption === letter) ? "true" : undefined}
      data-incorrect={isChecked && !result.isCorrect && selectedOption === letter ? "true" : undefined}>
      <input type="radio" name={`q-${q.questionOccurrenceId}`} checked={(review ? q.selectedOption : value) === letter} disabled={review || isChecked || pending}
        onChange={() => onSelect?.(letter)} /> <span><b>{letter}.</b> {!lettersOnly && text}</span>
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
      {isChecked && !answerVisible && <div className="practice-feedback practice-feedback-compact"
        data-state={result.isCorrect ? "correct" : "incorrect"} role="status">
        <p><b>{result.isCorrect ? "Bạn trả lời đúng." : "Bạn trả lời chưa đúng."}</b></p>
      </div>}
      {answerVisible && <div className="practice-feedback" data-state={isChecked ? result.isCorrect ? "correct" : "incorrect" : "viewed"}>
        {isChecked && <p><b>{result.isCorrect ? "Bạn trả lời đúng." : "Bạn trả lời chưa đúng."}</b></p>}
        <p className="practice-feedback-answer"><span>Đáp án đúng</span><strong>{correctOption}</strong></p>
        {result?.explanation && <p className="practice-feedback-explanation"><b>Giải thích</b><span>{result.explanation}</span></p>}
      </div>}
      <div className="practice-part-actions" data-state={answerVisible ? "viewed" : isChecked ? "checked" : "ready"}>
        {!answerVisible && <button type="button" className="outline-button" disabled={pending} onClick={onSeeAnswer}>Xem đáp án</button>}
        {(isChecked || answerVisible) && <button type="button" className="outline-button" disabled={pending} onClick={onRedo}>Làm lại câu này</button>}
        {!isChecked && !answerVisible && <button type="button" className="practice-check-button" disabled={!value || pending} onClick={onCheck}>Kiểm tra câu này</button>}
        {(isChecked || answerVisible) && onNext && <button type="button" className="primary-button" disabled={pending} onClick={onNext}>Câu tiếp →</button>}
      </div></>}
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
