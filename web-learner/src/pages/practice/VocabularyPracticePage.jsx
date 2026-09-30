import { useEffect, useReducer, useRef, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import { buildPracticeQuestions, practiceReducer, practiceResult } from "./vocabularyPractice";
import "../../styles/vocabulary.css";
import "../../styles/vocabulary-practice.css";

const ROOT = "/api/tu-vung";
const LOAD_ERROR = "Không thể tải dữ liệu luyện tập.";

export default function VocabularyPracticePage() {
  const { user, token } = useAuth();
  return <SiteLayout className="vocab-page"><SessionBoundary>
    <div className="site-container practice-page">
      <div className="page-heading">
        <span>Luyện tập / Luyện từ vựng</span>
        <h1>Luyện từ vựng</h1>
        <p>Kiểm tra khả năng nhớ từ với câu hỏi trắc nghiệm. Kết quả hoàn thành được lưu trong lịch sử luyện tập.</p>
      </div>
      {user && token ? <LearnerPractice key={token} token={token} /> :
        <section className="vocab-panel">
          <h2>Đăng nhập để luyện từ vựng</h2>
          <p>Chọn chủ đề và luyện những từ bạn muốn ôn sau khi đăng nhập.</p>
          <div className="practice-actions">
            <Link className="primary-button" to="/login">Đăng nhập</Link>
            <Link className="outline-button" to="/register">Đăng ký</Link>
            <Link className="vocab-text-button" to="/vocabulary">Học thử Office Essentials</Link>
          </div>
        </section>}
    </div>
  </SessionBoundary></SiteLayout>;
}

function LearnerPractice({ token }) {
  const [topics, setTopics] = useState([]);
  const [loading, setLoading] = useState(true);
  const [reload, setReload] = useState(0);
  const [topic, setTopic] = useState(null);
  const [mode, setMode] = useState("all");
  const [pool, setPool] = useState([]);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [session, dispatch] = useReducer(practiceReducer, null);
  const [saveState, setSaveState] = useState("");
  const submittedSessions = useRef(new WeakSet());
  const activeAttempt = useRef(0);

  useEffect(() => {
    let active = true;
    apiRequest(ROOT + "/topics", { token })
      .then(data => { if (active) setTopics(data); })
      .catch(() => { if (active) setError(LOAD_ERROR); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [token, reload]);

  const hasUnsavedAnswers = !!session && !session.finished && session.answers.length > 0;
  useEffect(() => {
    if (!hasUnsavedAnswers) return;
    const warnBeforeUnload = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warnBeforeUnload);
    return () => window.removeEventListener("beforeunload", warnBeforeUnload);
  }, [hasUnsavedAnswers]);

  function begin(targets, optionPool) {
    activeAttempt.current += 1;
    setSaveState("");
    const questions = buildPracticeQuestions(targets, optionPool);
    if (!questions.length) {
      setNotice("Chủ đề này chưa có đủ từ vựng để tạo bài luyện.");
      return;
    }
    setError("");
    setNotice(questions.length < targets.length ? "Một số từ chưa có dữ liệu phù hợp để tạo câu hỏi." : "");
    dispatch({ type: "start", questions });
  }

  async function startPractice() {
    if (starting) return;
    setStarting(true);
    setError("");
    setNotice("");
    try {
      const data = await apiRequest(ROOT + "/topics/" + topic.maChuDe + "/practice?mode=" + mode, { token });
      setPool(data.optionPool);
      if (mode === "review" && !data.words.length) {
        setNotice("Bạn không có từ nào cần ôn trong chủ đề này.");
      } else {
        begin(data.words, data.optionPool);
      }
    } catch {
      setError(LOAD_ERROR);
    } finally {
      setStarting(false);
    }
  }

  function leavePractice() {
    activeAttempt.current += 1;
    setSaveState("");
    if (hasUnsavedAnswers && !window.confirm("Thoát lượt luyện? Các câu trả lời trong lượt này sẽ không được lưu.")) return;
    dispatch({ type: "reset" });
    setTopic(null);
    setPool([]);
    setNotice("");
    setError("");
  }

  const question = session?.questions[session.position];
  const answer = session?.answers[session.position];
  const result = session?.finished ? practiceResult(session) : null;

  function advance() {
    if (!session || !answer) return;
    if (session.position + 1 < session.questions.length) {
      dispatch({ type: "next" });
      return;
    }
    if (submittedSessions.current.has(session)) return;
    submittedSessions.current.add(session);
    const attemptNumber = activeAttempt.current;
    dispatch({ type: "next" });
    setSaveState("saving");
    const answers = session.questions.map((item, index) => ({
      maTuVung: item.word.maTuVung,
      type: item.type,
      prompt: item.prompt,
      options: item.options,
      correctIndex: item.correctIndex,
      selectedIndex: session.answers[index].optionIndex,
    }));
    apiRequest(ROOT + "/practice-history", {
      token, method: "POST", body: { maChuDe: topic.maChuDe, answers }
    }).then(() => {
      if (activeAttempt.current === attemptNumber) setSaveState("saved");
    }).catch(() => {
      if (activeAttempt.current === attemptNumber) setSaveState("failed");
    });
  }

  return <>
    <div className="practice-history-link"><Link className="vocab-text-button" to="/practice/vocabulary/history"
      onClick={event => {
        if (hasUnsavedAnswers && !window.confirm("Thoát lượt luyện? Các câu trả lời trong lượt này sẽ không được lưu."))
          event.preventDefault();
      }}>Lịch sử luyện từ</Link></div>
    {loading && <p role="status">Đang tải dữ liệu luyện tập...</p>}
    {error && <div className="vocab-error" role="alert"><p>{error}</p>
      {!topic && <button className="outline-button" type="button" onClick={() => {
        setLoading(true); setError(""); setReload(value => value + 1);
      }}>Thử lại</button>}
    </div>}

    {!loading && !topic && <>
      <h2>Chọn chủ đề</h2>
      <div className="vocab-topic-grid">{topics.map(item =>
        <article className="vocab-topic-card" key={item.maChuDe}>
          <span className="vocab-pill available">{item.status}</span>
          <h2>{item.tenChuDe}</h2>
          <p>{item.totalWords} từ · {item.completedWords} từ đã nhớ</p>
          <button type="button" className="primary-button" onClick={() => {
            setTopic(item); setMode("all"); setError(""); setNotice("");
          }}>Chọn chủ đề</button>
        </article>)}</div>
      {!topics.length && !error && <p>Chưa có chủ đề từ vựng đang mở.</p>}
    </>}

    {topic && <>
      <button type="button" className="vocab-text-button" disabled={starting} onClick={leavePractice}>← Quay lại chọn chủ đề</button>
      {notice && <p role="status" className="vocab-note">{notice}</p>}

      {!session && <section className="vocab-panel practice-panel">
        <h2>{topic.tenChuDe}</h2>
        <p>{topic.totalWords} từ trong chủ đề</p>
        <fieldset className="practice-modes" disabled={starting}>
          <legend>Chọn bộ từ luyện tập</legend>
          <label><input type="radio" name="practice-mode" value="all" checked={mode === "all"}
            onChange={() => { setMode("all"); setNotice(""); }} /> Tất cả từ</label>
          <label><input type="radio" name="practice-mode" value="review" checked={mode === "review"}
            onChange={() => { setMode("review"); setNotice(""); }} /> Từ cần ôn</label>
        </fieldset>
        <p>Từ cần ôn gồm từ chưa học và từ chưa nhớ. Luyện tập không thay đổi trạng thái đã nhớ của bạn.</p>
        <button type="button" className="primary-button" disabled={starting} onClick={startPractice}>Bắt đầu luyện</button>
        {starting && <p role="status">Đang tải dữ liệu luyện tập...</p>}
      </section>}

      {session && !session.finished && <section className="vocab-panel practice-panel">
        <div className="practice-toolbar">
          <span>{topic.tenChuDe} · Câu {session.position + 1} / {session.questions.length}</span>
          <button type="button" className="vocab-text-button" onClick={leavePractice}>Thoát lượt luyện</button>
        </div>
        <p>{question.type === "word-to-meaning" ? "Chọn nghĩa tiếng Việt đúng:" : "Chọn từ tiếng Anh đúng:"}</p>
        <h2 className="practice-prompt">{question.prompt}</h2>
        {question.options.length < 4 && <p>Chủ đề có ít lựa chọn phù hợp; câu này có {question.options.length} đáp án.</p>}
        <div className="practice-options" aria-label="Các đáp án">
          {question.options.map((option, index) => <button type="button" key={index}
            className={"practice-option" + (answer && index === question.correctIndex ? " is-correct" :
              answer && index === answer.optionIndex ? " is-wrong" : "")}
            disabled={!!answer} onClick={() => dispatch({ type: "answer", optionIndex: index })}>
            <strong>{String.fromCharCode(65 + index)}.</strong> {option}
          </button>)}
        </div>
        {answer && <div className="practice-feedback" role="status">
          <strong>{answer.correct ? "Đúng" : "Sai"}</strong>
          <p>Đáp án đúng: {question.options[question.correctIndex]}</p>
          <button type="button" className="primary-button" onClick={advance}>
            {session.position + 1 === session.questions.length ? "Xem kết quả" : "Câu tiếp theo"}
          </button>
        </div>}
      </section>}

      {result && <section className="vocab-panel practice-panel">
        <h2>Kết quả luyện từ vựng</h2>
        <p className="practice-score">{result.correct}/{result.total} câu đúng · {result.percent}%</p>
        <p>Tổng số câu: {result.total} · Đúng: {result.correct} · Sai: {result.incorrect}</p>
        <h3>Từ cần xem lại</h3>
        {result.wrongWords.length ? <ul>{result.wrongWords.map(word =>
          <li key={word.maTuVung}><strong>{word.word}</strong> — {word.meaning}</li>)}</ul> :
          <p>Bạn đã trả lời đúng tất cả câu hỏi.</p>}
        <p role="status">{saveState === "saving" ? "Đang lưu kết quả..." :
          saveState === "saved" ? "Đã lưu kết quả luyện tập." :
          saveState === "failed" ? "Không thể lưu lịch sử luyện tập." : ""}</p>
        <p>Trạng thái học từ vựng của bạn không thay đổi.</p>
        <div className="practice-actions">
          {result.wrongWords.length > 0 && <button type="button" className="primary-button"
            onClick={() => begin(result.wrongWords, pool)}>Luyện lại từ sai</button>}
          <button type="button" className="outline-button" onClick={() => { setMode("all"); begin(pool, pool); }}>Luyện lại chủ đề</button>
          <button type="button" className="vocab-text-button" onClick={leavePractice}>Quay lại chọn chủ đề</button>
        </div>
      </section>}
    </>}
  </>;
}
