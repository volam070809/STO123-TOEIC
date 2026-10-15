import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { apiRequest, API_BASE_URL } from "../../services/api";
import { INITIAL_TOPIC_COUNT, topicCatalogView, topicGridStyle, useMobileTopicCatalog } from "../practice/topicCatalog";
import "../../styles/vocabulary.css";

const ROOT = "/api/tu-vung";
const COMPLETED = "DA_THUOC";
const LEARNING = "DANG_HOC";

export default function VocabularyPage() {
  const { user, token } = useAuth();
  const isLearner = !!user;
  const [topics, setTopics] = useState([]);
  const [topicQuery, setTopicQuery] = useState("");
  const [visibleCount, setVisibleCount] = useState(INITIAL_TOPIC_COUNT);
  const mobileCatalog = useMobileTopicCatalog();
  const [topic, setTopic] = useState(null);
  const [words, setWords] = useState([]);
  const [stage, setStage] = useState("topics");
  const [order, setOrder] = useState([]);
  const [position, setPosition] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [answers, setAnswers] = useState({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const audioRef = useRef(null);

  useEffect(() => {
    if (token && !user) return;
    let active = true;
    setLoading(true);
    setError("");
    setStage("topics");
    setTopicQuery("");
    setVisibleCount(INITIAL_TOPIC_COUNT);
    setTopic(null);
    setWords([]);
    const endpoint = isLearner ? ROOT + "/topics" : ROOT + "/demo";
    apiRequest(endpoint, { token: isLearner ? token : null })
      .then(data => {
        if (!active) return;
        if (isLearner) setTopics(data);
        else {
          setTopic({ tenChuDe: data.tenChuDe, totalWords: data.words.length });
          setWords(data.words);
        }
      })
      .catch(() => { if (active) setError("Không thể tải dữ liệu từ vựng."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [user, token, isLearner]);

  async function openTopic(item) {
    setError("");
    if (!isLearner) { setStage("list"); return; }
    setLoading(true);
    try {
      const data = await apiRequest(ROOT + "/topics/" + item.maChuDe, { token });
      setTopic(data.topic);
      setWords(data.words);
      setStage("list");
    } catch {
      setError("Không thể tải dữ liệu từ vựng.");
    } finally {
      setLoading(false);
    }
  }

  function start(mode = "all", fromIndex = 0) {
    const indices = words.map((_, i) => i);
    const chosen = isLearner && mode === "continue"
      ? indices.filter(i => words[i].trangThai !== COMPLETED) : indices;
    if (!chosen.length) return;
    setOrder(chosen);
    setPosition(fromIndex);
    setRevealed(false);
    setAnswers({});
    setError("");
    setStage("cards");
  }

  const currentIndex = order[position];
  const word = words[currentIndex];

  async function answer(remembered) {
    if (!isLearner || !word || busy) return;
    setBusy(true);
    setError("");
    try {
      const result = await apiRequest(ROOT + "/" + word.maTuVung + "/progress", {
        method: "PUT", token, body: { trangThai: remembered ? COMPLETED : LEARNING }
      });
      setWords(previous => previous.map(item =>
        item.maTuVung === word.maTuVung ? { ...item, trangThai: result.trangThai } : item));
      setAnswers(previous => ({ ...previous, [word.maTuVung]: remembered }));
      setRevealed(false);
      if (position + 1 === order.length) {
        setStage("result");
        refreshTopic();
      } else {
        setPosition(position + 1);
      }
    } catch {
      setError("Không thể lưu tiến độ. Vui lòng thử lại.");
    } finally {
      setBusy(false);
    }
  }

  async function refreshTopic() {
    if (!isLearner || !topic?.maChuDe) return;
    try {
      const data = await apiRequest(ROOT + "/topics/" + topic.maChuDe, { token });
      setTopic(data.topic);
      setWords(data.words);
      setTopics(previous => previous.map(item =>
        item.maChuDe === data.topic.maChuDe ? data.topic : item));
    } catch {
      setError("Không thể tải tiến độ mới nhất.");
    }
  }

  function returnToList() {
    setStage("list");
    setRevealed(false);
    if (isLearner) refreshTopic();
  }

  async function playAudio(example = false) {
    if (!isLearner || !word || !token) return;
    setError("");
    try {
      const response = await fetch(API_BASE_URL + ROOT + "/" + word.maTuVung +
        (example ? "/audio-vi-du" : "/audio"), {
        headers: { Authorization: "Bearer " + token }
      });
      if (!response.ok) throw new Error();
      const url = URL.createObjectURL(await response.blob());
      if (audioRef.current) {
        audioRef.current.pause();
        URL.revokeObjectURL(audioRef.current.src);
      }
      const audio = new Audio(url);
      audioRef.current = audio;
      audio.onended = () => { URL.revokeObjectURL(url); if (audioRef.current === audio) audioRef.current = null; };
      await audio.play();
    } catch {
      setError("Không thể phát âm thanh.");
    }
  }

  useEffect(() => () => {
    if (audioRef.current) {
      audioRef.current.pause();
      URL.revokeObjectURL(audioRef.current.src);
    }
  }, []);

  const doneInSession = Object.values(answers).filter(Boolean).length;
  const missedInSession = Object.values(answers).filter(value => !value).length;
  const title = topic?.tenChuDe || "Office Essentials";
  const catalogTopics = isLearner ? topics : [topic].filter(Boolean);
  const { filteredTopics, shownTopics, hasMore } = topicCatalogView(catalogTopics, topicQuery, visibleCount, mobileCatalog);

  return <SiteLayout className="vocab-page"><SessionBoundary><div className={"site-container" + (stage === "topics" ? " vocabulary-topics-page" : "")}>
    <div className="page-heading">
      <span>Web / {isLearner ? "Học viên" : "Khách vãng lai"} / Từ vựng</span>
      <h1>{stage === "topics" ? "Từ vựng theo chủ đề" :
        stage === "list" ? title : stage === "cards" ? "Học từ vựng" : "Kết quả học"}</h1>
      <p>{isLearner
        ? "Học theo chủ đề, nghe phát âm và lưu tiến độ của bạn."
        : "Học thử toàn bộ 10 từ Office Essentials. Kết quả chỉ có trong phiên này."}</p>
    </div>

    {loading && <p role="status">Đang tải từ vựng...</p>}
    {error && <p role="alert" className="vocab-error">{error}</p>}

    {!loading && stage === "topics" && <>
      {!isLearner && <div className="vocab-banner is-topics">
        <strong>Office Essentials: 10 từ học thử</strong>
        <span>Đăng ký tài khoản để học thêm nhiều chủ đề, nghe phát âm và lưu tiến độ học.</span>
      </div>}
      <div className="vocabulary-topics-heading"><h2>Chọn chủ đề để học</h2><p>Học bằng thẻ ghi nhớ và theo dõi số từ đã nhớ trong từng chủ đề.</p></div>
      {isLearner && topics.length > INITIAL_TOPIC_COUNT && <div className="vocabulary-topic-tools">
        <label htmlFor="learning-topic-search">Tìm chủ đề</label>
        <input id="learning-topic-search" type="search" value={topicQuery}
          placeholder="Nhập tên chủ đề" onChange={event => {
            setTopicQuery(event.target.value); setVisibleCount(INITIAL_TOPIC_COUNT);
          }} />
        <span>{filteredTopics.length} chủ đề</span>
      </div>}
      <div className="vocab-topic-grid" style={topicGridStyle(filteredTopics.length)}>
        {shownTopics.map(item => {
          const remembered = Math.min(Math.max(item.completedWords || 0, 0), item.totalWords || 0);
          const percent = item.totalWords > 0 ? Math.round(remembered / item.totalWords * 100) : 0;
          return <article className="vocab-topic-card" key={item.maChuDe || "demo"}>
            <VocabularyTopicCover name={item.tenChuDe} />
            <div className="vocabulary-topic-details">
            <span className="vocab-pill available">{isLearner ? item.status : "HỌC THỬ"}</span>
            <p className="vocabulary-topic-count">{item.totalWords} từ</p>
            {isLearner && <>
              <p className="vocabulary-topic-remembered">{remembered}/{item.totalWords} từ đã nhớ · {percent}%</p>
              <progress value={remembered} max={item.totalWords || 1} aria-label={`Tiến độ ${item.tenChuDe}: ${percent}%`} />
            </>}
            <button type="button" className="primary-button" onClick={() => openTopic(item)}>
              {isLearner ? remembered > 0 && remembered < item.totalWords
                ? "Tiếp tục học" : remembered === item.totalWords && item.totalWords > 0
                  ? "Học lại" : "Bắt đầu học" : "Xem danh sách"}
            </button>
            </div>
          </article>})}
      </div>
      {isLearner && hasMore && <button type="button" className="outline-button vocabulary-show-more"
        onClick={() => setVisibleCount(count => count + INITIAL_TOPIC_COUNT)}>
        Xem thêm chủ đề ({filteredTopics.length - visibleCount} còn lại)</button>}
      {isLearner && !topics.length && !error && <p>Chưa có chủ đề từ vựng đang mở.</p>}
      {isLearner && topics.length > 0 && filteredTopics.length === 0 && <p>Không tìm thấy chủ đề phù hợp.</p>}
      {!isLearner && <p className="vocab-note">Mở khóa thêm chủ đề bằng cách <Link to="/login">đăng nhập</Link> hoặc <Link to="/register">đăng ký</Link>.</p>}
    </>}

    {!loading && stage === "list" && <>
      <div className="vocab-list-back">
        <button type="button" className="vocab-text-button" onClick={() => setStage("topics")}>← Quay lại chủ đề</button>
      </div>
      <div className="vocab-banner">
        <strong>{title}</strong>
        <span>{words.length} từ {isLearner
          ? "· " + topic.completedWords + "/" + words.length + " đã hoàn thành · " + topic.progressPercent + "%"
          : "· Kết quả học thử chưa được lưu"}</span>
      </div>
      <div className="vocab-list-grid">
        <section className="vocab-panel vocab-word-list">
          <h2>Danh sách: {title}</h2>
          <div className="vocab-word-scroll" role="region" aria-label={`Danh sách từ ${title}`} tabIndex={0}>
            <ol>{words.map((item, index) => <li key={item.maTuVung}>
              <button type="button" className="vocab-word-row" onClick={() => start("all", index)}
                aria-label={`Mở thẻ ${item.word}`}>
                <strong>{item.word}</strong>
                {isLearner && <span className="vocab-list-status">
                  {item.trangThai === COMPLETED ? "Đã nhớ" : item.trangThai === LEARNING ? "Đang học" : "Chưa học"}
                </span>}
              </button>
            </li>)}</ol>
          </div>
          <div className="vocab-list-actions">
            <button type="button" className="primary-button" disabled={!words.length}
              onClick={() => start(isLearner && topic.completedWords > 0 && topic.completedWords < words.length
                ? "continue" : "all")}>{isLearner
                  ? topic.completedWords === words.length && words.length > 0 ? "Học lại"
                    : topic.completedWords > 0 ? "Tiếp tục học" : "Bắt đầu học"
                  : "Bắt đầu học thử"}</button>
            {isLearner && topic.completedWords > 0 && <button type="button" className="outline-button"
              onClick={() => start("all")}>Học lại toàn bộ</button>}
          </div>
        </section>
        {!isLearner && <aside className="vocab-panel vocab-unlock">
          <h2>Mở khóa thêm chủ đề</h2>
          <p>Đăng ký tài khoản để học thêm nhiều chủ đề, nghe phát âm và lưu tiến độ học.</p>
          <Link className="primary-button" to="/register">Đăng ký miễn phí</Link>
        </aside>}
      </div>
    </>}

    {!loading && stage === "cards" && word && <div className="vocabulary-flashcard-view">
      <button type="button" className="outline-button vocab-flashcard-back" onClick={returnToList}><FlashcardIcon kind="previous" />Quay lại danh sách</button>
      <div className="vocab-study-grid">
        <div className="vocab-study-heading">
          <strong>{title}</strong>
          <div className="vocab-study-progress"><span>Từ {position + 1}/{order.length}</span>
            <progress value={position + 1} max={order.length} aria-label="Tiến độ lượt học từ vựng" /></div>
        </div>
        <section className="vocab-panel vocab-flashcard-panel">
          <p className="vocab-flip-hint">Bấm vào thẻ để lật xem nghĩa / ví dụ</p>
          <button type="button" className={"vocab-flashcard" + (revealed ? " is-revealed" : "")}
            aria-label={`${word.word}: ${revealed ? "xem mặt trước" : "xem nghĩa và ví dụ"}`}
            onClick={() => setRevealed(value => !value)}>
            {!revealed ? <span className="vocab-flashcard-face vocab-flashcard-front">
              <span className="vocab-card-label">MẶT TRƯỚC</span>
              <strong>{word.word}</strong>
              {word.pronunciation && <span className="vocab-flashcard-pronunciation">{word.pronunciation}</span>}
              {word.partOfSpeech && <span className="vocab-flashcard-part-of-speech">{word.partOfSpeech}</span>}
            </span> : <span className="vocab-flashcard-face vocab-flashcard-reverse">
              <span className="vocab-card-label">MẶT SAU · {word.word}</span>
              <strong className="vocab-flashcard-meaning">{word.meaning}</strong>
              {word.example && <span className="vocab-flashcard-example"><span>Ví dụ</span>{word.example}</span>}
              {word.exampleMeaning && <span className="vocab-flashcard-translation"><span>Bản dịch</span>{word.exampleMeaning}</span>}
            </span>}
          </button>
          <div className="vocab-card-actions">
            {isLearner && <div className="vocab-flashcard-tools">
              {word.hasAudio && <button type="button" className="outline-button" onClick={() => playAudio()}><FlashcardIcon kind="audio" />Nghe từ</button>}
              {revealed && word.hasExampleAudio && <button type="button" className="outline-button" onClick={() => playAudio(true)}><FlashcardIcon kind="audio" />Nghe câu ví dụ</button>}
            </div>}
            <div className="vocab-flashcard-navigation">
              <button type="button" className="outline-button" disabled={position === 0}
                onClick={() => { setPosition(value => value - 1); setRevealed(false); }}><FlashcardIcon kind="previous" />Từ trước</button>
              <button type="button" className="outline-button" disabled={position + 1 === order.length}
                onClick={() => { setPosition(value => value + 1); setRevealed(false); }}>Từ tiếp theo<FlashcardIcon kind="next" /></button>
            </div>
            {isLearner && <div className="vocab-decision-actions">
              <button type="button" className="outline-button vocab-memory-forgotten" disabled={busy} aria-pressed={word.trangThai === LEARNING}
                onClick={() => answer(false)}><FlashcardIcon kind="retry" />Chưa nhớ</button>
              <button type="button" className="primary-button vocab-memory-remembered" disabled={busy} aria-pressed={word.trangThai === COMPLETED}
                onClick={() => answer(true)}><FlashcardIcon kind="check" />Đã nhớ</button>
            </div>}
          </div>
          {!isLearner && <p className="vocab-aside-note">Học thử không lưu tiến độ.</p>}
        </section>
      </div></div>}

    {!loading && stage === "result" && <div className="vocab-result-grid">
      <section className="vocab-panel vocab-result-panel">
        <div className="vocab-score"><strong>{doneInSession}/{order.length}</strong><span>từ đã nhớ trong lượt này</span></div>
        <p>{missedInSession} từ cần ôn. {isLearner
          ? "Tiến độ từng từ đã được lưu." : "Kết quả học thử chưa được lưu."}</p>
        <button type="button" className="primary-button" onClick={() => start("all")}>Học lại toàn bộ</button>
        <button type="button" className="vocab-text-button"
          onClick={returnToList}>Về danh sách</button>
      </section>
      {!isLearner && <aside className="vocab-panel vocab-unlock">
        <h2>Mở khóa thêm chủ đề</h2>
        <p>Đăng ký tài khoản để học thêm nhiều chủ đề, nghe phát âm và lưu tiến độ học.</p>
        <div className="vocab-unlock-actions">
          <Link className="primary-button" to="/register">Đăng ký miễn phí</Link>
          <Link className="outline-button" to="/login">Đăng nhập</Link>
        </div>
      </aside>}
    </div>}
  </div></SessionBoundary></SiteLayout>;
}

function VocabularyTopicCover({ name }) {
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

function FlashcardIcon({ kind }) {
  return <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    {kind === "audio" && <><path d="m11 5-5 4H3v6h3l5 4V5Z" /><path d="M15 8a6 6 0 0 1 0 8m3-11a10 10 0 0 1 0 14" /></>}
    {kind === "previous" && <path d="M19 12H5m5-5-5 5 5 5" />}
    {kind === "next" && <path d="M5 12h14m-5-5 5 5-5 5" />}
    {kind === "retry" && <><path d="M4 9a8 8 0 1 1 0 6" /><path d="M4 4v5h5" /></>}
    {kind === "check" && <path d="m5 12 4 4L19 6" />}
  </svg>;
}
