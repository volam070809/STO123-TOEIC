import { useEffect, useReducer, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { useAuth } from "../../contexts/AuthState";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { apiRequest } from "../../services/api";
import { buildPracticeQuestions, practiceReducer, practiceResult } from "./vocabularyPractice";
import { INITIAL_TOPIC_COUNT, topicCatalogView, topicGridStyle, useMobileTopicCatalog } from "./topicCatalog";
import "../../styles/vocabulary.css";
import "../../styles/vocabulary-practice.css";

const ROOT = "/api/tu-vung";
const LOAD_ERROR = "Không thể tải dữ liệu luyện tập.";

export default function VocabularyPracticePage() {
  const { user, token } = useAuth();
  const { requireAuthentication } = useGuestAuthPrompt();
  return <SiteLayout className="vocab-page"><SessionBoundary>
    <div className="site-container practice-page vocabulary-practice-page">
      <div className="page-heading">
        <span><Link to="/practice">Luyện tập</Link> / Luyện từ vựng</span>
        <h1>Luyện từ vựng</h1>
        <p>Kiểm tra khả năng nhớ từ với câu hỏi trắc nghiệm. Kết quả hoàn thành được lưu trong lịch sử luyện tập.</p>
        <Link className="outline-button vocabulary-back" to="/practice"><BackArrow />Quay lại chọn hình thức</Link>
      </div>
      {user && token ? <LearnerPractice key={token} token={token} /> :
        <section className="vocab-panel">
          <h2>Đăng nhập để luyện từ vựng</h2>
          <p>Chọn chủ đề và luyện những từ bạn muốn ôn sau khi đăng nhập.</p>
          <div className="practice-actions">
            <button className="primary-button" type="button" aria-haspopup="dialog"
              onClick={() => requireAuthentication("/practice/vocabulary")}>Bắt đầu luyện</button>
            <Link className="vocab-text-button" to="/vocabulary">Học thử Office Essentials</Link>
          </div>
        </section>}
    </div>
  </SessionBoundary></SiteLayout>;
}

function LearnerPractice({ token }) {
  const navigate = useNavigate();
  const { logout } = useAuth();
  const [topics, setTopics] = useState([]);
  const [topicQuery, setTopicQuery] = useState("");
  const [visibleCount, setVisibleCount] = useState(INITIAL_TOPIC_COUNT);
  const mobileCatalog = useMobileTopicCatalog();
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
  const [exitTarget, setExitTarget] = useState(null);
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
  const { filteredTopics, shownTopics, hasMore } = topicCatalogView(topics, topicQuery, visibleCount, mobileCatalog);
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

  function finishLeaving(destination, signOut = false) {
    activeAttempt.current += 1;
    setSaveState("");
    dispatch({ type: "reset" });
    setTopic(null);
    setPool([]);
    setNotice("");
    setError("");
    setExitTarget(null);
    if (signOut) logout();
    if (destination) navigate(destination);
  }

  function leavePractice(destination = null) {
    if (session && !session.finished) setExitTarget({ destination, signOut: false });
    else finishLeaving(destination);
  }

  useEffect(() => {
    if (!session || session.finished) return;
    const onNavigate = event => {
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
      event.preventDefault(); event.stopPropagation();
      setExitTarget({ destination: url.pathname + url.search + url.hash, signOut: false });
    };
    document.addEventListener("click", onNavigate, true);
    return () => document.removeEventListener("click", onNavigate, true);
  }, [session]);

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
        if (session && !session.finished) { event.preventDefault(); leavePractice("/practice/vocabulary/history"); }
      }}>Lịch sử luyện từ</Link></div>
    {loading && <p className="vocabulary-message" role="status">Đang tải dữ liệu luyện tập...</p>}
    {error && <div className="vocab-error" role="alert"><p>{error}</p>
      {!topic && <button className="outline-button" type="button" onClick={() => {
        setLoading(true); setError(""); setReload(value => value + 1);
      }}>Thử lại</button>}
    </div>}

    {!loading && !topic && !error && <>
      <div className="vocabulary-section-heading"><h2>Chọn chủ đề</h2><p>Chọn chủ đề bạn muốn luyện để bắt đầu ôn lại từ vựng.</p></div>
      {topics.length > INITIAL_TOPIC_COUNT && <div className="vocabulary-topic-tools">
        <label htmlFor="practice-topic-search">Tìm chủ đề</label>
        <input id="practice-topic-search" type="search" value={topicQuery}
          placeholder="Nhập tên chủ đề" onChange={event => {
            setTopicQuery(event.target.value); setVisibleCount(INITIAL_TOPIC_COUNT);
          }} />
        <span>{filteredTopics.length} chủ đề</span>
      </div>}
      <div className="vocab-topic-grid" style={topicGridStyle(filteredTopics.length)}>{shownTopics.map(item =>
        <article className="vocab-topic-card" key={item.maChuDe}>
          <PracticeTopicCover name={item.tenChuDe} />
          <div className="vocabulary-topic-details">
          <span className="vocab-pill available">{item.status}</span>
          <p className="vocabulary-topic-count"><strong>{item.totalWords}</strong> từ</p>
          <p className="vocabulary-topic-remembered"><strong>{item.completedWords}/{item.totalWords}</strong> từ đã nhớ <span>{item.progressPercent}%</span></p>
          <progress className="vocabulary-progress" value={item.completedWords} max={item.totalWords || 1} aria-label={`Số từ đã nhớ trong chủ đề ${item.tenChuDe}`} />
          <button type="button" className="primary-button" onClick={() => {
            setTopic(item); setMode("all"); setError(""); setNotice("");
          }}>Chọn chủ đề</button>
          </div>
        </article>)}</div>
      {hasMore && <button type="button" className="outline-button vocabulary-show-more"
        onClick={() => setVisibleCount(count => count + INITIAL_TOPIC_COUNT)}>
        Xem thêm chủ đề ({filteredTopics.length - visibleCount} còn lại)</button>}
      {!topics.length && !error && <p className="vocabulary-message">Chưa có chủ đề từ vựng đang mở.</p>}
      {topics.length > 0 && filteredTopics.length === 0 && <p className="vocabulary-message">Không tìm thấy chủ đề phù hợp.</p>}
    </>}

    {topic && <>
      <button type="button" className="outline-button vocabulary-back" disabled={starting} onClick={() => leavePractice()}><BackArrow />Quay lại chọn chủ đề</button>
      {notice && <p role="status" className="vocab-note">{notice}</p>}

      {!session && <section className="vocab-panel practice-panel vocabulary-selection">
        <div className="vocabulary-panel-heading"><span className="vocabulary-topic-art"><VocabularyIllustration /></span><div><h2>{topic.tenChuDe}</h2>
        <p>{topic.totalWords} từ trong chủ đề</p>
        </div></div>
        <fieldset className="practice-modes" disabled={starting}>
          <legend>Chọn bộ từ luyện tập</legend>
          <label><input type="radio" name="practice-mode" value="all" checked={mode === "all"}
            onChange={() => { setMode("all"); setNotice(""); }} /><span><strong>Tất cả từ</strong><small>Luyện toàn bộ từ vựng trong chủ đề.</small></span></label>
          <label><input type="radio" name="practice-mode" value="review" checked={mode === "review"}
            onChange={() => { setMode("review"); setNotice(""); }} /><span><strong>Từ cần ôn</strong><small>Tập trung vào những từ chưa học hoặc chưa nhớ.</small></span></label>
        </fieldset>
        <p className="vocabulary-guidance">Từ cần ôn gồm từ chưa học và từ chưa nhớ. Luyện tập không thay đổi trạng thái đã nhớ của bạn.</p>
        <div className="vocabulary-start-action"><button type="button" className="primary-button" disabled={starting} onClick={startPractice}>Bắt đầu luyện<svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M5 12h14m-5-5 5 5-5 5" /></svg></button></div>
        {starting && <p role="status">Đang tải dữ liệu luyện tập...</p>}
      </section>}

      {session && !session.finished && <section className="vocab-panel practice-panel vocabulary-quiz">
        <div className="practice-toolbar">
          <div><strong>{topic.tenChuDe}</strong><span>Câu {session.position + 1} / {session.questions.length}</span></div>
          <button type="button" className="outline-button vocabulary-back" onClick={() => leavePractice()}><BackArrow />Thoát lượt luyện</button>
        </div>
        <progress className="vocabulary-progress" value={session.answers.length} max={session.questions.length} aria-label="Số câu đã trả lời trong lượt luyện" />
        <div className="vocabulary-question-heading"><p>{question.type === "word-to-meaning" ? "Chọn nghĩa tiếng Việt đúng:" : "Chọn từ tiếng Anh đúng:"}</p>
        <h2 className="practice-prompt">{question.prompt}</h2>
        </div>
        {question.options.length < 4 && <p>Chủ đề có ít lựa chọn phù hợp; câu này có {question.options.length} đáp án.</p>}
        <div className="practice-options" aria-label="Các đáp án">
          {question.options.map((option, index) => <button type="button" key={index}
            aria-pressed={answer?.optionIndex === index}
            className={"practice-option" + (answer && index === question.correctIndex ? " is-correct" :
              answer && index === answer.optionIndex ? " is-wrong" : "")}
            disabled={!!answer} onClick={() => dispatch({ type: "answer", optionIndex: index })}>
            <strong>{String.fromCharCode(65 + index)}.</strong><span>{option}</span>
          </button>)}
        </div>
        {answer && <div className="practice-feedback" role="status" data-correct={answer.correct}>
          <strong>{answer.correct ? "Đúng" : "Sai"}</strong>
          <p>Đáp án đúng: {question.options[question.correctIndex]}</p>
          <button type="button" className="primary-button" onClick={advance}>
            {session.position + 1 === session.questions.length ? "Xem kết quả" : "Câu tiếp theo"}
          </button>
        </div>}
      </section>}

      {result && <section className="vocab-panel practice-panel vocabulary-result">
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
          <button type="button" className="outline-button vocabulary-back" onClick={() => leavePractice()}><BackArrow />Quay lại chọn chủ đề</button>
        </div>
      </section>}
    </>}
    {exitTarget && <div className="exam-confirm-backdrop" role="presentation"><div className="exam-confirm" role="dialog" aria-modal="true" aria-labelledby="practice-exit-title">
      <h2 id="practice-exit-title">Bạn có chắc muốn rời khỏi bài?</h2>
      <p>Tiến độ chưa nộp có thể bị mất.</p>
      <div className="practice-actions">
        <button type="button" className="outline-button" onClick={() => setExitTarget(null)}>Ở lại làm bài</button>
        <button type="button" className="primary-button" onClick={() => finishLeaving(exitTarget.destination, exitTarget.signOut)}>Thoát bài</button>
      </div>
    </div></div>}
  </>;
}

