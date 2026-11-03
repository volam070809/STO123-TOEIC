import { Component, useEffect, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest, API_BASE_URL } from "../../services/api";
import AdminSectionStats from "../../components/admin/AdminSectionStats";
import "../../styles/admin-vocabulary.css";

const root = "/api/admin/vocabulary";
const blankTopic = { tenChuDe: "", moTa: "", trangThai: "DANG_MO" };
const blankWord = { word: "", nghia: "", phienAm: "", loaiTu: "", viDu: "", dichViDu: "", topicIds: [] };
const partsOfSpeech = [
  ["noun", "Danh từ"], ["verb", "Động từ"], ["adjective", "Tính từ"],
  ["adverb", "Trạng từ"], ["pronoun", "Đại từ"], ["preposition", "Giới từ"],
  ["conjunction", "Liên từ"], ["interjection", "Thán từ"],
  ["determiner", "Từ hạn định"], ["article", "Mạo từ"],
  ["modal verb", "Động từ khuyết thiếu"], ["phrasal verb", "Cụm động từ"],
  ["phrase", "Cụm từ"], ["idiom", "Thành ngữ"]
];
const informationFields = [["word", "Từ vựng"], ["nghia", "Nghĩa tiếng Việt"], ["phienAm", "Phiên âm"],
  ["loaiTu", "Loại từ"], ["viDu", "Ví dụ"], ["dichViDu", "Dịch ví dụ"]];
const missingInfo = word => ["phienAm", "loaiTu", "viDu", "dichViDu"].some(key => !word[key]?.trim());

export default function AdminVocabularyPage() {
  return <VocabularyErrorBoundary><AdminVocabularyContent /></VocabularyErrorBoundary>;
}

class VocabularyErrorBoundary extends Component {
  state = { failed: false, retry: 0 };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error) { console.error("Admin vocabulary render failed", error); }
  render() {
    if (this.state.failed) return <div className="admin-page av-page"><div className="av-alert" role="alert"><span>Không thể hiển thị trang từ vựng. Vui lòng thử lại.</span><button type="button" onClick={() => this.setState(current => ({ failed: false, retry: current.retry + 1 }))}>Thử lại</button></div></div>;
    return <div key={this.state.retry}>{this.props.children}</div>;
  }
}

