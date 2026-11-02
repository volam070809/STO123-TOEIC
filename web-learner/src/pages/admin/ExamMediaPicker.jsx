import { useEffect, useState } from "react";
import { apiRequest, API_BASE_URL } from "../../services/api";

export default function ExamMediaPicker({ token, bank, kind, onSelect, onClose }) {
  const [prefix, setPrefix] = useState("");
  const [items, setItems] = useState([]);
  const [next, setNext] = useState(null);
  const [preview, setPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let live = true;
    setBusy(true); setError("");
    apiRequest(`/api/admin/content/media?${new URLSearchParams({ bank, prefix, pageSize: "50" })}`, { token })
      .then(data => { if (live) { setItems(data.items); setNext(data.next); } })
      .catch(e => { if (live) setError(e.message); })
      .finally(() => { if (live) setBusy(false); });
    return () => { live = false; };
  }, [bank, prefix, token]);

  useEffect(() => () => { if (preview?.url) URL.revokeObjectURL(preview.url); }, [preview]);

  async function more() {
    if (!next || busy) return;
    setBusy(true); setError("");
    try {
      const data = await apiRequest(`/api/admin/content/media?${new URLSearchParams({
        bank, prefix, pageSize: "50", continuationToken: next
      })}`, { token });
      setItems(old => [...old, ...data.items]); setNext(data.next);
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  async function view(item) {
    setError("");
    try {
      const response = await fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ bank, path: item.path })}`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Không tải được media từ Azure Blob.");
      setPreview({ url: URL.createObjectURL(await response.blob()), type: item.contentType });
    } catch (e) { setError(e.message); }
  }

  const matches = items.filter(item => kind === "audio" ?
    item.contentType === "audio/mpeg" || item.path.toLowerCase().endsWith(".mp3") :
    item.contentType?.startsWith("image/") || /\.(jpe?g|png|webp)$/i.test(item.path));
  return <div className="exam-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose(); }}><aside className="exam-drawer" role="dialog" aria-modal="true" aria-label="Chọn media"><div className="exam-drawer-head"><div><h2>Chọn {kind === "audio" ? "audio" : "hình ảnh"}</h2><p>{bank === "DE_THI" ? "Ngân hàng đề thi" : "Ngân hàng luyện tập"}</p></div><button type="button" onClick={onClose}>Đóng</button></div>
    <label>Tìm theo đường dẫn<input value={prefix} onChange={event => setPrefix(event.target.value)} placeholder="question-media/…"/></label>
    {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {busy && <p>Đang tải thư viện…</p>}
    {preview && <div className="exam-media-field">{preview.type?.startsWith("audio/") ? <audio controls src={preview.url}/> : <img className="exam-media-preview" src={preview.url} alt="Xem trước media"/>}</div>}
    <div className="exam-group-list">{matches.map(item => <div className="exam-group-row" key={item.path}><div><strong>{item.path}</strong><span>{Math.round((item.size || 0) / 1024)} KB</span></div><div className="exam-unit-actions"><button type="button" onClick={() => view(item)}>Xem</button><button type="button" onClick={() => onSelect(item.path)}>Chọn</button></div></div>)}{!busy && matches.length === 0 && <p>Không tìm thấy tệp phù hợp. Hãy tải tệp mới hoặc tìm theo đường dẫn khác.</p>}</div>
    {next && <button type="button" disabled={busy} onClick={more}>Tải thêm</button>}
  </aside></div>;
}
