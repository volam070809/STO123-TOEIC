import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import { arenaApi, arenaConnection } from "../../services/arenaApi";
import { PrivateImage, PrivateAudio } from "../exam/ExamMedia";
import { examMediaBlob } from "../../services/examApi";
import { initialsFromName } from "../../components/auth/avatarInitials";
import DocumentRenderer from "../exam/DocumentRenderer";
import { preferFreshArenaState } from "./arenaState";
import "../../styles/arena.css";

const names = ["Mô tả hình ảnh", "Hỏi và đáp", "Hội thoại", "Bài nói ngắn", "Hoàn thành câu", "Hoàn thành đoạn văn", "Đọc hiểu"];
const statusLabels = { WAITING: "Đang tìm đối thủ", READY: "Chuẩn bị", STARTING: "Sắp bắt đầu", ACTIVE: "Đang đấu", REVEAL: "Đáp án", COMPLETED: "Đã hoàn thành", SURRENDERED: "Đã đầu hàng", ABANDONED: "Đã kết thúc" };

function ArenaPlayerAvatar({ player, token, size = "normal" }) {
  const [image, setImage] = useState("");
  const [failed, setFailed] = useState("");
  useEffect(() => {
    if (!player?.avatarEndpoint) { setImage(""); return undefined; }
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(player.avatarEndpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setImage(objectUrl); }
    }).catch(() => { if (!controller.signal.aborted) setImage(""); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [player?.avatarEndpoint, token]);
  const src = player?.avatarEndpoint ? image : player?.avatarUrl;
  return <span className={`arena-avatar arena-avatar-${size}`} role="img" aria-label={`Ảnh đại diện của ${player?.name || "học viên"}`}>
    <span>{initialsFromName(player?.name)}</span>{src && failed !== src && <img src={src} alt="" onError={() => setFailed(src)} />}
  </span>;
}

function ArenaVersus({ state, token, compact = false }) {
  return <div className={compact ? "arena-vs arena-vs-compact" : "arena-vs"}>
    <div className="arena-vs-player"><ArenaPlayerAvatar player={state.you} token={token} size={compact ? "small" : "normal"} />
      <span className="arena-vs-role">BẠN</span><strong>{state.you.name}</strong>
      <small className={state.you.online ? "is-online" : ""}>{state.you.online ? "● Đang kết nối" : "○ Chưa kết nối"}</small>
      {!compact && <b className={state.you.ready ? "is-ready" : ""}>{state.you.ready ? "Đã sẵn sàng" : "Chưa sẵn sàng"}</b>}</div>
    <span className="arena-vs-mark" aria-hidden="true">VS</span>
    {state.opponent ? <div className="arena-vs-player"><ArenaPlayerAvatar player={state.opponent} token={token} size={compact ? "small" : "normal"} />
      <span className="arena-vs-role">ĐỐI THỦ</span><strong>{state.opponent.name}</strong>
      <small className={state.opponent.online ? "is-online" : ""}>{state.opponent.online ? "● Đang kết nối" : "○ Mất kết nối"}</small>
      {!compact && <b className={state.opponent.ready ? "is-ready" : ""}>{state.opponent.ready ? "Đã sẵn sàng" : "Chưa sẵn sàng"}</b>}</div> :
      <div className="arena-searching" role="status"><span className="arena-search-ring" aria-hidden="true" />
        <strong>Đang tìm đối thủ</strong><small>Chưa có người chơi thứ hai</small></div>}
  </div>;
}

function ArenaDocuments({ question, token }) {
  const hasMatchingDocument = question.documents?.some(doc =>
    typeof doc.content === "string" && doc.content.trim() === question.context?.trim());
  return <>{question.context && !hasMatchingDocument && <div className="arena-context">{question.context}</div>}
    {question.documents?.map(doc => {
      let content = doc.content;
      if (["EMAIL", "TABLE", "CHAT", "FORM"].includes(doc.type) &&
          typeof content === "string" && content.trim().startsWith("{")) {
        try { content = JSON.parse(content); } catch { /* keep original text */ }
      }
      return <DocumentRenderer key={doc.order} document={{ ...doc, content }} token={token} ImageComponent={PrivateImage} />;
    })}
    {question.imageUrl && <PrivateImage endpoint={question.imageUrl} token={token} alt="Hình câu hỏi" />}
    {question.audioUrl && <PrivateAudio endpoint={question.audioUrl} token={token} mode="MOCK" />}
  </>;
}

export function ArenaLobbyPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [part, setPart] = useState(5);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [activeMatch, setActiveMatch] = useState(null);
  const [activeLookup, setActiveLookup] = useState({ loading: true, error: "" });
  const [activeRetry, setActiveRetry] = useState(0);
  useEffect(() => {
    let active = true;
    arenaApi.active(token).then(match => {
      if (active) { setActiveMatch(match); setActiveLookup({ loading: false, error: "" }); }
    }).catch(e => {
      if (active) setActiveLookup({ loading: false,
        error: e.message || "Không thể kiểm tra trận đang dở." });
    });
    return () => { active = false; };
  }, [token, activeRetry]);

  async function begin(kind) {
    if (busy) return;
    setBusy(kind); setError("");
    try {
      const match = kind === "quick" ? await arenaApi.quick(part, token) :
        kind === "private" ? await arenaApi.privateRoom(part, token) : await arenaApi.join(code, token);
      navigate(`/arena/matches/${match.id}`);
    } catch (e) { setError(e.message || "Không thể tạo trận. Vui lòng thử lại."); setBusy(""); }
  }
  return <SiteLayout><div className="arena-page site-container">
    <header className="arena-hero"><div><span className="arena-eyebrow">TOEIC ARENA · 1 ĐỐI 1</span>
      <h1>Thử sức cùng người học khác</h1><p>Hai người cùng làm một bộ câu hỏi. Đáp án được khóa ngay khi chọn, sau đó cùng xem kết quả từng câu.</p></div>
      <span className="arena-hero-number" aria-hidden="true">1:1</span></header>
    {activeMatch && <div className="arena-resume"><span><strong>Bạn có trận Part {activeMatch.part} cần tiếp tục</strong><small>{statusLabels[activeMatch.status]}</small></span>
      <Link className="arena-primary" to={`/arena/matches/${activeMatch.id}`}>Trở lại trận</Link></div>}
    {activeLookup.loading && <p className="arena-lobby-status" role="status">Đang kiểm tra trận đang dở…</p>}
    {activeLookup.error && <div className="arena-error arena-lobby-status" role="alert">{activeLookup.error}
      <button type="button" onClick={() => { setActiveLookup({ loading: true, error: "" });
        setActiveRetry(value => value + 1); }}>Thử lại</button></div>}
    {!activeMatch && !activeLookup.loading && !activeLookup.error && <section className="arena-panel arena-entry"><div className="arena-section-heading"><div><span className="arena-eyebrow">BƯỚC 01 · CHỌN NỘI DUNG</span><h2>Chọn Part thi đấu</h2><p>Chọn một Part, sau đó tìm đối thủ hoặc mời bạn bè vào phòng riêng.</p></div><Link to="/smart-learning">Xem năng lực của tôi →</Link></div>
      <div className="arena-part-grid">{names.map((name, index) => <button type="button" key={name} className={part === index + 1 ? "is-selected" : ""}
        onClick={() => setPart(index + 1)} aria-pressed={part === index + 1}><strong>Part {index + 1}</strong><span>{name}</span></button>)}</div>
      <div className="arena-entry-next"><div><span className="arena-eyebrow">BƯỚC 02 · CHỌN CÁCH GHÉP</span>
        <strong>Part {part} · {names[part - 1]}</strong><small>Đối thủ sẽ làm cùng Part với bạn.</small></div>
        <div className="arena-actions arena-entry-actions"><button className="arena-primary" type="button" onClick={() => begin("quick")} disabled={!!busy}>{busy === "quick" ? "Đang tìm trận…" : "Tìm trận nhanh"}</button>
          <button className="arena-secondary" type="button" onClick={() => begin("private")} disabled={!!busy}>{busy === "private" ? "Đang tạo phòng…" : "Tạo phòng riêng"}</button></div></div>
      <form className="arena-join" onSubmit={event => { event.preventDefault(); begin("join"); }}><div><label htmlFor="arena-code">Đã có mã phòng?</label><small>Nhập mã do bạn bè gửi để vào trận của họ.</small></div>
        <input id="arena-code" value={code} onChange={event => setCode(event.target.value.toUpperCase())} placeholder="Mã 8 ký tự" maxLength={8} autoComplete="off" />
        <button className="arena-secondary" disabled={!!busy || code.trim().length !== 8}>Vào phòng</button></form>
      {error && <p role="alert" className="arena-error">{error}</p>}</section>}
  </div></SiteLayout>;
}