function PracticeTopicCover({ name }) {
  const [imageFailed, setImageFailed] = useState(false);
  const cover = [
    { pattern: /office|work|công việc/i, image: "office", theme: "office" },
    { pattern: /travel|du lịch/i, image: "travel", theme: "travel" },
    { pattern: /shopping|mua sắm/i, image: "shopping", theme: "shopping" },
    { pattern: /business|kinh doanh/i, image: "business", theme: "business" },
  ].find(item => item.pattern.test(name || ""));
  return <div className="vocabulary-topic-cover" data-theme={cover?.theme}>
    <img src={cover && !imageFailed ? `/images/vocabulary/${cover.image}.jpg` : "/images/home/vocabulary-card.webp"}
      alt="" width="600" height="240" loading="lazy" onError={() => setImageFailed(true)} />
    <h2>{name}</h2>
  </div>;
}

function BackArrow() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M19 12H5m5-5-5 5 5 5" /></svg>;
}

function VocabularyIllustration() {
  return <svg aria-hidden="true" viewBox="0 0 80 80" fill="none">
    <rect x="19" y="10" width="43" height="52" rx="8" fill="currentColor" opacity=".12" transform="rotate(10 40 36)" />
    <rect x="13" y="17" width="43" height="52" rx="8" fill="white" stroke="currentColor" strokeWidth="2" />
    <path d="M24 30h21M24 39h15M24 48h21M24 57h10" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />
    <circle cx="59" cy="59" r="12" fill="currentColor" />
    <path d="m54 59 3 3 6-7" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
  </svg>;
}
