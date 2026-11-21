import { useEffect, useMemo, useRef, useState } from "react";
import { apiRequest, API_BASE_URL } from "../../services/api";
import DocumentRenderer from "../exam/DocumentRenderer";
import { clearPartSevenDraft, clearPartSevenGroupDraft, loadPartSevenDraft,
  loadPartSevenGroupDraft, savePartSevenDraft, savePartSevenGroupDraft } from "./partSevenDraft";
import { alignStudioTranslation, importStudioDocument, makeBlock, moveItem, newId, reorderItem,
  newStudioDocument, parseStudioTranslation, serializeStudioDocument, serializeStudioTranslation,
  studioIssues, studioTemplates } from "./partSevenStudioData";
import { serializePart7Translation } from "./partSevenTranslationData";
import "../../styles/part-seven-studio.css";

const blankQuestion = () => ({ text: "", a: "", b: "", c: "", d: "", correct: "A", difficulty: 2, explanation: "" });
const imageTypes = ".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp";

function StudioImage({ endpoint, token, bank, alt }) {
  const [url, setUrl] = useState("");
  useEffect(() => {
    if (!endpoint) return undefined;
    if (endpoint.startsWith("blob:")) return undefined;
    const controller = new AbortController();
    let objectUrl = "";
    fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ path: endpoint, bank })}`,
      { headers: { Authorization: `Bearer ${token}` }, signal: controller.signal })
      .then(response => { if (!response.ok) throw new Error("Ảnh không tồn tại"); return response.blob(); })
      .then(blob => { if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } })
      .catch(() => { if (!controller.signal.aborted) setUrl("error"); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token, bank]);
  if (!endpoint) return null;
  if (url === "error") return <span role="alert">Không tải được ảnh từ Blob.</span>;
  return <img className="exam-image" src={endpoint.startsWith("blob:") ? endpoint : url || undefined} alt={alt} />;
}

function InlineText({ value, onChange, placeholder, multiline = false, label }) {
  return multiline ? <textarea aria-label={label || placeholder} placeholder={placeholder} value={value || ""}
    onChange={event => onChange(event.target.value)} rows={Math.max(2, Math.min(8, (value || "").split("\n").length + 1))} /> :
    <input aria-label={label || placeholder} placeholder={placeholder} value={value || ""}
      onChange={event => onChange(event.target.value)} />;
}

export default function PartSevenStudio({ token, bank = "DE_THI", examId = null, unit = null,
  inline = false, initialCount = null, draftScope = null, onClose, onSaved, onPartChange }) {
  const draftId = draftScope || examId;
  const source = useMemo(() => unit ? JSON.stringify({ resource: unit.resource, documents: unit.documents,
    questions: unit.questions }) : "", [unit]);
  const initial = useMemo(() => {
    const draft = unit ? loadPartSevenGroupDraft(token, bank, unit.groupId, source)?.value :
      loadPartSevenDraft(token, bank, draftId);
    const raw = draft?.studio || draft;
    const documents = raw?.documents?.length ? raw.documents.map(importStudioDocument) :
      unit?.documents?.length ? unit.documents.map(importStudioDocument) : [newStudioDocument()];
    let oldTranslation = null;
    let migrationError = "";
    try {
      oldTranslation = raw?.part7Translation ?
        serializePart7Translation(raw.part7Translation, raw.documents) : null;
    } catch { migrationError = "Bản dịch trong bản nháp cũ không hợp lệ."; }
    let translated;
    try { translated = raw?.translation || parseStudioTranslation(oldTranslation ||
      unit?.resource?.translation, documents); }
    catch { migrationError = "Bản dịch hiện tại không đọc được.";
      translated = { legacyText: "", documents: documents.map(doc => alignStudioTranslation(doc)) }; }
    return { documents, questions: raw?.questions?.length ? raw.questions :
      unit?.questions?.length ? unit.questions : Array.from({ length: Math.max(2,
        Math.min(5, initialCount || 2)) }, blankQuestion),
      text: raw?.text || unit?.resource?.text || "",
      translation: translated, migrationError };
  }, [token, bank, draftId, unit, source, initialCount]);
  const [state, setState] = useState(initial);
  const [tab, setTab] = useState("documents");
  const [selectedDoc, setSelectedDoc] = useState(0);
  const [selectedQuestion, setSelectedQuestion] = useState(0);
  const [language, setLanguage] = useState("en");
  const [selectedBlock, setSelectedBlock] = useState(0);
  const [templateSearch, setTemplateSearch] = useState("");
  const [showTemplates, setShowTemplates] = useState(false);
  const [templateMode, setTemplateMode] = useState("replace");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [previews, setPreviews] = useState({});
  const history = useRef([initial]);
  const historyIndex = useRef(0);
  const objectUrls = useRef([]);
  const [, setRevision] = useState(0);
  const dirty = JSON.stringify(state) !== JSON.stringify(initial);
  const issues = studioIssues(state.documents, state.questions);
  const doc = state.documents[selectedDoc];
  const translation = state.translation.documents[selectedDoc];

  useEffect(() => () => objectUrls.current.forEach(URL.revokeObjectURL), []);
  useEffect(() => {
    if (!dirty) return undefined;
    const timer = window.setTimeout(() => {
      if (unit) savePartSevenGroupDraft(token, bank, unit.groupId, source,
        { studio: state, documents: state.documents }, state.translation);
      else savePartSevenDraft(token, bank, draftId, { studio: state,
        documents: state.documents, questions: state.questions });
    }, 600);
    return () => window.clearTimeout(timer);
  }, [dirty, token, bank, draftId, unit, source, state]);

  function change(next) {
    setState(next);
    history.current = history.current.slice(0, historyIndex.current + 1);
    history.current.push(next);
    if (history.current.length > 80) history.current.shift();
    historyIndex.current = history.current.length - 1;
    setRevision(value => value + 1);
  }
  function undo(direction) {
    const index = historyIndex.current + direction;
    if (index < 0 || index >= history.current.length) return;
    historyIndex.current = index; setState(history.current[index]); setRevision(value => value + 1);
  }
  useEffect(() => {
    const listener = event => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
        event.preventDefault(); undo(event.shiftKey ? 1 : -1);
      } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") {
        event.preventDefault(); undo(1);
      }
    };
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);
  useEffect(() => {
    if (!dirty) return undefined;
    const listener = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", listener);
    return () => window.removeEventListener("beforeunload", listener);
  }, [dirty]);

  function updateDoc(index, nextDoc) {
    const documents = state.documents.map((item, at) => at === index ? nextDoc : item);
    const translations = documents.map((item, at) => alignStudioTranslation(item,
      state.translation.documents[at]));
    change({ ...state, documents, translation: { ...state.translation, documents: translations } });
  }
  function updateBlock(index, nextBlock) {
    updateDoc(selectedDoc, { ...doc, blocks: doc.blocks.map((block, at) => at === index ? nextBlock : block) });
  }
  function changeDocumentOrder(index, direction) {
    change({ ...state, documents: moveItem(state.documents, index, direction),
      translation: { ...state.translation,
        documents: moveItem(state.translation.documents, index, direction) } });
    setSelectedDoc(index + direction);
  }
  function removeDocument(index) {
    if (state.documents.length <= 1 || !window.confirm("Xóa tài liệu này khỏi nhóm?")) return;
    change({ ...state, documents: state.documents.filter((_, at) => at !== index),
      translation: { ...state.translation,
        documents: state.translation.documents.filter((_, at) => at !== index) } });
    setSelectedDoc(Math.max(0, Math.min(selectedDoc, state.documents.length - 2)));
  }
  function addDocument(type) {
    if (state.documents.length >= 3) return;
    const next = newStudioDocument(type);
    change({ ...state, documents: [...state.documents, next], translation: { ...state.translation,
      documents: [...state.translation.documents, alignStudioTranslation(next)] } });
    setSelectedDoc(state.documents.length); setSelectedBlock(0); setShowTemplates(false);
  }
  function duplicateDocument(index) {
    if (state.documents.length >= 3) return;
    const original = state.documents[index];
    const ids = new Map(original.blocks.map(block => [block.id, newId()]));
    const copy = { ...original, id: newId(), blocks: original.blocks.map(block =>
      ({ ...block, id: ids.get(block.id) })) };
    const originalTranslation = state.translation.documents[index];
    const translated = { ...originalTranslation, blocks: originalTranslation.blocks.map(block =>
      ({ ...block, id: ids.get(block.id) })) };
    const documents = [...state.documents]; documents.splice(index + 1, 0, copy);
    const translations = [...state.translation.documents]; translations.splice(index + 1, 0, translated);
    change({ ...state, documents, translation: { ...state.translation, documents: translations } });
    setSelectedDoc(index + 1); setSelectedBlock(0);
  }
  function changeTemplate(type) {
    if (doc.blocks.some(block => block.text || block.value || block.path || block.rows?.some(row => row.some(Boolean))) &&
      !window.confirm("Đổi mẫu sẽ xóa nội dung của tài liệu đang chọn. Tiếp tục?")) return;
    updateDoc(selectedDoc, newStudioDocument(type)); setSelectedBlock(0); setShowTemplates(false);
  }
  function addBlock(kind) {
    const block = makeBlock(kind);
    const blocks = [...doc.blocks]; blocks.splice(selectedBlock + 1, 0, block);
    updateDoc(selectedDoc, { ...doc, blocks }); setSelectedBlock(selectedBlock + 1);
  }
  function moveBlock(index, direction) {
    updateDoc(selectedDoc, { ...doc, blocks: moveItem(doc.blocks, index, direction) });
    setSelectedBlock(index + direction);
  }
  function removeBlock(index) {
    if (doc.blocks.length <= 1) return;
    updateDoc(selectedDoc, { ...doc, blocks: doc.blocks.filter((_, at) => at !== index) });
    setSelectedBlock(Math.max(0, index - 1));
  }
  function duplicateBlock(index) {
    if (doc.blocks.length >= 100) return;
    const id = newId();
    const blocks = [...doc.blocks]; blocks.splice(index + 1, 0, { ...doc.blocks[index], id });
    const translated = [...translation.blocks];
    translated.splice(index + 1, 0, { ...translated[index], id });
    change({ ...state, documents: state.documents.map((item, at) => at === selectedDoc ?
      { ...item, blocks } : item), translation: { ...state.translation,
      documents: state.translation.documents.map((item, at) => at === selectedDoc ?
        { ...item, blocks: translated } : item) } });
    setSelectedBlock(index + 1);
  }
  function updateTranslation(index, nextBlock) {
    const documents = state.translation.documents.map((item, at) => at === selectedDoc ?
      { ...item, blocks: item.blocks.map((block, position) => position === index ? nextBlock : block) } : item);
    change({ ...state, translation: { ...state.translation, documents } });
  }
  function reorderTable(index, axis, from, to) {
    const original = doc.blocks[index];
    const translated = translation.blocks[index];
    const transform = block => axis === "row" ? { ...block, rows: reorderItem(block.rows, from, to) } :
      { ...block, headers: reorderItem(block.headers, from, to),
        rows: block.rows.map(row => reorderItem(row, from, to)) };
    const documents = state.documents.map((item, at) => at === selectedDoc ? { ...item,
      blocks: item.blocks.map((block, position) => position === index ? transform(original) : block) } : item);
    const translations = state.translation.documents.map((item, at) => at === selectedDoc ? { ...item,
      blocks: item.blocks.map((block, position) => position === index ? transform(translated) : block) } : item);
    change({ ...state, documents, translation: { ...state.translation, documents: translations } });
  }
  async function uploadImage(file, index) {
    if (!file) return;
    setBusy(true); setError("");
    try {
      const form = new FormData(); form.append("file", file);
      const saved = await apiRequest(`/api/admin/content/media/upload?bank=${bank}`, {
        token, method: "POST", body: form, timeoutMs: 120_000 });
      updateBlock(index, { ...doc.blocks[index], path: saved.path });
      const url = URL.createObjectURL(file); objectUrls.current.push(url);
      setPreviews(old => ({ ...old, [saved.path]: url }));
    } catch (failure) { setError(failure.message); }
    finally { setBusy(false); }
  }
  function updateQuestion(index, key, value) {
    change({ ...state, questions: state.questions.map((item, at) => at === index ?
      { ...item, [key]: value } : item) });
  }
  function makePayload() {
    return { part: 7, bank, text: state.text || null,
      translation: serializeStudioTranslation(state.translation, state.documents),
      audioPath: null, imagePath: null, documents: state.documents.map(serializeStudioDocument),
      questions: state.questions.map(question => ({ id: unit ? question.id || null : null,
        text: question.text, a: question.a, b: question.b,
        c: question.c, d: question.d, correct: question.correct, difficulty: Number(question.difficulty),
        explanation: question.explanation || null })) };
  }
  async function save() {
    if (state.migrationError) { setError("Bản dịch cũ cần được xử lý trước khi lưu."); return; }
    if (issues.length) { setError("Hoàn thiện các mục được liệt kê trước khi lưu."); setTab("preview"); return; }
    setBusy(true); setError("");
    try {
      const payload = makePayload();
      const result = await apiRequest(unit ? `/api/admin/content/groups/${unit.groupId}/studio` :
        examId ? `/api/admin/content/question-authoring/exam/${examId}` :
          "/api/admin/content/question-authoring", {
        token, method: unit ? "PUT" : "POST", body: payload, timeoutMs: 120_000 });
      if (unit) clearPartSevenGroupDraft(token, bank, unit.groupId);
      else clearPartSevenDraft(token, bank, draftId);
      onSaved(result);
    } catch (failure) { setError(failure.message); }
    finally { setBusy(false); }
  }
  async function duplicate() {
    if (state.migrationError) { setError("Bản dịch cũ cần được xử lý trước khi nhân bản."); return; }
    if (issues.length) { setError("Hoàn thiện nhóm trước khi nhân bản."); setTab("preview"); return; }
    setBusy(true); setError("");
    try {
      const result = await apiRequest("/api/admin/content/question-authoring", {
        token, method: "POST", body: makePayload(), timeoutMs: 120_000 });
      setMessage(`Đã tạo nhóm mới #${result.groupId}.`);
    } catch (failure) { setError(failure.message); } finally { setBusy(false); }
  }
  function close() {
    if (busy) return;
    if (dirty) {
      const saved = unit ? savePartSevenGroupDraft(token, bank, unit.groupId, source,
        { studio: state, documents: state.documents }, state.translation) :
        savePartSevenDraft(token, bank, draftId, { studio: state, documents: state.documents,
          questions: state.questions });
      if (!saved && !window.confirm("Không lưu được bản nháp. Đóng và bỏ thay đổi?")) return;
    }
    onClose();
  }
  function switchPart(nextPart) {
    if (nextPart === 7 || !onPartChange) return;
    if (dirty && !savePartSevenDraft(token, bank, draftId, { studio: state,
      documents: state.documents, questions: state.questions }) &&
      !window.confirm("Không lưu được bản nháp Part 7. Chuyển Part và bỏ thay đổi?")) return;
    onPartChange(nextPart);
  }

  const categories = [...new Set(studioTemplates.map(item => item.category))];
  const filtered = studioTemplates.filter(item => `${item.label} ${item.type}`.toLowerCase()
    .includes(templateSearch.toLowerCase()));
  const activeQuestion = state.questions[selectedQuestion];
  return <section className={`part7-studio ${inline ? "is-inline" : ""}`} aria-label="Part 7 Visual Document Studio">
    <header className="part7-studio-header"><div><small>STO123 · TOEIC PART 7</small>
      <h2>{unit ? `Chỉnh sửa nhóm #${unit.groupId}` : "Soạn nhóm câu hỏi"}</h2>
      <p>{state.documents.length} tài liệu · {state.questions.length} câu hỏi · {issues.length ?
        `${issues.length} mục cần hoàn thiện` : dirty ? "Có thay đổi chưa xuất bản" : "Sẵn sàng"}</p></div>
      <div className="part7-studio-actions"><button type="button" onClick={() => undo(-1)}>↶ Hoàn tác</button>
        <button type="button" onClick={() => undo(1)}>↷ Làm lại</button>
        <button type="button" onClick={close}>Đóng</button><button type="button" className="part7-primary" disabled={busy} onClick={save}>{busy ? "Đang lưu…" : "Lưu nhóm"}</button></div>
    </header>
    {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {state.migrationError && <p role="alert" className="exam-alert exam-alert-error">
      {state.migrationError} Bản gốc vẫn được giữ nguyên.
      <button type="button" onClick={() => { if (window.confirm("Bỏ bản dịch cũ không đọc được và tạo bản dịch mới?"))
        change({ ...state, migrationError: "", translation: { legacyText: "",
          documents: state.documents.map(doc => alignStudioTranslation(doc)) } }); }}>Tạo lại bản dịch</button></p>}
    {message && <p role="status" className="exam-alert exam-alert-success">{message}</p>}
    <nav className="part7-studio-tabs" aria-label="Các bước soạn đề">
      {[ ["documents", "01 Tài liệu"], ["questions", "02 Câu hỏi"], ["preview", "03 Xem như học viên"] ].map(([key, label]) =>
        <button key={key} type="button" aria-current={tab === key ? "step" : undefined} className={tab === key ? "active" : ""}
          onClick={() => setTab(key)}>{label}</button>)}
    </nav>
    <div className="part7-studio-body"><aside className="part7-studio-sidebar">
      <strong>Cấu trúc nhóm</strong><small>Chọn mục để chỉnh sửa</small>
      {state.documents.map((item, index) => <div className="part7-sidebar-row" key={item.id}>
        <button type="button" className={tab === "documents" && selectedDoc === index ? "active" : ""}
          onClick={() => { setSelectedDoc(index); setSelectedBlock(0); setTab("documents"); }}>
          <span>Tài liệu {index + 1}</span><small>{studioTemplates.find(template => template.type === item.type)?.label}</small></button>
        <div><button type="button" aria-label={`Đưa tài liệu ${index + 1} lên`} disabled={index === 0}
          onClick={() => changeDocumentOrder(index, -1)}>↑</button>
          <button type="button" aria-label={`Đưa tài liệu ${index + 1} xuống`} disabled={index === state.documents.length - 1}
            onClick={() => changeDocumentOrder(index, 1)}>↓</button>
          <button type="button" aria-label={`Xóa tài liệu ${index + 1}`} disabled={state.documents.length <= 1}
            onClick={() => removeDocument(index)}>×</button>
          <button type="button" aria-label={`Nhân bản tài liệu ${index + 1}`} disabled={state.documents.length >= 3}
            onClick={() => duplicateDocument(index)}>⧉</button></div></div>)}
      <button type="button" disabled={state.documents.length >= 3} onClick={() => {
        setTab("documents"); setTemplateMode("add"); setShowTemplates(true); }}>＋ Thêm tài liệu</button>
      <hr />{state.questions.map((question, index) => <button type="button" key={index}
        className={tab === "questions" && selectedQuestion === index ? "active" : ""}
        onClick={() => { setSelectedQuestion(index); setTab("questions"); }}>
        Câu {index + 1} <small>{question.text?.slice(0, 32) || "Chưa có nội dung"}</small></button>)}
      <button type="button" disabled={state.questions.length >= 5 || !!unit?.exams?.length}
        onClick={() => { change({ ...state, questions: [...state.questions, blankQuestion()] });
          setSelectedQuestion(state.questions.length); setTab("questions"); }}>＋ Thêm câu hỏi</button>
      {!!unit?.exams?.length && <small>Nhóm đang thuộc đề: giữ nguyên số câu để bảo toàn cấu trúc đề.</small>}
      <hr /><button type="button" onClick={() => { setMessage("Bản nháp được tự lưu trên trình duyệt này."); }}>Bản nháp tự lưu</button>
      {unit && <button type="button" disabled={busy} onClick={duplicate}>Nhân bản nhóm</button>}
    </aside>
    <main className="part7-studio-main">
      {tab === "documents" && doc && <>
        <div className="part7-editor-heading"><div><small>TÀI LIỆU {selectedDoc + 1} / {state.documents.length}</small><h3>{studioTemplates.find(item => item.type === doc.type)?.label}</h3></div>
          <button type="button" onClick={() => { setTemplateMode("replace"); setShowTemplates(value => !value); }}>Đổi mẫu ▾</button></div>
        {showTemplates && <div className="part7-template-picker"><input autoFocus placeholder="Tìm trong 25 mẫu…" value={templateSearch}
          onChange={event => setTemplateSearch(event.target.value)} aria-label="Tìm mẫu tài liệu" />
          {categories.map(category => <div key={category}><strong>{category}</strong><div className="part7-template-grid">
            {filtered.filter(item => item.category === category).map(item => <button type="button" key={item.type}
              onClick={() => templateMode === "add" ? addDocument(item.type) : changeTemplate(item.type)}>
              {item.label}</button>)}</div></div>)}
          <button type="button" onClick={() => setShowTemplates(false)}>Đóng danh sách mẫu</button></div>}
        <div className="part7-language-toggle"><span>Ngôn ngữ soạn</span>
          <button type="button" className={language === "en" ? "active" : ""} onClick={() => setLanguage("en")}>English</button>
          <button type="button" className={language === "vi" ? "active" : ""} onClick={() => setLanguage("vi")}>Tiếng Việt</button></div>
        <div className={`part7-paper part7-paper-${doc.type.toLowerCase()}`}>
          {doc.blocks.map((block, index) => {
            const translated = translation?.blocks[index] || {};
            const shown = language === "vi" ? translated : block;
            const edit = next => language === "vi" ? updateTranslation(index, { ...translated, ...next }) :
              updateBlock(index, { ...block, ...next });
            return <div key={block.id} className={`part7-inline-block ${selectedBlock === index ? "selected" : ""}`}
              onClick={() => setSelectedBlock(index)} onDragOver={event => event.preventDefault()}
              onDrop={event => { event.preventDefault(); const from = Number(event.dataTransfer.getData("text/plain"));
                if (Number.isInteger(from) && from >= 0 && from < doc.blocks.length && from !== index) {
                  updateDoc(selectedDoc, { ...doc, blocks: reorderItem(doc.blocks, from, index) });
                  setSelectedBlock(index);
                } }}>
              <div className="part7-block-handle" draggable onDragStart={event => event.dataTransfer.setData("text/plain", String(index))}
                aria-label={`Kéo khối ${index + 1}`}>⠿</div>
              <div className="part7-block-content">
                {["HEADING", "PARAGRAPH", "BULLET", "NUMBERED"].includes(block.kind) &&
                  <InlineText value={shown.text} multiline={block.kind !== "HEADING"} onChange={text => edit({ text })}
                    placeholder={block.kind === "HEADING" ? "Nhập tiêu đề…" : "Nhập nội dung trực tiếp tại đây…"}
                    label={`Nội dung khối ${index + 1}`} />}
                {block.kind === "META" && <div className="part7-meta-edit"><InlineText value={shown.label} onChange={label => edit({ label })}
                  placeholder="Nhãn" /><InlineText value={shown.value} onChange={value => edit({ value })} placeholder="Giá trị" /></div>}
                {block.kind === "TABLE" && <div className="part7-table-wrap"><table><thead><tr>{block.headers.map((header, col) =>
                  <th key={col}><InlineText value={language === "vi" ? shown.headers?.[col] : header}
                    onChange={value => { const headers = [...shown.headers]; headers[col] = value; edit({ headers }); }}
                    placeholder={`Cột ${col + 1}`} />{language === "en" && <span className="part7-cell-actions">
                      <button type="button" aria-label={`Đưa cột ${col + 1} sang trái`} disabled={col === 0}
                        onClick={() => reorderTable(index, "column", col, col - 1)}>←</button>
                      <button type="button" aria-label={`Đưa cột ${col + 1} sang phải`} disabled={col === block.headers.length - 1}
                        onClick={() => reorderTable(index, "column", col, col + 1)}>→</button></span>}</th>)}</tr></thead><tbody>{block.rows.map((row, rowIndex) =>
                    <tr key={rowIndex}>{row.map((cell, col) => <td key={col}><InlineText
                      value={language === "vi" ? shown.rows?.[rowIndex]?.[col] : cell}
                      onChange={value => { const rows = shown.rows.map(item => [...item]); rows[rowIndex][col] = value; edit({ rows }); }}
                      placeholder="Nội dung ô" />{col === 0 && language === "en" && <span className="part7-cell-actions">
                        <button type="button" aria-label={`Đưa dòng ${rowIndex + 1} lên`} disabled={rowIndex === 0}
                          onClick={() => reorderTable(index, "row", rowIndex, rowIndex - 1)}>↑</button>
                        <button type="button" aria-label={`Đưa dòng ${rowIndex + 1} xuống`} disabled={rowIndex === block.rows.length - 1}
                          onClick={() => reorderTable(index, "row", rowIndex, rowIndex + 1)}>↓</button></span>}</td>)}</tr>)}</tbody></table>
                  {language === "en" && <div className="part7-table-controls">
                    <button type="button" disabled={block.headers.length >= 12} onClick={() => updateBlock(index,
                      { ...block, headers: [...block.headers, ""], rows: block.rows.map(row => [...row, ""]) })}>＋ Cột</button>
                    <button type="button" disabled={block.headers.length <= 1} onClick={() => updateBlock(index,
                      { ...block, headers: block.headers.slice(0, -1), rows: block.rows.map(row => row.slice(0, -1)) })}>− Cột</button>
                    <button type="button" disabled={block.rows.length >= 100} onClick={() => updateBlock(index,
                      { ...block, rows: [...block.rows, block.headers.map(() => "")] })}>＋ Dòng</button>
                    <button type="button" disabled={block.rows.length <= 1} onClick={() => updateBlock(index,
                      { ...block, rows: block.rows.slice(0, -1) })}>− Dòng</button></div>}</div>}
                {block.kind === "IMAGE" && <div className="part7-image-edit">
                  {block.path && <div className="part7-image-canvas" style={{ width: `${block.width}%`,
                    marginLeft: block.align === "right" || block.align === "center" ? "auto" : 0,
                    marginRight: block.align === "left" || block.align === "center" ? "auto" : 0 }}>
                    <StudioImage endpoint={previews[block.path] || block.path} token={token} bank={bank}
                      alt={block.caption || "Ảnh tài liệu"} /></div>}
                  {language === "en" && <div><label className="part7-upload">{block.path ? "Thay ảnh" : "Tải ảnh từ máy"}<input type="file" accept={imageTypes} disabled={busy}
                    onChange={event => { uploadImage(event.target.files?.[0], index); event.target.value = ""; }} /></label>
                    {block.path && <button type="button" onClick={() => updateBlock(index, { ...block, path: "" })}>Gỡ ảnh</button>}
                    <label>Độ rộng <input type="range" min="20" max="100" value={block.width} onChange={event =>
                      updateBlock(index, { ...block, width: Number(event.target.value) })} />{block.width}%</label>
                    <select aria-label="Căn ảnh" value={block.align} onChange={event => updateBlock(index,
                      { ...block, align: event.target.value })}><option value="left">Trái</option><option value="center">Giữa</option><option value="right">Phải</option></select></div>}
                  <InlineText value={shown.caption} onChange={caption => edit({ caption })} placeholder="Chú thích ảnh…" />
                </div>}
                {block.kind === "SEPARATOR" && <hr />}
              </div>
              {language === "en" && selectedBlock === index && <div className="part7-block-actions">
                <button type="button" aria-label="Đưa khối lên" disabled={index === 0} onClick={() => moveBlock(index, -1)}>↑</button>
                <button type="button" aria-label="Đưa khối xuống" disabled={index === doc.blocks.length - 1} onClick={() => moveBlock(index, 1)}>↓</button>
                <button type="button" onClick={() => duplicateBlock(index)}>Nhân đôi</button>
                <button type="button" disabled={doc.blocks.length <= 1} onClick={() => removeBlock(index)}>Xóa</button></div>}
            </div>;
          })}
          {language === "en" && <div className="part7-add-content"><span>＋ Thêm nội dung</span>
            {[ ["HEADING", "Tiêu đề"], ["PARAGRAPH", "Đoạn văn"], ["BULLET", "Gạch đầu dòng"],
              ["NUMBERED", "Danh sách số"], ["META", "Trường thông tin"], ["TABLE", "Bảng"],
              ["IMAGE", "Hình ảnh"], ["SEPARATOR", "Đường phân cách"] ].map(([kind, label]) =>
              <button key={kind} type="button" disabled={doc.blocks.length >= 100} onClick={() => addBlock(kind)}>{label}</button>)}</div>}
        </div>
        <details className="part7-legacy-text"><summary>Ngữ liệu chung và bản dịch cũ</summary>
          <label>Ngữ liệu chung<textarea value={state.text} onChange={event => change({ ...state, text: event.target.value })} /></label>
          <label>Bản dịch cũ<textarea value={state.translation.legacyText} onChange={event => change({ ...state,
            translation: { ...state.translation, legacyText: event.target.value } })} /></label></details>
      </>}
      {tab === "questions" && activeQuestion && <div className="part7-question-editor"><small>CÂU {selectedQuestion + 1} / {state.questions.length}</small>
        <h3>Soạn câu hỏi và đáp án</h3><label>Nội dung câu hỏi tiếng Anh<textarea rows={3} value={activeQuestion.text || ""}
          onChange={event => updateQuestion(selectedQuestion, "text", event.target.value)} /></label>
        <div className="part7-answer-grid">{["a", "b", "c", "d"].map(key => <label key={key}>Đáp án {key.toUpperCase()}
          <input maxLength={512} value={activeQuestion[key] || ""} onChange={event => updateQuestion(selectedQuestion,
            key, event.target.value)} /></label>)}</div>
        <div className="part7-question-settings"><label>Đáp án đúng<select value={activeQuestion.correct || "A"}
          onChange={event => updateQuestion(selectedQuestion, "correct", event.target.value)}>
          {["A", "B", "C", "D"].map(value => <option key={value}>{value}</option>)}</select></label>
          <label>Độ khó<select value={activeQuestion.difficulty || 2} onChange={event => updateQuestion(selectedQuestion,
            "difficulty", Number(event.target.value))}><option value={1}>Dễ</option><option value={2}>Trung bình</option><option value={3}>Khó</option></select></label></div>
        <label>Giải thích<textarea rows={4} value={activeQuestion.explanation || ""}
          onChange={event => updateQuestion(selectedQuestion, "explanation", event.target.value)} /></label>
        <div className="part7-question-actions"><button type="button" disabled={selectedQuestion === 0}
          onClick={() => setSelectedQuestion(selectedQuestion - 1)}>← Câu trước</button>
          <button type="button" disabled={selectedQuestion === state.questions.length - 1}
            onClick={() => setSelectedQuestion(selectedQuestion + 1)}>Câu sau →</button>
          <button type="button" disabled={state.questions.length <= 2 || !!unit?.exams?.length}
            onClick={() => { if (!window.confirm("Xóa câu này khỏi nhóm?")) return;
              change({ ...state, questions: state.questions.filter((_, at) => at !== selectedQuestion) });
              setSelectedQuestion(Math.max(0, selectedQuestion - 1)); }}>Xóa câu</button></div></div>}
      {tab === "preview" && <div className="part7-preview"><h3>Xem như học viên</h3>
        {issues.length > 0 && <div role="status" className="exam-alert exam-alert-error"><strong>Cần hoàn thiện trước khi lưu</strong>
          <ul>{issues.map((item, index) => <li key={index}>{item}</li>)}</ul></div>}
        <div className="exam-content exam-content-grouped exam-part7-content"><div className="exam-source">
          {state.text && <p className="exam-preserve-lines">{state.text}</p>}
          {state.documents.map((item, index) => <DocumentRenderer key={item.id} token={token}
            ImageComponent={props => <StudioImage {...props} token={token} bank={bank} />}
            document={{ type: item.type, order: index + 1, content: { version: 2,
              blocks: item.blocks.map(block => block.kind === "IMAGE" ?
                { ...block, imageUrl: previews[block.path] || block.path } : block) } }} />)}
        </div><div className="exam-group-questions">{state.questions.map((question, index) =>
          <section className="exam-question" key={index}><h4>Câu {index + 1}</h4>
            <p className="exam-preserve-lines">{question.text || "Nội dung câu hỏi…"}</p>
            <div className="exam-options">{["a", "b", "c", "d"].map(key => <div key={key} className="exam-option">
              <input type="radio" disabled aria-label={`Đáp án ${key.toUpperCase()}`} />
              <span><strong>{key.toUpperCase()}.</strong> {question[key]}</span></div>)}</div></section>)}</div></div></div>}
    </main></div>
    <footer className="part7-studio-footer"><span>{issues.length ? `${issues.length} mục cần hoàn thiện` : "Đã đủ thông tin để lưu"}</span>
      {onPartChange && <label>Chọn Part <select value={7} onChange={event => switchPart(Number(event.target.value))}>
        {Array.from({ length: 7 }, (_, index) => <option key={index + 1} value={index + 1}>Part {index + 1}</option>)}
      </select></label>}
      <button type="button" onClick={() => setTab("preview")}>Xem trước</button>
      <button type="button" className="part7-primary" disabled={busy} onClick={save}>Lưu nhóm Part 7</button></footer>
  </section>;
}
