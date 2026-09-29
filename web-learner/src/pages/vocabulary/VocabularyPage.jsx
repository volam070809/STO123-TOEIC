import { useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import SessionBoundary from "../../components/auth/SessionBoundary";
import { demoVocabulary } from "../../data/demoVocabulary";
import "../../styles/vocabulary.css";

const allWords = demoVocabulary.map((_, index) => index);
const lockedTopics = [
  { title: "Travel basics", count: 20 },
  { title: "Business meetings", count: 20 },
  { title: "Daily routines", count: 15 },
];

export default function VocabularyPage() {
  const { user } = useAuth();
  const isLearner = !!user;
  const [stage, setStage] = useState("topics");
  const [wordIndices, setWordIndices] = useState(allWords);
  const [position, setPosition] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [remembered, setRemembered] = useState([]);
  const [forgotten, setForgotten] = useState([]);
  const currentIndex = wordIndices[position];
  const word = demoVocabulary[currentIndex];

  function start(words = allWords) {
    setWordIndices(words);
    setPosition(0);
    setRevealed(false);
    setRemembered([]);
    setForgotten([]);
    setStage("cards");
  }

  function answer(isRemembered) {
    if (isRemembered) setRemembered(previous => [...previous, currentIndex]);
    else setForgotten(previous => [...previous, currentIndex]);
    setRevealed(false);
    if (position + 1 === wordIndices.length) setStage("result");
    else setPosition(position + 1);
  }

  const isTopics = stage === "topics";
  const isList = stage === "list";
  const isCards = stage === "cards";
  const isResult = stage === "result";

  return <SiteLayout className="vocab-page"><SessionBoundary><div className="site-container">
    <div className="page-heading">
      <span>Web / {isLearner ? "Học viên" : "Khách vãng lai"} / {isTopics ? "Từ vựng" : isList ? "Học thử" : isCards ? "Office Essentials" : "Kết quả"}</span>
      <h1>{isTopics ? "Chọn chủ đề học thử" : isList ? "Học thử từ vựng miễn phí" : isCards ? "Học thử từ vựng" : "Kết quả học thử"}</h1>
      <p>{isTopics ? (isLearner ? "Khám phá danh sách từ vựng mẫu Office Essentials." : "Khách vãng lai được mở một danh sách. Các chủ đề còn lại cần đăng nhập.")
        : isList ? (isLearner ? "Danh sách mẫu gồm 10 từ; kết quả không được lưu." : "Bạn có thể học thử một danh sách gồm 10 từ trước khi đăng nhập.")
          : isCards ? (revealed ? "Mặt sau hiển thị nghĩa, phát âm và ví dụ của từ." : "Mỗi lượt học gồm 10 từ. Nghĩa và ví dụ chỉ hiện sau khi lật thẻ.")
            : "Bạn đã hoàn thành danh sách Office Essentials."}</p>
    </div>

    {isTopics && <>
      {isLearner ? <div className="vocab-banner"><strong>Nội dung học thử</strong><span>Office Essentials là dữ liệu mẫu trong ứng dụng. Tiến độ không được lưu.</span></div>
        : <div className="vocab-banner is-topics"><strong>Giới hạn tài khoản khách</strong><span>Đã dùng 0/1 danh sách học thử · Chọn Office Essentials để bắt đầu.</span></div>}
      <div className={"vocab-topic-grid" + (isLearner ? " is-learner" : "")}>
        <article className="vocab-topic-card"><span className="vocab-pill available">MỞ ĐƯỢC</span><h2>Office Essentials</h2>
          <p>10 từ · Theo chủ đề</p><p>Từ vựng công sở cơ bản</p>
          <button type="button" className="primary-button" onClick={() => setStage("list")}>Học thử danh sách</button></article>
        {!isLearner && lockedTopics.map(topic => <article className="vocab-topic-card locked" key={topic.title}>
          <span className="vocab-pill locked-label">CẦN ĐĂNG NHẬP</span><h2>{topic.title}</h2>
          <p>{topic.count} từ · Theo chủ đề</p><p>Đăng nhập để mở danh sách</p>
          <Link className="outline-button" to="/login">Đăng nhập để mở</Link></article>)}
      </div>
      <p className="vocab-note">{isLearner ? "Đây là nội dung mẫu; các chủ đề khác chưa có dữ liệu trong phiên bản này." : "Sau khi đăng nhập, bạn có thể học theo nhiều chủ đề, lưu từ cần ôn và xem lịch sử kết quả."}</p>
    </>}

    {isList && <>
      {isLearner ? <div className="vocab-banner"><strong>Danh sách từ vựng mẫu</strong><span>10 từ Office Essentials · Không lưu tiến độ học.</span></div>
        : <div className="vocab-banner"><strong>Tài khoản khách vãng lai</strong><span>Đã sử dụng 0/1 danh sách học thử · Đăng nhập để mở toàn bộ chủ đề.</span></div>}
      <div className="vocab-list-grid"><section className="vocab-panel vocab-word-list">
        <div className="vocab-list-heading"><h2>Danh sách: Office Essentials</h2><p>10 từ vựng cơ bản trong môi trường công sở</p></div>
        <ol>{demoVocabulary.map((item, index) => <li key={item.word}>{index === 0
          ? <button type="button" className="vocab-word-row is-current" onClick={() => start()} aria-label="Bắt đầu học thử với appointment"><strong>{item.word}</strong><span className="vocab-list-status">Đang học</span></button>
          : <span className="vocab-word-row"><span>{item.word}</span><span className="vocab-list-status">Chưa mở</span></span>}</li>)}</ol>
      </section>{isLearner ? <aside className="vocab-panel vocab-unlock"><h2>Về danh sách mẫu</h2>
        <p>Danh sách này dùng dữ liệu minh họa trong ứng dụng. Bạn có thể lật thẻ và xem kết quả của lượt học hiện tại.</p>
        <small>Kết quả không được lưu sau khi rời trang.</small></aside>
      : <aside className="vocab-panel vocab-unlock"><h2>Mở khóa toàn bộ danh sách</h2>
        <p>Đăng ký tài khoản để học theo chủ đề, lưu tiến độ và làm bài kiểm tra.</p>
        <div className="vocab-unlock-actions"><Link className="primary-button" to="/register">Đăng ký miễn phí</Link>
          <Link className="outline-button" to="/login">Đăng nhập</Link></div>
        <small>Bạn vẫn có thể tiếp tục học thử danh sách hiện tại.</small></aside>}</div>
      <button type="button" className="vocab-text-button" onClick={() => setStage("topics")}>← Chọn chủ đề khác</button>
    </>}

    {isCards && <div className="vocab-study-grid"><section className="vocab-panel vocab-flashcard-panel">
      <div className="vocab-study-heading"><strong>Office Essentials</strong><span>Từ {position + 1}/{wordIndices.length}{revealed ? " · Đã lật thẻ" : ""}</span></div>
      <div className="vocab-progress" aria-label={`Từ ${position + 1} trên ${wordIndices.length}`}><span style={{ width: `${(position + 1) / wordIndices.length * 100}%` }} /></div>
      <button type="button" className={"vocab-flashcard" + (revealed ? " is-revealed" : "")} onClick={() => setRevealed(!revealed)} aria-label={revealed ? `Xem mặt trước của ${word.word}` : `Lật thẻ ${word.word}`}>
        {!revealed && <><span className="vocab-card-label">MẶT TRƯỚC</span><strong>{word.word}</strong>
          <span>{word.pronunciation} · {word.partOfSpeech}</span><small>Nhấn vào thẻ để xem nghĩa, ví dụ và phát âm.</small></>}
        {revealed && <><span className="vocab-card-label">MẶT SAU</span><strong>{word.word}</strong>
          <span>{word.pronunciation} · {word.partOfSpeech}</span><b>{word.meaning}</b><small>Ví dụ: {word.example}</small></>}
      </button>
      <div className="vocab-card-actions">{!revealed && <button type="button" className="primary-button" onClick={() => setRevealed(true)}>Lật thẻ</button>}
        {revealed && <button type="button" className="outline-button" onClick={() => setRevealed(false)}>Xem mặt trước</button>}
        <button type="button" className="vocab-text-button" onClick={() => answer(false)}>Từ tiếp theo →</button></div>
    </section><aside className="vocab-panel vocab-study-aside"><h2>{revealed ? "Bạn đã nhớ từ này?" : "Quy tắc học thử"}</h2>
      <p>{revealed ? "Chọn mức độ nhớ để hệ thống gợi ý ôn lại." : "Lật thẻ để xem nghĩa và ví dụ, sau đó chọn mức độ nhớ của bạn."}</p>
      {revealed ? <div className="vocab-decision-actions"><button type="button" className="outline-button" onClick={() => answer(false)}>Chưa nhớ</button>
        <button type="button" className="primary-button" onClick={() => answer(true)}>Đã nhớ</button></div>
        : isLearner ? <span className="vocab-aside-note">Kết quả chỉ hiển thị trong lượt học này.</span>
          : <Link className="outline-button" to="/login">Đăng nhập để lưu</Link>}
      {revealed && <span className="vocab-aside-note">Xem kết quả sau từ cuối cùng.</span>}
    </aside></div>}

    {isResult && <div className="vocab-result-grid"><section className="vocab-panel vocab-result-panel">
      <div className="vocab-score"><strong>{remembered.length}/{wordIndices.length}</strong><span>từ nhớ đúng</span></div>
      <p>{isLearner ? "Bạn có thể học lại " + forgotten.length + " từ chưa nhớ. Kết quả mẫu không được lưu." : "Bạn có thể học lại " + forgotten.length + " từ chưa nhớ hoặc đăng nhập để lưu kết quả."}</p>
      {forgotten.length > 0 && <button type="button" className="primary-button" onClick={() => start(forgotten)}>Học lại từ chưa nhớ</button>}
      <button type="button" className="vocab-text-button" onClick={() => start()}>Học lại toàn bộ</button>
    </section>{isLearner ? <aside className="vocab-panel vocab-unlock"><h2>Kết quả lượt học mẫu</h2>
        <p>Điểm số chỉ phản ánh lượt học hiện tại và không được lưu vào tài khoản.</p></aside>
      : <aside className="vocab-panel vocab-unlock"><h2>Mở khóa trải nghiệm đầy đủ</h2>
      <p>Đăng nhập để lưu tiến độ, học thêm chủ đề và luyện từ theo kết quả.</p>
      <div className="vocab-unlock-actions"><Link className="primary-button" to="/login">Đăng nhập</Link>
        <Link className="outline-button" to="/register">Đăng ký miễn phí</Link></div></aside>}
      <section className="vocab-panel vocab-result-summary"><h2>Tóm tắt lượt học</h2>
        <p>{remembered.length} từ đã nhớ · {forgotten.length} từ cần ôn</p></section>
    </div>}
  </div></SessionBoundary></SiteLayout>;
}