function AdminVocabularyContent() {
  const { token } = useAuth();
  const [topics, setTopics] = useState([]);
  const [topicOptions, setTopicOptions] = useState([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [topicSearch, setTopicSearch] = useState("");
  const [topic, setTopic] = useState(null);
  const [words, setWords] = useState([]);
  const [wordSearch, setWordSearch] = useState("");
  const [filter, setFilter] = useState("");
  const [drawer, setDrawer] = useState(null);
  const [form, setForm] = useState(null);
  const [image, setImage] = useState(null);
  const [removeCover, setRemoveCover] = useState(false);
  const [previewUrl, setPreviewUrl] = useState("");
  const [coverVersion, setCoverVersion] = useState(0);
  const [statsRevision, setStatsRevision] = useState(0);
  const [audioVersion, setAudioVersion] = useState(0);
  const [existingQuery, setExistingQuery] = useState("");
  const [existingWords, setExistingWords] = useState([]);
  const [newTopicName, setNewTopicName] = useState("");
  const [sections, setSections] = useState({ audio: true, info: false, topics: false });
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const call = (path, options = {}) => apiRequest(root + path, { token, ...options });
  const coverUrl = id => `${API_BASE_URL}${root}/topics/${id}/cover?v=${coverVersion}`;
  async function loadTopics() {
    const result = await call(`/topics?q=${encodeURIComponent(topicSearch)}&page=${page}`);
    if (!Array.isArray(result?.items)) throw new Error("Dữ liệu chủ đề không hợp lệ. Hãy thử tải lại.");
    setTopics(result.items); setTotal(result.total ?? 0);
    return result.items;
  }
  async function loadWords(id = topic?.maChuDe) {
    if (!id) return [];
    const result = await call(`/topics/${id}/words?q=${encodeURIComponent(wordSearch)}&filter=${filter}`);
    if (!Array.isArray(result)) throw new Error("Dữ liệu từ vựng không hợp lệ. Hãy thử tải lại.");
    setWords(result);
    return result;
  }
  async function loadOptions() {
    const result = await call("/topic-options");
    if (!Array.isArray(result)) throw new Error("Không tải được danh sách chủ đề liên kết.");
    setTopicOptions(result);
  }
  async function retryLoad() {
    setError(""); setLoading(true);
    try { await Promise.all([loadTopics(), loadOptions(), topic ? loadWords() : Promise.resolve()]); }
    catch (e) { setError(e.message); }
    finally { setLoading(false); }
  }
  useEffect(() => {
    let active = true;
    setLoading(true);
    const request = topic ? loadWords() : loadTopics();
    request.catch(e => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [topic?.maChuDe, topic ? wordSearch : topicSearch, topic ? filter : page, token]);
  useEffect(() => { loadOptions().catch(e => setError(e.message)); }, [token]);
  useEffect(() => {
    if (!topic || !existingQuery.trim()) { setExistingWords([]); return; }
    let active = true;
    const timer = setTimeout(() => call(`/word-options?q=${encodeURIComponent(existingQuery)}&topicId=${topic.maChuDe}`)
      .then(result => { if (active) setExistingWords(Array.isArray(result) ? result : []); })
      .catch(e => { if (active) setError(e.message); }), 250);
    return () => { active = false; clearTimeout(timer); };
  }, [existingQuery, topic?.maChuDe, token]);
  useEffect(() => {
    if (!image) { setPreviewUrl(""); return; }
    const url = URL.createObjectURL(image);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [image]);
  useEffect(() => {
    if (!dirty) return;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  function close(force = false) {
    if (!force && dirty && !window.confirm("Bạn có thay đổi chưa lưu. Đóng mà không lưu?")) return;
    setDrawer(null); setDirty(false); setImage(null); setRemoveCover(false);
    setExistingQuery(""); setNewTopicName("");
  }
  function editTopic(item = null) {
    setDrawer({ type: "topic", item }); setForm(item ? { ...item } : { ...blankTopic });
    setImage(null); setRemoveCover(false); setDirty(false); setError("");
  }
  function editWord(item = null) {
    setDrawer({ type: "word", item });
    setForm(item ? { ...item, topicIds: [...(item.topicIds || [])] } : { ...blankWord, topicIds: [topic.maChuDe] });
    setSections({ audio: true, info: !item, topics: !item });
    setExistingQuery(""); setNewTopicName(""); setDirty(false); setError("");
  }
  function change(key, value) { setForm(current => ({ ...current, [key]: value })); setDirty(true); }
  async function updateLists() {
    await Promise.all([loadTopics(), loadOptions(), topic ? loadWords() : Promise.resolve()]);
  }
  async function afterSaved(message) {
    setNotice(message);
    setStatsRevision(value => value + 1);
    try { await updateLists(); }
    catch (e) { setError(`${message} Không tải lại được danh sách: ${e.message}`); }
  }
  async function saveTopic() {
    if (busy) return;
    if (!form.tenChuDe?.trim()) { setError("Nhập tên chủ đề."); return; }
    if (image && (!/\.jpg$/i.test(image.name) || image.size > 5_000_000 || image.type !== "image/jpeg")) {
      setError("Chỉ nhận ảnh JPG tối đa 5 MB."); return;
    }
    setBusy(true); setError(""); setNotice("");
    try {
      let id = drawer.item?.maChuDe;
      if (id) await call(`/topics/${id}`, { method: "PUT", body: { tenChuDe: form.tenChuDe, moTa: form.moTa, trangThai: form.trangThai } });
      else {
        const created = await call("/topics", { method: "POST", body: { tenChuDe: form.tenChuDe, moTa: form.moTa } });
        id = created.maChuDe;
        setDrawer({ type: "topic", item: { maChuDe: id } });
        setForm(current => ({ ...current, maChuDe: id }));
        if (form.trangThai === "DA_DONG")
          await call(`/topics/${id}`, { method: "PUT", body: { tenChuDe: form.tenChuDe, moTa: form.moTa, trangThai: form.trangThai } });
      }
      if (image) {
        const data = new FormData(); data.set("file", image);
        await call(`/topics/${id}/cover`, { method: "POST", body: data });
        setCoverVersion(value => value + 1);
      } else if (removeCover && form.duongDanAnhBia) {
        await call(`/topics/${id}/cover`, { method: "DELETE" });
        setCoverVersion(value => value + 1);
      }
      if (topic?.maChuDe === id) setTopic(current => ({ ...current, ...form, maChuDe: id,
        duongDanAnhBia: removeCover ? null : image ? "updated" : current.duongDanAnhBia }));
      close(true);
      await afterSaved("Đã lưu chủ đề.");
    } catch (e) {
      setError(`${e.message} Nếu thông tin đã được lưu, dùng Thử tải lại để kiểm tra trước khi lưu tiếp.`);
      try { await updateLists(); } catch { /* Giữ lỗi thao tác gốc. */ }
    } finally { setBusy(false); }
  }
  async function deleteTopic() {
    if (!drawer?.item || busy || !window.confirm(`Xóa chủ đề “${form.tenChuDe}”? Chủ đề có từ, lịch sử học hoặc liên kết khóa học sẽ được giữ lại.`)) return;
    setBusy(true); setError("");
    try {
      await call(`/topics/${drawer.item.maChuDe}`, { method: "DELETE" });
      if (topic?.maChuDe === drawer.item.maChuDe) { setTopic(null); setWords([]); }
      close(true); await afterSaved("Đã xóa chủ đề.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  async function saveWord() {
    if (busy) return;
    if (!form.word?.trim() || !form.nghia?.trim() || !form.topicIds.length) {
      setError("Nhập từ, nghĩa và chọn ít nhất một chủ đề."); return;
    }
    if (form.loaiTu && !partsOfSpeech.some(([value]) => value === form.loaiTu) &&
        form.loaiTu !== drawer.item?.loaiTu) {
      setError("Loại từ không hợp lệ. Hãy chọn một mục trong danh sách."); return;
    }
    setBusy(true); setError(""); setNotice("");
    try {
      const id = drawer.item?.maTuVung;
      await call(id ? `/words/${id}` : "/words", { method: id ? "PUT" : "POST", body: form });
      close(true); await afterSaved("Đã lưu từ vựng.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  async function linkExisting(word) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      await call(`/topics/${topic.maChuDe}/words/${word.maTuVung}`, { method: "POST" });
      close(true); await afterSaved("Đã thêm từ vào chủ đề.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  async function createLinkedTopic() {
    const name = newTopicName.trim();
    if (!name || busy) return;
    setBusy(true); setError("");
    try {
      const created = await call("/topics", { method: "POST", body: { tenChuDe: name, moTa: "" } });
      change("topicIds", [...form.topicIds, created.maChuDe]);
      setNewTopicName("");
      await loadOptions();
      setStatsRevision(value => value + 1);
      setNotice("Đã tạo chủ đề. Nhấn Lưu để liên kết với từ này.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  async function unlink() {
    if (busy || !window.confirm(`Gỡ “${form.word}” khỏi chủ đề này? Từ và tiến độ học sẽ được giữ nguyên.${dirty ? " Các thay đổi chưa lưu sẽ bị bỏ." : ""}`)) return;
    setBusy(true); setError("");
    try {
      await call(`/topics/${topic.maChuDe}/words/${form.maTuVung}`, { method: "DELETE" });
      close(true); await afterSaved("Đã gỡ từ khỏi chủ đề.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  async function audioAction(kind, file) {
    if (busy) return;
    if (file && (!/\.mp3$/i.test(file.name) || file.size > 10_000_000)) { setError("Chỉ nhận MP3 tối đa 10 MB."); return; }
    if (!file && !window.confirm("Xóa âm thanh này?")) return;
    setBusy(true); setError(""); setNotice("");
    try {
      if (file) {
        const data = new FormData(); data.set("file", file);
        await call(`/words/${form.maTuVung}/audio/${kind}`, { method: "POST", body: data });
      } else await call(`/words/${form.maTuVung}/audio/${kind}`, { method: "DELETE" });
      setForm(current => ({ ...current, [kind === "word" ? "hasAudio" : "hasExampleAudio"]: !!file }));
      setAudioVersion(value => value + 1);
      await afterSaved(file ? "Đã lưu âm thanh." : "Đã xóa âm thanh.");
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }
  function sectionToggle(key, event) {
    const isOpen = event.currentTarget.open;
    setSections(current => ({ ...current, [key]: isOpen }));
  }
  function openTopic(item) { setTopic(item); setWordSearch(""); setFilter(""); setError(""); }

  return <div className="admin-page av-page">
    <header className="av-header"><div>
      {topic && <button className="av-back" type="button" onClick={() => { if (dirty && !window.confirm("Bạn có thay đổi chưa lưu. Quay lại danh sách chủ đề?")) return; close(true); setTopic(null); }}>← Danh sách chủ đề</button>}
      <h1>{topic ? "Từ vựng theo chủ đề" : "Quản lý từ vựng"}</h1>
      <p>{topic ? "Chọn từ để xem thông tin và quản lý âm thanh." : "Chọn một chủ đề để quản lý từ vựng."}</p>
    </div><button className="av-primary" type="button" onClick={() => topic ? editWord() : editTopic()}>+ Thêm {topic ? "từ vựng" : "chủ đề"}</button></header>

    <AdminSectionStats endpoint={topic ? `vocabulary/topics/${topic.maChuDe}` : "vocabulary"} title={topic ? "Thống kê chủ đề" : "Thống kê từ vựng"} revision={statsRevision} />
    {error && <div className="av-alert" role="alert"><span>{error}</span><button type="button" onClick={retryLoad}>Thử tải lại</button><button type="button" onClick={() => setError("")}>Đóng</button></div>}
    {notice && <div className="av-success" role="status"><span>{notice}</span><button type="button" onClick={() => setNotice("")}>Đóng</button></div>}

    {topic && <section className="av-topic-header"><div className="av-topic-image">{topic.duongDanAnhBia ? <img src={coverUrl(topic.maChuDe)} alt="" /> : <span>STO123</span>}</div><div className="av-topic-copy"><h2>{topic.tenChuDe}</h2><p>{topic.moTa || "Chưa có mô tả."}</p><span className={topic.trangThai === "DANG_MO" ? "av-badge" : "av-badge closed"}>{topic.trangThai === "DANG_MO" ? "Đang mở" : "Đã đóng"}</span></div><button className="av-secondary" type="button" onClick={() => editTopic(topic)}>Sửa chủ đề</button></section>}

    <div className="av-tools"><input type="search" aria-label="Tìm kiếm" placeholder={topic ? "Tìm từ hoặc nghĩa..." : "Tìm chủ đề..."} value={topic ? wordSearch : topicSearch} onChange={event => topic ? setWordSearch(event.target.value) : (setTopicSearch(event.target.value), setPage(1))} />
      {topic && <select aria-label="Lọc từ vựng" value={filter} onChange={event => setFilter(event.target.value)}><option value="">Tất cả từ</option><option value="audio">Thiếu âm thanh</option><option value="info">Thiếu thông tin</option></select>}
    </div>
    {loading && <p className="av-loading" role="status">Đang tải dữ liệu...</p>}
    {!topic ? <><div className="av-grid">{topics.map(item => <article className="av-card" key={item.maChuDe}>
      <button className="av-card-open" type="button" onClick={() => openTopic(item)} aria-label={`Mở chủ đề ${item.tenChuDe}`}><div className="av-cover">{item.duongDanAnhBia ? <img src={coverUrl(item.maChuDe)} alt="" /> : <span>STO123</span>}</div><div className="av-card-body"><h2>{item.tenChuDe}</h2><p>{item.wordCount} từ vựng</p><span className={item.trangThai === "DANG_MO" ? "av-badge" : "av-badge closed"}>{item.trangThai === "DANG_MO" ? "Đang mở" : "Đã đóng"}</span></div></button>
      <div className="av-card-action"><button className="av-secondary" type="button" onClick={() => editTopic(item)}>Sửa</button></div>
    </article>)}</div>{!loading && !topics.length && <p className="av-empty">Chưa có chủ đề phù hợp. Nhấn Thêm chủ đề để bắt đầu.</p>}
      {!!topics.length && <div className="av-pager"><button type="button" disabled={page === 1 || loading} onClick={() => setPage(page - 1)}>Trước</button><span>Trang {page} / {Math.max(1, Math.ceil(total / 12))}</span><button type="button" disabled={page * 12 >= total || loading} onClick={() => setPage(page + 1)}>Sau</button></div>}
    </> : <div className="av-list">{words.map(item => <button className="av-row" type="button" key={item.maTuVung} onClick={() => editWord(item)}><span className="av-word"><strong>{item.word}</strong><span>{item.nghia}</span></span><span className="av-row-status">{!item.hasAudio && <em>Thiếu âm thanh từ</em>}{!item.hasExampleAudio && <em>Thiếu âm thanh ví dụ</em>}{item.hasAudio && item.hasExampleAudio && <span className="av-ok">Đủ âm thanh</span>}{missingInfo(item) && <span className="av-info-missing">Thiếu thông tin</span>}</span><span className="av-row-arrow">›</span></button>)}{!loading && !words.length && <p className="av-empty">Chưa có từ phù hợp với tìm kiếm hoặc bộ lọc.</p>}</div>}

    {drawer && <div className="av-overlay" onMouseDown={event => { if (event.target === event.currentTarget) close(); }}><aside className="av-drawer" role="dialog" aria-modal="true" aria-label={drawer.type === "topic" ? "Chỉnh sửa chủ đề" : "Chi tiết từ vựng"}>
      <div className="av-drawer-head"><div><small>{drawer.type === "topic" ? "CHỦ ĐỀ" : "TỪ VỰNG"}</small><h2>{drawer.type === "topic" ? drawer.item ? "Sửa chủ đề" : "Thêm chủ đề" : drawer.item ? form.word : "Thêm từ vựng"}</h2></div><button type="button" onClick={() => close()} aria-label="Đóng">×</button></div>
      <div className="av-drawer-body">{drawer.type === "topic" ? <>
        <label>Tên chủ đề<input value={form.tenChuDe} maxLength={128} onChange={event => change("tenChuDe", event.target.value)} /></label>
        <label>Mô tả<textarea rows={3} value={form.moTa || ""} maxLength={256} onChange={event => change("moTa", event.target.value)} /></label>
        <div className="av-field"><strong>Ảnh bìa</strong><div className="av-image-preview">{previewUrl ? <img src={previewUrl} alt="Ảnh bìa mới" /> : !removeCover && form.duongDanAnhBia ? <img src={coverUrl(form.maChuDe)} alt="Ảnh bìa hiện tại" /> : <span>Chưa có ảnh bìa</span>}</div><label className="av-file-button">{form.duongDanAnhBia ? "Thay ảnh JPG" : "Chọn ảnh JPG"}<input type="file" accept="image/jpeg,.jpg" onChange={event => { setImage(event.target.files?.[0] || null); setRemoveCover(false); setDirty(true); }} /></label>{(form.duongDanAnhBia || image) && <button className="av-text-button" type="button" onClick={() => { setImage(null); setRemoveCover(true); setDirty(true); }}>Xóa ảnh bìa</button>}<small>Ảnh JPG tối đa 5 MB. Xem trước trước khi lưu.</small></div>
        <label>Hiển thị<select value={form.trangThai} onChange={event => change("trangThai", event.target.value)}><option value="DANG_MO">Đang mở</option><option value="DA_DONG">Đã đóng</option></select></label>
        {drawer.item && <div className="av-danger-zone"><button type="button" disabled={busy} onClick={deleteTopic}>Xóa chủ đề</button><small>Chỉ xóa được chủ đề chưa có từ, lịch sử học hoặc tham chiếu khóa học.</small></div>}
      </> : <>
        {!drawer.item && <section className="av-existing"><h3>Thêm từ đã có</h3><input type="search" placeholder="Tìm từ trong kho..." value={existingQuery} onChange={event => setExistingQuery(event.target.value)} />{existingWords.map(word => <button type="button" disabled={busy} key={word.maTuVung} onClick={() => linkExisting(word)}>{word.word} — {word.nghia}</button>)}<p>Hoặc nhập từ mới bên dưới.</p></section>}
        {drawer.item && <details className="av-section" open={sections.audio} onToggle={event => sectionToggle("audio", event)}><summary>Âm thanh <span>{form.hasAudio && form.hasExampleAudio ? "Đã đủ" : "Cần bổ sung"}</span></summary>{[["word", "Âm thanh từ", form.hasAudio], ["example", "Âm thanh ví dụ", form.hasExampleAudio]].map(([kind, label, exists]) => <div className="av-audio" key={kind}><div><strong>{label}</strong><span className={exists ? "av-ok" : "av-missing"}>{exists ? "Đã có MP3" : "Chưa có MP3"}</span></div>{exists && <AuthorizedAudio token={token} url={`${API_BASE_URL}${root}/words/${form.maTuVung}/audio/${kind}`} version={audioVersion} />}<div className="av-audio-actions"><label className="av-file-button">{exists ? "Thay MP3" : "Thêm MP3"}<input type="file" accept="audio/mpeg,.mp3" disabled={busy} onChange={event => { const file = event.target.files?.[0]; if (file) audioAction(kind, file); event.target.value = ""; }} /></label>{exists && <button className="av-text-button" type="button" disabled={busy} onClick={() => audioAction(kind, null)}>Xóa</button>}</div></div>)}</details>}
        <details className="av-section" open={sections.info} onToggle={event => sectionToggle("info", event)}><summary>Thông tin từ vựng <span>{missingInfo(form) ? "Thiếu thông tin" : "Đầy đủ"}</span></summary><div className="av-section-body">{informationFields.map(([key, label]) => <label key={key}>{label}{key === "loaiTu" ? <select value={form.loaiTu || ""} onChange={event => change(key, event.target.value)}><option value="">Chọn loại từ</option>{form.loaiTu && !partsOfSpeech.some(([value]) => value === form.loaiTu) && <option value={form.loaiTu}>{form.loaiTu} (dữ liệu hiện có)</option>}{partsOfSpeech.map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select> : <input value={form[key] || ""} maxLength={key === "nghia" ? 256 : key === "viDu" || key === "dichViDu" ? 512 : 128} onChange={event => change(key, event.target.value)} />}</label>)}</div></details>
        <details className="av-section" open={sections.topics} onToggle={event => sectionToggle("topics", event)}><summary>Chủ đề liên kết <span>{form.topicIds.length} chủ đề</span></summary><div className="av-section-body">{topicOptions.map(option => <label className="av-check" key={option.maChuDe}><input type="checkbox" checked={form.topicIds.includes(option.maChuDe)} onChange={event => change("topicIds", event.target.checked ? [...form.topicIds, option.maChuDe] : form.topicIds.filter(id => id !== option.maChuDe))} />{option.tenChuDe}</label>)}<div className="av-inline-create"><input placeholder="Tên chủ đề mới" value={newTopicName} maxLength={128} onChange={event => setNewTopicName(event.target.value)} /><button type="button" disabled={busy || !newTopicName.trim()} onClick={createLinkedTopic}>Thêm</button></div>{drawer.item && <button className="av-text-button" type="button" disabled={busy} onClick={unlink}>Gỡ khỏi chủ đề đang xem</button>}</div></details>
      </>}</div>
      <footer className="av-actions"><button type="button" onClick={() => close()}>Hủy</button><button className="av-primary" type="button" disabled={busy} onClick={drawer.type === "topic" ? saveTopic : saveWord}>{busy ? "Đang lưu..." : "Lưu"}</button></footer>
    </aside></div>}
  </div>;
}

function AuthorizedAudio({ token, url, version }) {
  const [src, setSrc] = useState("");
  const [error, setError] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    let objectUrl;
    setSrc(""); setError(false);
    fetch(url, { headers: { Authorization: `Bearer ${token}` } })
      .then(response => { if (!response.ok) throw new Error("Không tải được âm thanh."); return response.blob(); })
      .then(blob => { objectUrl = URL.createObjectURL(blob); if (active) setSrc(objectUrl); else URL.revokeObjectURL(objectUrl); })
      .catch(() => { if (active) setError(true); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [url, token, version, retry]);
  if (error) return <div className="av-audio-error">Không tải được âm thanh. <button type="button" onClick={() => setRetry(value => value + 1)}>Thử lại</button></div>;
  return src ? <audio controls preload="none" src={src} /> : <span>Đang tải âm thanh...</span>;
}
