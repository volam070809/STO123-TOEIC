import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { apiRequest, API_BASE_URL } from "../../services/api";
import { INITIAL_TOPIC_COUNT, topicCatalogView, topicGridStyle, useMobileTopicCatalog } from "../practice/topicCatalog";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { publicVocabularyApi } from "../../services/publicVocabularyApi";
import { useContentRevision } from "../../services/contentRevisions";
import { guestPreviewHasMore, vocabularyReturnTo, vocabularyTopicFromSearch } from "./guestVocabularyPreview";
import "../../styles/vocabulary.css";
import "../../styles/vocabulary-studio.css";

const ROOT = "/api/tu-vung";
const COMPLETED = "DA_THUOC";
const LEARNING = "DANG_HOC";

export default function VocabularyPage() {
  const { user, token } = useAuth();
  const contentRevision = useContentRevision("VOCABULARY");
  const { requireAuthentication } = useGuestAuthPrompt();
  const { search } = useLocation();
  const navigate = useNavigate();
  const isLearner = user?.vaiTro === "HOC_VIEN";
  const isAdmin = user?.vaiTro === "ADMIN_QUAN_LY" || user?.vaiTro === "ADMIN_NOI_DUNG";
  const canReadAll = isLearner || isAdmin;
  const requestedTopicId = vocabularyTopicFromSearch(search);
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
  const selectionRef = useRef(null);
  selectionRef.current = { topic, words, order, position, stage };

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
    const load = async () => {
      const data = canReadAll ? await apiRequest(ROOT + "/topics", { token }) : await publicVocabularyApi.topics();
      if (!active) return;
      setTopics(data);
      const requested = requestedTopicId && data.find(item => item.maChuDe === requestedTopicId);
      if (!requested) return;
      const detail = canReadAll ? await apiRequest(ROOT + "/topics/" + requested.maChuDe, { token }) :
        await publicVocabularyApi.preview(requested.maChuDe);
      if (!active) return;
      setTopic(detail.topic);
      setWords(detail.words);
      setStage("list");
    };
    load()
      .catch(() => { if (active) setError("Không thể tải dữ liệu từ vựng."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [user, token, canReadAll, requestedTopicId]);

  useEffect(() => {
    if (!contentRevision || token && !user) return undefined;
    let live = true;
    const refresh = async () => {
      const list = canReadAll ? await apiRequest(ROOT + "/topics", { token, cache: "no-store" }) :
        await publicVocabularyApi.topics();
      if (!live) return;
      setTopics(list);
      const selected = selectionRef.current;
      const topicId = selected.topic?.maChuDe;
      if (!topicId) return;
      if (!list.some(item => item.maChuDe === topicId)) {
        setTopic(null); setWords([]); setStage("topics");
        setError("Chủ đề này không còn mở. Danh sách đã được cập nhật.");
        return;
      }
      const detail = canReadAll ? await apiRequest(ROOT + "/topics/" + topicId,
        { token, cache: "no-store" }) : await publicVocabularyApi.preview(topicId);
      if (!live || selectionRef.current.topic?.maChuDe !== topicId) return;
      const current = selectionRef.current;
      setTopic(detail.topic);
      setWords(detail.words);
      if (current.stage === "cards") {
        const orderedIds = current.order.map(index => current.words[index]?.maTuVung).filter(Boolean);
        const nextOrder = orderedIds.map(id => detail.words.findIndex(word => word.maTuVung === id))
          .filter(index => index >= 0);
        detail.words.forEach((_, index) => { if (!nextOrder.includes(index)) nextOrder.push(index); });
        if (nextOrder.length === 0) setStage("list");
        else {
          const currentId = current.words[current.order[current.position]]?.maTuVung;
          const currentIndex = detail.words.findIndex(word => word.maTuVung === currentId);
          setOrder(nextOrder);
          setPosition(currentIndex >= 0 ? nextOrder.indexOf(currentIndex) : Math.min(current.position, nextOrder.length - 1));
        }
      }
    };
    refresh().catch(() => { if (live) setError("Không thể tải nội dung từ vựng mới. Hãy thử lại."); });
    return () => { live = false; };
  }, [contentRevision, token, user, canReadAll]);

  async function openTopic(item) {
    setError("");
    setLoading(true);
    try {
      const data = canReadAll ? await apiRequest(ROOT + "/topics/" + item.maChuDe, { token }) :
        await publicVocabularyApi.preview(item.maChuDe);
      setTopic(data.topic);
      setWords(data.words);
      setStage("list");
      navigate(vocabularyReturnTo(item.maChuDe));
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
    if (!word || (canReadAll && !token) || (!canReadAll && !topic?.maChuDe)) return;
    setError("");
    try {
      const endpoint = canReadAll ? ROOT + "/" + word.maTuVung +
        (example ? "/audio-vi-du" : "/audio") :
        publicVocabularyApi.audioEndpoint(topic.maChuDe, word.maTuVung, example);
      const response = await fetch(API_BASE_URL + endpoint, canReadAll ? {
        headers: { Authorization: "Bearer " + token }
      } : {});
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
      audioRef.current = null;
    }
  }, [word?.maTuVung, stage]);

  const doneInSession = Object.values(answers).filter(Boolean).length;
  const missedInSession = Object.values(answers).filter(value => !value).length;
  const completedCount = words.filter(item => item.trangThai === COMPLETED).length;
  const completionPercent = words.length ? Math.round(completedCount / words.length * 100) : 0;
  const wordLength = [...(word?.word || "")].length;
  const wordSize = wordLength > 20 ? "very-long" : wordLength > 13 ? "long" : "normal";
  const title = topic?.tenChuDe || "Chủ đề từ vựng";
  const catalogTopics = topics;
  const { filteredTopics, shownTopics, hasMore } = topicCatalogView(catalogTopics, topicQuery, visibleCount, mobileCatalog);
  const previewHasMore = !isLearner && guestPreviewHasMore(topic, words);
  const authReturnTo = vocabularyReturnTo(topic?.maChuDe);

  return <SiteLayout className="vocab-page" aiContext={stage === "cards" && word ? { type: "VOCABULARY", wordId: word.maTuVung } : null}><SessionBoundary><div className={"site-container" + (stage === "topics" ? " vocabulary-topics-page" : "")}>
    {(stage === "topics" || stage === "result") && <div className="page-heading">
      <span>Web / {isLearner ? "Học viên" : "Khách vãng lai"} / Từ vựng</span>
      <h1>{stage === "topics" ? "Từ vựng theo chủ đề" :
        stage === "list" ? title : stage === "cards" ? "Học từ vựng" : "Kết quả học"}</h1>
      <p>{isLearner
        ? "Học theo chủ đề, nghe phát âm và lưu tiến độ của bạn."
        : "Khám phá các chủ đề và học thử tối đa 10 từ trong mỗi chủ đề."}</p>
    </div>}

    {loading && <p role="status">Đang tải từ vựng...</p>}
    {error && <p role="alert" className="vocab-error">{error}</p>}

    {!loading && stage === "topics" && <>
      {!isLearner && <div className="vocab-banner is-topics">
        <strong>Học thử 10 từ trong mỗi chủ đề</strong>
        <span>Bạn có thể đổi chủ đề tự do; đăng nhập để học toàn bộ từ và lưu tiến độ.</span>
      </div>}
      <div className="vocabulary-catalog-header">
        <div className="vocabulary-topics-heading"><h2>Chọn chủ đề để học</h2><p>Học bằng thẻ ghi nhớ và theo dõi số từ đã nhớ trong từng chủ đề.</p></div>
        <div className="vocabulary-topic-tools">
          <label htmlFor="learning-topic-search">Tìm chủ đề</label>
          <div className="vocabulary-topic-search">
            <input id="learning-topic-search" type="search" value={topicQuery}
              placeholder="Nhập tên chủ đề" onChange={event => {
                setTopicQuery(event.target.value); setVisibleCount(INITIAL_TOPIC_COUNT);
              }} />
            <span aria-live="polite">{filteredTopics.length} chủ đề</span>
          </div>
        </div>
      </div>
      <div className="vocab-topic-grid" style={topicGridStyle(filteredTopics.length)}>
        {shownTopics.map(item => {
          const remembered = Math.min(Math.max(item.completedWords || 0, 0), item.totalWords || 0);
          const percent = item.totalWords > 0 ? Math.round(remembered / item.totalWords * 100) : 0;
          return <article className="vocab-topic-card" key={item.maChuDe || "demo"}>
            {item.duongDanAnhBia ? <div className="vocabulary-topic-cover"><img src={`${API_BASE_URL}/api/admin/vocabulary/topics/${item.maChuDe}/cover`} alt="" width="600" height="240" /><h2>{item.tenChuDe}</h2></div> : <VocabularyTopicCover name={item.tenChuDe} />}
            <div className="vocabulary-topic-details">
            <div className="vocabulary-topic-meta">
              <span className="vocab-pill available">{isLearner ? item.status : "HỌC THỬ"}</span>
              <p className="vocabulary-topic-count"><strong>{item.totalWords}</strong> từ</p>
            </div>
            {isLearner && <>
              <div className="vocabulary-topic-progress-copy">
                <span>Đã nhớ <strong>{remembered}/{item.totalWords}</strong> từ</span>
                <strong>{percent}%</strong>
              </div>
              <progress className="vocabulary-topic-progress" value={remembered} max={item.totalWords || 1}
                aria-label={`Tiến độ ${item.tenChuDe}: ${percent}%`} />
            </>}
            <button type="button" className="primary-button" onClick={() => openTopic(item)}>
              {isLearner ? remembered > 0 && remembered < item.totalWords
                ? "Tiếp tục học" : remembered === item.totalWords && item.totalWords > 0
                  ? "Học lại" : "Bắt đầu học" : "Xem danh sách"}
            </button>
            </div>
          </article>})}
      </div>
      {hasMore && <button type="button" className="outline-button vocabulary-show-more"
        onClick={() => setVisibleCount(count => count + INITIAL_TOPIC_COUNT)}>
        Xem thêm chủ đề ({filteredTopics.length - visibleCount} còn lại)</button>}
      {!topics.length && !error && <p>Chưa có chủ đề từ vựng đang mở.</p>}
      {topics.length > 0 && filteredTopics.length === 0 && <p>Không tìm thấy chủ đề phù hợp.</p>}
      {!isLearner && <p className="vocab-note">Đăng nhập để học toàn bộ từ và lưu tiến độ của bạn.</p>}
    </>}

    {!loading && stage === "list" && <div className="vocab-studio-intro">
      <button type="button" className="vocab-studio-back" onClick={() => {
        setStage("topics"); navigate("/vocabulary");
      }}>← Tất cả chủ đề</button>
      <section className="vocab-studio-hero">
        {topic?.duongDanAnhBia && <img className="vocab-studio-cover" src={`${API_BASE_URL}/api/admin/vocabulary/topics/${topic.maChuDe}/cover`} alt="" />}
        <div className="vocab-studio-hero-content"><span className="vocab-studio-kicker">IMMERSIVE VOCABULARY STUDIO · {words.length} TỪ</span>
          <h2>{title}</h2>{topic?.moTa && <p>{topic.moTa}</p>}
          <div className="vocab-studio-hero-meta"><span>{isLearner ? `${completedCount}/${words.length} từ đã nhớ` : "Học thử miễn phí"}</span>
            {isLearner && <strong>{completionPercent}% hoàn thành</strong>}</div>
          {isLearner && <progress value={completedCount} max={words.length || 1} aria-label={`Tiến độ chủ đề ${title}`} />}
          <div className="vocab-studio-hero-actions"><button type="button" className="vocab-studio-primary" disabled={!words.length}
            onClick={() => start(isLearner && completedCount > 0 && completedCount < words.length ? "continue" : "all")}>
            {isLearner ? completedCount > 0 && completedCount < words.length ? "Tiếp tục hành trình" :
              completedCount === words.length && words.length > 0 ? "Học lại chủ đề" : "Bắt đầu khám phá" : "Bắt đầu học thử"} <span aria-hidden="true">→</span></button>
            {isLearner && completedCount > 0 && <button type="button" className="vocab-studio-secondary" onClick={() => start("all")}>Học lại từ đầu</button>}
            {isLearner && <Link className="vocab-studio-secondary" to={`/practice/vocabulary?topic=${topic.maChuDe}`}>Luyện tập chủ đề</Link>}</div>
        </div>
      </section>
      <div className="vocab-journey-heading"><div><span>LỘ TRÌNH TỪ VỰNG</span><h3>Chọn một từ để bắt đầu</h3></div><small>Cuộn ngang để xem toàn bộ</small></div>
      <div className="vocab-journey" role="region" aria-label={`Các từ trong chủ đề ${title}`} tabIndex={0}>
        {words.map((item, index) => <button type="button" className="vocab-journey-item" key={item.maTuVung}
          onClick={() => start("all", index)}><span>{String(index + 1).padStart(2, "0")}</span><strong>{item.word}</strong>
          <small>{isLearner ? item.trangThai === COMPLETED ? "Đã nhớ" : item.trangThai === LEARNING ? "Đang học" : "Chưa học" : "Học thử"}</small></button>)}
      </div>
      {!words.length && <p className="vocab-studio-empty">Chủ đề này chưa có từ vựng để học.</p>}
      {!isLearner && <div className="vocab-studio-guest"><p>Đăng nhập để học toàn bộ từ và lưu tiến độ của bạn.</p>
        <Link to="/register" state={{ returnTo: authReturnTo }}>Đăng ký miễn phí →</Link></div>}
    </div>}

    {!loading && stage === "cards" && word && <div className="vocab-studio">
      <div className="vocab-studio-topbar"><button type="button" className="vocab-studio-back" onClick={returnToList}>← {title}</button>
        <span>STO123 VOCABULARY STUDIO</span>{isLearner && <Link to={`/practice/vocabulary?topic=${topic.maChuDe}`}>Luyện tập chủ đề →</Link>}</div>
      <div className="vocab-studio-learning-heading"><div><span>HÀNH TRÌNH TỪ VỰNG</span><h2>{title}</h2></div>
        <div className="vocab-studio-progress"><span>TỪ HIỆN TẠI</span><strong>{String(currentIndex + 1).padStart(2, "0")} <i>/ {String(words.length).padStart(2, "0")}</i></strong>
          <small>Trong lượt: {position + 1}/{order.length}</small></div></div>
      {isLearner && <div className="vocab-studio-completion"><span>Đã nhớ <strong>{completedCount}/{words.length} từ</strong></span>
        <progress value={completedCount} max={words.length || 1} aria-label={`Đã nhớ ${completedCount} trên ${words.length} từ`} />
        <strong>{completionPercent}%</strong></div>}
      <div className="vocab-studio-scene"><span className="vocab-studio-light" aria-hidden="true" />
        <p className="vocab-studio-flip-hint">Chạm hoặc bấm vào thẻ để {revealed ? "xem lại từ" : "khám phá nghĩa"}</p>
        <button type="button" key={word.maTuVung} className={"vocab-flashcard" + (revealed ? " is-revealed" : "")}
          aria-label={revealed ? `${word.word}, nghĩa ${word.meaning}. Bấm để xem mặt trước` : `${word.word}. Bấm để xem nghĩa và ví dụ`}
          aria-pressed={revealed} onClick={() => setRevealed(value => !value)}>
          <span className="vocab-flashcard-inner">
            <span className="vocab-flashcard-face vocab-flashcard-front" aria-hidden={revealed}>
              <span className="vocab-face-content vocab-face-content-front">
                <span className="vocab-card-label">TỪ VỰNG · {String(position + 1).padStart(2, "0")}</span>
                <strong className={`vocab-word-size-${wordSize}`}>{word.word}</strong>
                {word.pronunciation && <span className="vocab-flashcard-pronunciation">{word.pronunciation}</span>}
                {word.partOfSpeech && <span className="vocab-flashcard-part-of-speech">{word.partOfSpeech}</span>}
                <span className="vocab-studio-card-cue">Lật thẻ để khám phá <span aria-hidden="true">↗</span></span>
              </span>
            </span>
            <span className="vocab-flashcard-face vocab-flashcard-reverse" aria-hidden={!revealed}>
              <span className="vocab-face-content vocab-face-content-back">
                <span className="vocab-card-label">NGHĨA VÀ NGỮ CẢNH</span>
                <strong className={`vocab-flashcard-word vocab-word-size-${wordSize}`}>{word.word}</strong>
                {word.pronunciation && <span className="vocab-flashcard-pronunciation">{word.pronunciation}</span>}
                <strong className="vocab-flashcard-meaning">{word.meaning}</strong>
                {(word.example || word.exampleMeaning) && <span className="vocab-example-panel">
                  {word.example && <span className="vocab-example-row"><span className="vocab-example-label">VÍ DỤ</span><span className="vocab-example-text">{word.example}</span></span>}
                  {word.exampleMeaning && <span className="vocab-example-row vocab-example-translation"><span className="vocab-example-label">BẢN DỊCH</span><span className="vocab-example-text">{word.exampleMeaning}</span></span>}
                </span>}
              </span>
            </span>
          </span>
        </button>
      </div>
      <div className="vocab-studio-controls">
        <div className="vocab-studio-audio">{word.hasAudio && <button type="button" onClick={() => playAudio()}><FlashcardIcon kind="audio" />Nghe phát âm</button>}
          {revealed && word.hasExampleAudio && <button type="button" onClick={() => playAudio(true)}><FlashcardIcon kind="audio" />Nghe ví dụ</button>}
          {!word.hasAudio && <span>Chưa có âm thanh cho từ này</span>}</div>
        <div className="vocab-studio-navigation"><button type="button" disabled={position === 0 || busy}
          onClick={() => { setPosition(value => value - 1); setRevealed(false); }}><FlashcardIcon kind="previous" />Từ trước</button>
          <button type="button" disabled={busy || position + 1 === order.length && !previewHasMore}
            onClick={() => { if (position + 1 < order.length) { setPosition(value => value + 1); setRevealed(false); }
              else if (previewHasMore) requireAuthentication(authReturnTo); }}>Từ tiếp theo<FlashcardIcon kind="next" /></button></div>
        {isLearner && <div className="vocab-studio-decisions"><button type="button" disabled={busy} aria-pressed={word.trangThai === LEARNING}
          onClick={() => answer(false)}><FlashcardIcon kind="retry" />Chưa nhớ</button>
          <button type="button" disabled={busy} aria-pressed={word.trangThai === COMPLETED}
            onClick={() => answer(true)}><FlashcardIcon kind="check" />{busy ? "Đang lưu…" : "Đã nhớ"}</button></div>}
      </div>
      <div className="vocab-journey-heading"><div><span>ĐANG HỌC</span><h3>Hành trình của bạn</h3></div><small>Chọn từ để xem lại</small></div>
      <div className="vocab-journey vocab-journey-compact" role="region" aria-label="Chọn từ trong lượt học" tabIndex={0}>
        {order.map((index, journeyPosition) => <button type="button" key={words[index].maTuVung}
          className={journeyPosition === position ? "vocab-journey-item is-active" : "vocab-journey-item"}
          aria-current={journeyPosition === position ? "step" : undefined} disabled={busy}
          onClick={() => { setPosition(journeyPosition); setRevealed(false); }}>
          <span>{String(journeyPosition + 1).padStart(2, "0")}</span><strong>{words[index].word}</strong></button>)}
      </div>
      {!isLearner && <p className="vocab-studio-trial">Học thử không lưu tiến độ. <Link to="/register" state={{ returnTo: authReturnTo }}>Tạo tài khoản để học đầy đủ →</Link></p>}
    </div>}

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
        <h2>Học đầy đủ và lưu tiến độ</h2>
        <p>Đăng ký tài khoản để học toàn bộ từ trong chủ đề và lưu tiến độ học.</p>
        <div className="vocab-unlock-actions">
          <Link className="primary-button" to="/register" state={{ returnTo: authReturnTo }}>Đăng ký miễn phí</Link>
          <Link className="outline-button" to="/login" state={{ returnTo: authReturnTo }}>Đăng nhập</Link>
        </div>
      </aside>}
    </div>}
  </div></SessionBoundary></SiteLayout>;
}

function VocabularyTopicCover({ name }) {
  const [imageFailed, setImageFailed] = useState(false);
  const cover = [
    { pattern: /office|work|công việc|recruitment|human resources|tuyển dụng|nhân sự/i, image: "office", theme: "office" },
    { pattern: /travel|du lịch/i, image: "travel", theme: "travel" },
    { pattern: /shopping|retail|mua sắm|bán lẻ/i, image: "shopping", theme: "shopping" },
    { pattern: /business|kinh doanh|banking|finance|marketing|advertising|ngân hàng|tài chính|tiếp thị|quảng cáo/i, image: "business", theme: "business" },
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
