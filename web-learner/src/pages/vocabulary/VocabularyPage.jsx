import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { apiRequest, API_BASE_URL } from "../../services/api";
import "../../styles/vocabulary.css";

const ROOT = "/api/tu-vung";
const COMPLETED = "DA_THUOC";
const LEARNING = "DANG_HOC";

export default function VocabularyPage() {
  const { user, token } = useAuth();
  const isLearner = !!user;
  const [topics, setTopics] = useState([]);
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

  return <SiteLayout className="vocab-page"><SessionBoundary><div className="site-container">
    <div className="page-heading">
      <span>Web / {isLearner ? "Học viên" : "Khách vãng lai"} / Từ vựng</span>
      <h1>{stage === "topics" ? "Chọn chủ đề từ vựng" :
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
      <div className="vocab-topic-grid">
        {(isLearner ? topics : [topic]).filter(Boolean).map(item =>
          <article className="vocab-topic-card" key={item.maChuDe || "demo"}>
            <span className="vocab-pill available">{isLearner ? item.status : "HỌC THỬ"}</span>
            <h2>{item.tenChuDe}</h2>
            <p>{item.totalWords} từ</p>
            {isLearner && <>
              <p>{item.completedWords}/{item.totalWords} đã hoàn thành · {item.progressPercent}%</p>
              <progress value={item.completedWords} max={item.totalWords || 1} />
            </>}
            <button type="button" className="primary-button" onClick={() => openTopic(item)}>
              {isLearner ? item.completedWords === item.totalWords && item.totalWords > 0
                ? "Học lại" : item.completedWords
                  ? "Tiếp tục học" : "Bắt đầu học" : "Xem danh sách"}
            </button>
          </article>)}
      </div>
      {isLearner && !topics.length && !error && <p>Chưa có chủ đề từ vựng đang mở.</p>}
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
          <ol>{words.map((item, index) => <li key={item.maTuVung}>
            <button type="button" className="vocab-word-row" onClick={() => start("all", index)}
              aria-label={`Mở thẻ ${item.word}`}>
              <strong>{item.word}</strong>
              {isLearner && <span className="vocab-list-status">
                {item.trangThai === COMPLETED ? "Đã nhớ" : item.trangThai === LEARNING ? "Đang học" : "Chưa học"}
              </span>}
            </button>
          </li>)}</ol>
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

    {!loading && stage === "cards" && word && <>
      <button type="button" className="vocab-text-button" onClick={returnToList}>← Quay lại danh sách</button>
      <div className="vocab-study-grid">
      <div className="vocab-study-heading">
        <strong>{title}</strong><span>{position + 1}/{order.length}</span>
        <progress value={position + 1} max={order.length} />
      </div>
      <section className="vocab-panel vocab-flashcard-panel">
        <button type="button" className={"vocab-flashcard" + (revealed ? " is-revealed" : "")}
          onClick={() => setRevealed(value => !value)}>
          <span className="vocab-card-label">{revealed ? "MẶT SAU" : "MẶT TRƯỚC"}</span>
          <strong>{word.word}</strong>
          <span>{word.pronunciation} · {word.partOfSpeech}</span>
          {revealed && <>
            <b>{word.meaning}</b>
            {word.example && <small>Ví dụ: {word.example}</small>}
            {word.exampleMeaning && <small>Dịch: {word.exampleMeaning}</small>}
          </>}
        </button>
        <div className="vocab-card-actions">
          {isLearner && word.hasAudio && <button type="button" className="outline-button"
            onClick={() => playAudio()}>🔊 Nghe từ</button>}
          {isLearner && revealed && word.hasExampleAudio && <button type="button" className="outline-button"
            onClick={() => playAudio(true)}>🔊 Nghe câu ví dụ</button>}
          <button type="button" className="outline-button" onClick={() => setRevealed(value => !value)}>
            {revealed ? "Xem mặt trước" : "Lật thẻ"}</button>
          <button type="button" className="vocab-text-button" disabled={position === 0}
            onClick={() => { setPosition(value => value - 1); setRevealed(false); }}>← Từ trước</button>
          <button type="button" className="vocab-text-button" disabled={position + 1 === order.length}
            onClick={() => { setPosition(value => value + 1); setRevealed(false); }}>Từ tiếp theo →</button>
        </div>
      </section>
      <aside className="vocab-panel vocab-study-aside">
        {isLearner ? <>
          <h2>Bạn đã nhớ từ này?</h2>
          <p>Chọn mức độ nhớ để lưu tiến độ của bạn.</p>
          <div className="vocab-decision-actions">
            <button type="button" className="outline-button" disabled={busy}
              onClick={() => answer(false)}>Chưa nhớ</button>
            <button type="button" className="primary-button" disabled={busy}
              onClick={() => answer(true)}>Đã nhớ</button>
          </div>
        </> : <>
          <h2>Học thử từ vựng</h2>
          <p>Lật thẻ để xem nghĩa và ví dụ. Dùng Từ trước hoặc Từ tiếp theo để xem tất cả từ.</p>
          <span className="vocab-aside-note">Học thử không lưu tiến độ.</span>
        </>}
      </aside>
    </div></>}

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
