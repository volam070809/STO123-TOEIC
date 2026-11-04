import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import ReactMarkdown from "react-markdown";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import websiteFeatures from "../../data/websiteFeatures.json";
import "../../styles/ai-toeic-chat.css";

const api = "/api/ai/conversations";
const suggestions = ["Tôi nên học gì tiếp?", "Tìm bài học về thì hiện tại hoàn thành",
  "Tôi có thể thanh toán lại khi nào?", "Luyện Part 5 ở đâu?"];
const activeKey = id => `sto123-ai-active-${id}`;
const dateText = value => value ? new Date(value).toLocaleDateString("vi-VN") : "";
const contextLabel = context => context?.type === "VOCABULARY" ? "Theo từ đang học" :
  context?.type === "LESSON" ? "Theo bài học hiện tại" :
    context?.type === "PRACTICE" ? "Theo câu luyện tập" :
      context?.type === "COURSE_EXERCISE" ? "Theo câu bài tập đang học" : null;

function Markdown({ text }) {
  const allowed = href => typeof href === "string" &&
    (/^\/(?:vocabulary\?topic=[1-9]\d*|courses\?paymentId=[1-9]\d*|courses\/[1-9]\d*(?:\/steps\/[1-9]\d*)?)$/.test(href) ||
      websiteFeatures.some(feature => feature.link === href));
  return <ReactMarkdown components={{
    a: ({ href, children }) => allowed(href) ? <Link to={href}>{children}</Link> : <span>{children}</span>,
    img: ({ alt }) => <span>{alt || "Hình ảnh"}</span>,
  }}>{text}</ReactMarkdown>;
}

function Sparkle({ size = 24 }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="m12 2 1.7 6.3L20 10l-6.3 1.7L12 18l-1.7-6.3L4 10l6.3-1.7L12 2Z" fill="currentColor"/><path d="m19 17 .7 2.3L22 20l-2.3.7L19 23l-.7-2.3L16 20l2.3-.7L19 17Z" fill="currentColor"/></svg>;
}