export function ArenaMatchPage() {
  const { id } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const connection = useRef(null);
  const [state, setState] = useState(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [connectionStatus, setConnectionStatus] = useState("connecting");
  const [busy, setBusy] = useState("");
  const [confirmSurrender, setConfirmSurrender] = useState(false);
  const [clock, setClock] = useState(0);
  const acceptState = useCallback(next => {
    setState(current => preferFreshArenaState(current, next));
  }, []);
  const refresh = useCallback(async () => {
    const next = await arenaApi.state(id, token);
    acceptState(next); setError(""); return next;
  }, [id, token, acceptState]);

  useEffect(() => {
    let active = true;
    const hub = arenaConnection(token);
    connection.current = hub;
    hub.on("ArenaState", next => { if (active && next.id === id) { acceptState(next); setError(""); } });
    hub.onreconnecting(() => { if (active) setConnectionStatus("reconnecting"); });
    hub.onreconnected(() => { if (active) { setConnectionStatus("connected"); hub.invoke("Sync", id).then(acceptState).catch(() => refresh().catch(() => {})); } });
    hub.onclose(() => { if (active) setConnectionStatus("offline"); });
    arenaApi.state(id, token).then(next => { if (active) { acceptState(next); setError(""); } })
      .catch(e => { if (active) setError(e.message || "Không thể tải trận đấu."); });
    hub.start().then(() => { if (active) { setConnectionStatus("connected"); hub.invoke("Sync", id).then(acceptState).catch(() => {}); } })
      .catch(() => { if (active) setConnectionStatus("offline"); });
    const poll = setInterval(() => { if (active) refresh().catch(() => {}); }, 5000);
    const timer = setInterval(() => setClock(Date.now()), 500);
    return () => { active = false; clearInterval(poll); clearInterval(timer); connection.current = null; hub.stop().catch(() => {}); };
  }, [id, token, refresh, acceptState]);

  async function act(label, invoke, fallback) {
    if (busy) return;
    setBusy(label); setError("");
    try {
      const next = connection.current?.state === "Connected" ? await invoke() : await fallback();
      acceptState(next);
    } catch (e) { setError(e.message || "Thao tác chưa hoàn tất. Hãy đồng bộ lại trận."); await refresh().catch(() => {}); }
    finally { setBusy(""); }
  }
  async function playAgain() {
    if (!state || busy) return;
    setBusy("again"); setError("");
    try { const next = await arenaApi.quick(state.part, token); navigate(`/arena/matches/${next.id}`); }
    catch (e) { setError(e.message || "Không thể bắt đầu trận mới."); setBusy(""); }
  }
  async function leave() {
    if (!window.confirm("Rời phòng chờ này?")) return;
    try { await arenaApi.leave(id, token); navigate("/arena"); }
    catch (e) { setError(e.message || "Chưa thể rời phòng."); await refresh().catch(() => {}); }
  }
  async function surrender() {
    if (busy) return;
    setBusy("surrender"); setError("");
    try {
      const next = await arenaApi.surrender(id, token);
      acceptState(next); setConfirmSurrender(false);
    } catch (e) { setError(e.message || "Chưa thể đầu hàng. Hãy đồng bộ trận đấu."); await refresh().catch(() => {}); }
    finally { setBusy(""); }
  }
  async function copyCode() {
    if (!state?.roomCode) return;
    if (!navigator.clipboard) { setError("Hãy sao chép mã phòng thủ công."); return; }
    try { await navigator.clipboard.writeText(state.roomCode); setNotice("Đã sao chép mã phòng."); }
    catch { setError("Hãy sao chép mã phòng thủ công."); }
  }
  const deadline = state?.status === "ACTIVE" ? state.deadlineUtc : state?.status === "REVEAL" ? state.revealUntilUtc : state?.status === "STARTING" ? state.startsAtUtc : null;
  const seconds = deadline && clock ? Math.max(0, Math.ceil((new Date(deadline).getTime() - clock) / 1000)) : null;
  const question = state?.question;
  const revealing = state?.status === "REVEAL" || state?.status === "COMPLETED";
  const inMatch = ["STARTING", "ACTIVE", "REVEAL"].includes(state?.status);
  const roomClosed = state?.status === "ABANDONED" && !state.startsAtUtc;
  return <SiteLayout focused={inMatch}><div className="arena-page site-container">
    <div className="arena-match-nav">{!inMatch ? <Link to="/arena">← Đấu trường</Link> :
      <strong>STO123 · TOEIC ARENA</strong>}
      {state?.status !== "COMPLETED" && <span>{connectionStatus === "connected" ? "● Đang đồng bộ" : "○ Đang kết nối lại"}</span>}
      {inMatch && <button className="arena-quiet" type="button" onClick={() => setConfirmSurrender(true)}>Đầu hàng</button>}</div>
    {error && <div className="arena-error" role="alert">{error} <button type="button" onClick={() => refresh().catch(e => setError(e.message))}>Thử lại</button></div>}
    {notice && <p role="status" className="arena-info">{notice}</p>}
    {!state && !error && <p role="status" className="arena-panel">Đang tải trận đấu…</p>}
    {state && <>{(state.status === "WAITING" || state.status === "READY" || roomClosed) ?
      <header className="arena-room-heading"><div><span className="arena-eyebrow">ĐẤU TRƯỜNG · PART {state.part}</span><h1>{names[state.part - 1]}</h1></div>
        <span className="arena-room-step">{roomClosed ? "PHÒNG ĐÃ ĐÓNG" : state.opponent ? "BƯỚC 03 · SẴN SÀNG" : "BƯỚC 02 · GHÉP TRẬN"}</span></header> : state.status === "COMPLETED" ? null :
      <header className="arena-match-header"><div><span className="arena-eyebrow">PART {state.part} · {names[state.part - 1]}</span><h1>{statusLabels[state.status] || state.status}</h1>
      <p>{state.status === "ACTIVE" ? `Câu ${state.questionIndex + 1} / ${state.totalQuestions}` : state.status === "REVEAL" ? "Cùng xem đáp án và điểm số" : "Hai người làm cùng bộ câu hỏi"}</p></div>
      <div className="arena-score" aria-label="Tỉ số hiện tại"><div className="arena-score-player"><ArenaPlayerAvatar player={state.you} token={token} size="small" />
        <span><small>Bạn · {state.you.name}</small><strong>{state.you.score}</strong></span></div><b>:</b>
        {state.opponent ? <div className="arena-score-player"><ArenaPlayerAvatar player={state.opponent} token={token} size="small" />
          <span><small>Đối thủ · {state.opponent.name}</small><strong>{state.opponent.score}</strong></span></div> :
          <span className="arena-score-empty">Chưa ghép đối thủ</span>}</div></header>}
      {(state.status === "WAITING" || state.status === "READY") && <section className="arena-panel arena-waiting">
        <div className="arena-waiting-heading"><span className="arena-eyebrow">PHÒNG ĐẤU · PART {state.part}</span>
          <h2>{state.opponent ? "Đối thủ đã vào phòng" : state.roomCode ? "Mời bạn bè vào phòng" : "Đang tìm đối thủ phù hợp"}</h2>
          <p>{state.opponent ? "Cả hai nhấn Sẵn sàng để bắt đầu cùng lúc." :
            state.roomCode ? "Gửi mã phòng cho người chơi khác. Chỉ người đăng nhập mới có thể tham gia." :
              "Bạn sẽ nhìn thấy tên và ảnh của đối thủ ngay khi ghép trận thành công."}</p></div>
        <ArenaVersus state={state} token={token} />
        {state.roomCode && <div className="arena-invite"><span>Mã phòng riêng</span><strong>{state.roomCode}</strong>
          <button type="button" className="arena-secondary" onClick={copyCode}>Sao chép mã</button></div>}
        <div className="arena-actions arena-wait-actions">{state.opponent && !state.you.ready && <button type="button" className="arena-primary" disabled={!!busy} onClick={() => act("ready", () => connection.current.invoke("Ready", id), () => arenaApi.ready(id, token))}>{state.opponent.ready ? "Sẵn sàng để bắt đầu" : "Tôi đã sẵn sàng"}</button>}
          {state.opponent && state.you.ready && <span className="arena-info" role="status">Đang chờ đối thủ sẵn sàng…</span>}
          {!state.opponent && !state.roomCode && <button type="button" className="arena-secondary" disabled={!!busy} onClick={() => act("invite", () => arenaApi.invite(id, token), () => arenaApi.invite(id, token))}>Chuyển sang phòng riêng</button>}
          <button type="button" className="arena-quiet" disabled={!!busy} onClick={leave}>Rời phòng chờ</button></div>
      </section>}
      {state.status === "STARTING" && <section className="arena-panel arena-countdown" role="status">
        <ArenaVersus state={state} token={token} compact />
        <p>Trận đấu bắt đầu sau</p><b>{seconds ?? 3}</b></section>}
      {(state.status === "ACTIVE" || state.status === "REVEAL") && question && <section className="arena-panel arena-question">
        <div className="arena-question-meta"><strong>Part {state.part} · Câu {question.number} / {state.totalQuestions}</strong>
          <small>{state.opponent?.online ? state.opponentSubmitted ? "Đối thủ đã trả lời" : "Đối thủ đang làm bài" : "Đối thủ mất kết nối"}</small>
          <span aria-label={`Còn ${seconds ?? 0} giây`}>{seconds == null ? "" : `${seconds}s`}</span></div>
        <ArenaDocuments key={`${id}-${state.questionIndex}`} question={question} token={token} />
        {question.content && <h2>{question.content}</h2>}
        {!question.content && state.part <= 2 && <h2 className="arena-listening-prompt">Nghe âm thanh và chọn đáp án đúng.</h2>}
        <div className="arena-choices">{question.optionLetters.map(letter => {
          const selected = state.selected === letter;
          const correct = revealing && question.correct === letter;
          return <button type="button" key={letter} className={`${selected ? "is-picked " : ""}${correct ? "is-correct " : ""}${revealing && selected && !correct ? "is-wrong" : ""}`}
            disabled={state.status !== "ACTIVE" || state.submitted || !!busy || seconds === 0}
            onClick={() => act("answer", () => connection.current.invoke("SubmitAnswer", id, state.questionIndex, letter), () => arenaApi.answer(id, state.questionIndex, letter, token))}>
            <b>{letter}</b>{state.part > 2 || revealing ? <span>{question[letter.toLowerCase()]}</span> :
              <span className="arena-audio-choice">Đáp án {letter}</span>}</button>;
        })}</div>
        {state.submitted && !revealing && <p className="arena-info" role="status">Đã khóa đáp án {state.selected}. {state.opponentSubmitted ? "Đang đối chiếu kết quả…" : "Đang chờ đối thủ hoặc hết giờ."}</p>}
        {revealing && <div className="arena-reveal"><strong>{state.result?.youCorrect ? "Bạn trả lời đúng!" : "Bạn chưa trả lời đúng."}</strong>
          <p>Đáp án: {question.correct}. {state.result?.opponentCorrect ? "Đối thủ trả lời đúng." : "Đối thủ chưa trả lời đúng."}</p>
          <p className="arena-leading">{state.result?.leading === "TIE" ? "Hai bạn đang hòa nhau." :
            state.result?.leading === "YOU" ? "Bạn đang dẫn trước." : "Đối thủ đang dẫn trước."}</p>
          {question.explanation && <p>{question.explanation}</p>}</div>}
      </section>}
      {state.status === "COMPLETED" && <section className="arena-panel arena-result-stage" data-outcome={state.you.score === state.opponent?.score ? "draw" : state.you.score > (state.opponent?.score ?? 0) ? "win" : "loss"}>
        <span className="arena-eyebrow">PART {state.part} · TRẬN ĐẤU ĐÃ KẾT THÚC</span>
        <h2>{state.you.score === state.opponent?.score ? "Một trận hòa đầy nỗ lực" : state.you.score > (state.opponent?.score ?? 0) ? "Chiến thắng thuộc về bạn!" : "Hẹn bạn ở trận tiếp theo"}</h2>
        <p>{state.you.score === state.opponent?.score ? "Hai bạn đã có kết quả ngang nhau." : state.you.score > (state.opponent?.score ?? 0) ? "Bạn đã vượt qua đối thủ trong trận này." : "Bạn đã hoàn thành trận đấu. Thử sức thêm một lần nữa nhé."}</p>
        <div className="arena-result-scoreboard"><div><span>BẠN</span><strong>{state.you.name || "Bạn"}</strong><b>{state.you.score}</b></div>
          <span aria-hidden="true">—</span><div><span>ĐỐI THỦ</span><strong>{state.opponent?.name || "Đối thủ"}</strong><b>{state.opponent?.score ?? 0}</b></div></div>
        <div className="arena-actions"><button className="arena-primary" type="button" disabled={!!busy} onClick={playAgain}>{busy === "again" ? "Đang tìm trận…" : "Chơi trận mới →"}</button>
          <Link className="arena-secondary" to="/arena">Về đấu trường</Link></div>
        <details className="arena-review"><summary>Chi tiết trận đấu</summary>{state.details?.length ? state.details.map(item => <article key={item.number}><strong>Câu {item.number}: {item.youCorrect ? "Đúng" : "Sai"}</strong>
          <p>{item.content}</p><small>Bạn: {item.youSelected || "Bỏ qua"} · Đối thủ: {item.opponentSelected || "Bỏ qua"} · Đúng: {item.correct}</small>
          {item.explanation && <p>{item.explanation}</p>}</article>) : <p>Chưa có chi tiết câu hỏi cho trận này.</p>}</details></section>}
      {(state.status === "SURRENDERED" || state.status === "ABANDONED") && <section className="arena-panel arena-ended">
        <span className="arena-eyebrow">KẾT QUẢ TRẬN ĐẤU</span>
        <h2>{roomClosed ? "Phòng chờ đã đóng" : state.status === "SURRENDERED" ? state.winnerId === state.you.id ? "Bạn thắng do đối thủ đầu hàng" : "Bạn đã đầu hàng" :
          state.winnerId === state.you.id ? "Bạn thắng do đối thủ rời trận" : state.winnerId ? "Trận kết thúc do gián đoạn" : "Trận kết thúc vì cả hai không trả lời"}</h2>
        <p>{roomClosed ? "Chủ phòng đã rời hoặc thời gian chờ đã hết. Bạn có thể tạo trận mới." :
          "Trận không hoàn thành đủ câu nên không được tính vào phân tích năng lực."}</p>
        <Link className="arena-primary" to="/arena">Về đấu trường</Link></section>}
    </>}
    {confirmSurrender && inMatch && <div className="arena-dialog-backdrop" role="presentation" onClick={() => setConfirmSurrender(false)}>
      <div className="arena-dialog" role="dialog" aria-modal="true" aria-labelledby="arena-surrender-title" onClick={event => event.stopPropagation()}>
        <h2 id="arena-surrender-title">Đầu hàng trận này?</h2><p>Đối thủ sẽ thắng trận. Kết quả chưa hoàn thành không tính vào phân tích năng lực.</p>
        <div className="arena-actions"><button type="button" className="arena-secondary" onClick={() => setConfirmSurrender(false)}>Tiếp tục đấu</button>
          <button type="button" className="arena-danger" disabled={!!busy} onClick={surrender}>{busy ? "Đang xử lý…" : "Xác nhận đầu hàng"}</button></div></div></div>}
  </div></SiteLayout>;
}