export default function AiToeicChatWidget({ learningContext, embedded = false, active = false, onMinimize }) {
  const { pathname } = useLocation();
  const { user, token } = useAuth();
  const learnerId = user?.vaiTro === "HOC_VIEN" ? user.maNguoiDung : null;
  const hidden = !learnerId || /^\/exam\//.test(pathname) || pathname === "/placement";
  const [open, setOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [conversations, setConversations] = useState([]);
  const [activeId, setActiveId] = useState(null);
  const [messages, setMessages] = useState([]);
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [copiedId, setCopiedId] = useState(null);
  const endRef = useRef(null);
  const inputRef = useRef(null);

  useEffect(() => {
    if (!learnerId || !token || hidden) return undefined;
    let live = true;
    const savedId = Number(sessionStorage.getItem(activeKey(learnerId))) || null;
    setLoading(true);
    Promise.all([apiRequest(api, { token }), savedId ? apiRequest(`${api}/${savedId}`, { token }).catch(() => null) : null])
      .then(([list, detail]) => {
        if (!live) return;
        setConversations(list);
        if (list.some(row => row.id === savedId)) {
          setActiveId(savedId);
          setMessages(detail?.messages || []);
          setError(detail ? "" : "Chưa tải được tin nhắn. Hãy thử lại.");
        } else { setActiveId(null); setMessages([]); sessionStorage.removeItem(activeKey(learnerId)); setError(""); }
      }).catch(e => { if (live) setError(e.data?.message || e.message || "Không tải được hội thoại."); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [learnerId, token, hidden]);

  const visible = embedded ? active : open;
  useEffect(() => { if (visible) endRef.current?.scrollIntoView({ block: "end", behavior: "smooth" }); }, [visible, messages, pending]);

  function toggle() {
    if (embedded) { onMinimize?.(); return; }
    const next = !open;
    setOpen(next);
    if (next) setTimeout(() => inputRef.current?.focus(), 80);
  }
  function activate(id) { setActiveId(id); sessionStorage.setItem(activeKey(learnerId), String(id)); }
  async function refreshList() {
    const list = await apiRequest(api, { token });
    setConversations(list);
  }
  async function retryLoad() {
    setLoading(true); setError("");
    try {
      await refreshList();
      const id = activeId || Number(sessionStorage.getItem(activeKey(learnerId)));
      if (id) {
        const detail = await apiRequest(`${api}/${id}`, { token });
        activate(id); setMessages(detail.messages);
      }
    } catch (e) { setError(e.data?.message || e.message || "Chưa tải được hội thoại."); }
    finally { setLoading(false); }
  }
  async function newConversation() {
    if (busy) return;
    setError(""); setLoading(true);
    try {
      const row = await apiRequest(api, { method: "POST", token });
      activate(row.id); setMessages([]); setDraft(""); setPending(null); setHistoryOpen(false);
      setConversations(current => [row, ...current]); inputRef.current?.focus();
    } catch (e) { setError(e.data?.message || e.message); }
    finally { setLoading(false); }
  }
  async function selectConversation(id) {
    if (busy) return;
    setError(""); setLoading(true);
    try {
      const detail = await apiRequest(`${api}/${id}`, { token });
      activate(id); setMessages(detail.messages); setPending(null); setHistoryOpen(false);
    } catch (e) { setError(e.data?.message || e.message); }
    finally { setLoading(false); }
  }
  async function deleteConversation(id) {
    if (busy || !window.confirm("Xóa cuộc trò chuyện này và toàn bộ tin nhắn?")) return;
    setError(""); setLoading(true);
    try {
      await apiRequest(`${api}/${id}`, { method: "DELETE", token });
      setConversations(current => current.filter(row => row.id !== id));
      if (id === activeId) { setActiveId(null); setMessages([]); setPending(null); sessionStorage.removeItem(activeKey(learnerId)); }
    } catch (e) { setError(e.data?.message || e.message); }
    finally { setLoading(false); }
  }
  async function send(item) {
    if (busy || !item.text.trim()) return;
    setBusy(true); setError(""); setPending({ ...item, status: "sending" }); setDraft("");
    let id = activeId;
    try {
      if (!id) {
        const row = await apiRequest(api, { method: "POST", token });
        id = row.id; activate(id);
      }
      const result = await apiRequest(`${api}/${id}/messages`, {
        method: "POST", token, timeoutMs: 55_000,
        body: { requestId: item.requestId, message: item.text, context: item.context }
      });
      setMessages(current => [...current.filter(row => row.id !== result.userMessage.id && row.id !== result.aiMessage.id),
        result.userMessage, result.aiMessage]);
      setPending(null);
      await refreshList().catch(() => {});
    } catch (e) {
      setPending({ ...item, status: "failed" });
      setError(e.data?.message || e.message || "Chưa gửi được câu hỏi. Hãy thử lại.");
    } finally { setBusy(false); }
  }
  function submit(event) {
    event.preventDefault();
    const text = draft.trim();
    if (!text || busy) return;
    send({ text, requestId: crypto.randomUUID(), context: learningContext || null });
  }
  async function copy(text, id) {
    try { await navigator.clipboard.writeText(text); setCopiedId(id); setTimeout(() => setCopiedId(null), 1800); }
    catch { setError("Không sao chép được. Hãy chọn nội dung để sao chép thủ công."); }
  }

  if (hidden) return null;
  return <div className="ai-chat-shell">
    {visible && <section className="ai-chat-panel" role="dialog" aria-label="Trợ lý AI TOEIC">
      <header className="ai-chat-head"><span className="ai-chat-avatar"><Sparkle /></span><div><strong>Trợ lý AI TOEIC</strong><small>Giải thích dễ hiểu, học theo nhịp của bạn</small></div>
        <button type="button" className="ai-chat-head-button" aria-label="Lịch sử hội thoại" title="Lịch sử" onClick={() => setHistoryOpen(value => !value)}>☷</button>
        <button type="button" className="ai-chat-head-button" aria-label="Thu nhỏ chatbot" title="Thu nhỏ" onClick={toggle}>−</button></header>
      {historyOpen ? <div className="ai-chat-history"><div className="ai-chat-history-head"><strong>Lịch sử hội thoại</strong><button type="button" onClick={newConversation} disabled={busy || loading}>+ Cuộc mới</button></div>
        {conversations.length === 0 && <p>Chưa có cuộc trò chuyện nào.</p>}
        {conversations.map(row => <div className={row.id === activeId ? "ai-chat-history-row is-active" : "ai-chat-history-row"} key={row.id}>
          <button type="button" onClick={() => selectConversation(row.id)}><strong>{row.title}</strong><small>{dateText(row.updatedAt)} · {row.messageCount} tin nhắn</small></button>
          <button type="button" className="ai-chat-delete" aria-label={`Xóa ${row.title}`} title="Xóa" onClick={() => deleteConversation(row.id)}>×</button></div>)}
      </div> : <>
        <div className="ai-chat-toolbar"><span>{contextLabel(learningContext) || "Gia sư TOEIC của bạn"}</span><button type="button" onClick={newConversation} disabled={busy || loading}>+ Hội thoại mới</button></div>
        <div className="ai-chat-messages" aria-live="polite">
          {loading && <p className="ai-chat-state">Đang tải hội thoại…</p>}
          {!loading && messages.length === 0 && !pending && <div className="ai-chat-welcome"><div className="ai-chat-welcome-icon"><Sparkle size={30} /></div><h2>Hôm nay bạn muốn học gì?</h2><p>Hỏi về bài học, từ vựng, lộ trình hoặc cách dùng STO123.</p><div className="ai-chat-suggestions">{suggestions.map(text => <button type="button" key={text} onClick={() => { setDraft(text); inputRef.current?.focus(); }}>{text} <span>↗</span></button>)}</div></div>}
          {messages.map(row => <article key={row.id} className={`ai-chat-message ${row.role === "MODEL" ? "is-ai" : "is-user"}`}><div className="ai-chat-bubble">{row.role === "MODEL" ? <Markdown text={row.text} /> : <p>{row.text}</p>}</div>{row.role === "MODEL" && <button className="ai-chat-copy" type="button" onClick={() => copy(row.text, row.id)}>{copiedId === row.id ? "Đã sao chép" : "Sao chép"}</button>}</article>)}
          {pending && <article className="ai-chat-message is-user"><div className="ai-chat-bubble"><p>{pending.text}</p></div>{pending.status === "failed" && <button className="ai-chat-retry" type="button" onClick={() => send(pending)}>Thử gửi lại</button>}</article>}
          {pending?.status === "sending" && <div className="ai-chat-typing" role="status"><span /><span /><span /><small>AI đang suy nghĩ…</small></div>}
          <div ref={endRef} />
        </div>
        {error && <p className="ai-chat-error" role="alert">{error} {!pending && <button type="button" onClick={retryLoad}>Thử lại</button>}</p>}
        <form className="ai-chat-compose" onSubmit={submit}><textarea ref={inputRef} rows={2} maxLength={3000} value={draft} placeholder="Hỏi về bài học hoặc cách dùng STO123…" onChange={event => setDraft(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); submit(event); } }} aria-label="Câu hỏi cho AI" /><button type="submit" disabled={!draft.trim() || busy} aria-label="Gửi câu hỏi">➤</button></form>
        <p className="ai-chat-footnote">AI có thể nhầm lẫn. Hãy đối chiếu với bài học khi cần.</p>
      </>}
    </section>}
    {!embedded && <button type="button" className="ai-chat-launcher" aria-label={open ? "Thu nhỏ AI TOEIC" : "Hỏi AI TOEIC"} aria-expanded={open} onClick={toggle}><Sparkle /><span>Hỏi AI TOEIC</span></button>}
  </div>;
}
