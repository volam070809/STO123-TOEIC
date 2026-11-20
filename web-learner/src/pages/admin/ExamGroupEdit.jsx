import { useEffect, useRef, useState } from "react";
import { apiRequest, API_BASE_URL } from "../../services/api";
import PartSevenDocumentForm from "./PartSevenDocumentForm";
import PartSevenTranslationForm from "./PartSevenTranslationForm";
import PartSevenLivePreview from "./PartSevenLivePreview";
import { documentIssues, documentPayload, fromDocument, moveItem, newDocument } from "./partSevenDocumentData";
import { parsePart7Translation, serializePart7Translation, updateTranslationStructure } from "./partSevenTranslationData";
import { clearPartSevenGroupDraft, loadPartSevenGroupDraft, savePartSevenGroupDraft } from "./partSevenDraft";

const part6Translation = (value, order) => value?.replace(/\r/g, "").split("\n")
  .find(line => line.trim().startsWith(`[${order}]`))?.trim().slice(3).trim() || "";
const withPart6Translation = (value, order, translation) =>
  Array.from({ length: 4 }, (_, index) => `[${index + 1}] ${index + 1 === order ? translation : part6Translation(value, index + 1)}`).join("\n");

const sourceValue = unit => ({ text: unit.resource?.text || "", translation: unit.resource?.translation || "",
  audioPath: unit.resource?.audio || "", imagePath: unit.resource?.image || "",
  documents: (unit.documents || []).map(fromDocument) });
const sourceTranslation = unit => parsePart7Translation(unit.resource?.translation,
  (unit.documents || []).map(fromDocument));

function SavedImagePreview({ path, bank, token, localUrl }) {
  const [preview, setPreview] = useState({ path: "", url: "", error: false });
  useEffect(() => {
    if (!path || localUrl) return undefined;
    const controller = new AbortController();
    let objectUrl = "";
    fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ bank, path })}`, {
      headers: { Authorization: `Bearer ${token}` }, signal: controller.signal
    }).then(response => {
      if (!response.ok) throw new Error("Không tải được ảnh từ Blob.");
      return response.blob();
    }).then(blob => {
      if (!controller.signal.aborted) {
        objectUrl = URL.createObjectURL(blob);
        setPreview({ path, url: objectUrl, error: false });
      }
    }).catch(() => {
      if (!controller.signal.aborted) setPreview({ path, url: "", error: true });
    });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [path, bank, token, localUrl]);
  if (!path || localUrl || preview.path !== path) return null;
  if (preview.error) return <p role="alert">Không tải được ảnh từ Blob.</p>;
  return preview.url && <img className="exam-media-preview" src={preview.url} alt="Ảnh hiện tại" />;
}

export default function ExamGroupEdit({ unit, token, onCancel, onSaved }) {
  const [source] = useState(() => JSON.stringify({ resource: unit.resource, documents: unit.documents }));
  const [draft] = useState(() => unit.part === 7 ?
    loadPartSevenGroupDraft(token, unit.bank || "DE_THI", unit.groupId, source) : null);
  const [value, setValue] = useState(() => draft?.value || sourceValue(unit));
  const [part7Translation, setPart7Translation] = useState(() =>
    draft?.part7Translation || sourceTranslation(unit));
  const [previews, setPreviews] = useState({});
  const objectUrls = useRef([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [draftNotice, setDraftNotice] = useState(draft ? "Đã mở lại bản nháp chỉnh sửa nhóm trên trình duyệt này." : "");
  const [initial, setInitial] = useState(() => JSON.stringify({ value: sourceValue(unit),
    part7Translation: sourceTranslation(unit) }));
  const dirty = JSON.stringify({ value, part7Translation }) !== initial;
  useEffect(() => () => objectUrls.current.forEach(URL.revokeObjectURL), []);
  useEffect(() => {
    if (unit.part !== 7 || !dirty) return;
    const timer = window.setTimeout(() => savePartSevenGroupDraft(token, unit.bank || "DE_THI",
      unit.groupId, source, value, part7Translation), 500);
    return () => window.clearTimeout(timer);
  }, [unit.part, unit.bank, unit.groupId, token, source, value, part7Translation, dirty]);
  useEffect(() => {
    if (!dirty) return undefined;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    const guardLink = event => {
      const link = event.target.closest?.("a[href]");
      if (link && !window.confirm("Ngữ liệu có thay đổi chưa lưu. Rời trang và bỏ thay đổi?")) {
        event.preventDefault(); event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", guardLink, true);
    return () => { window.removeEventListener("beforeunload", warn);
      document.removeEventListener("click", guardLink, true); };
  }, [dirty]);
  function cancel() {
    if (unit.part === 7 && dirty && savePartSevenGroupDraft(token, unit.bank || "DE_THI",
      unit.groupId, source, value, part7Translation)) { onCancel(); return; }
    if (dirty && !window.confirm("Ngữ liệu có thay đổi chưa lưu. Bỏ thay đổi và quay lại?")) return;
    onCancel();
  }
  const change = (key, next) => setValue(old => ({ ...old, [key]: next }));

  async function upload(file, setter, key) {
    if (!file) return;
    setBusy(true); setError("");
    try {
      const form = new FormData(); form.append("file", file);
      const result = await apiRequest(`/api/admin/content/media/upload?bank=${unit.bank}`, {
        method: "POST", token, body: form, timeoutMs: 120000
      });
      setter(result.path);
      const url = URL.createObjectURL(file);
      objectUrls.current.push(url);
      setPreviews(old => ({ ...old, [key]: url }));
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  function fileField(label, path, setter, accept, key = label) {
    return <div className="exam-author-file"><strong>{label}</strong><small>{path || "Chưa chọn tệp"}</small>
      <label className="exam-file-button">Tải tệp<input type="file" accept={accept} disabled={busy}
        onChange={event => { upload(event.target.files?.[0], setter, key); event.target.value = ""; }} /></label>
      {accept.includes("image") && path && <button className="exam-file-button" type="button" disabled={busy}
        onClick={() => { setter(""); setPreviews(old => ({ ...old, [key]: null })); }}>Gỡ ảnh</button>}
      {previews[key] && (accept.includes("audio") ? <audio controls src={previews[key]} /> :
        <img className="exam-media-preview" src={previews[key]} alt={label} />)}
      {accept.includes("image") && <SavedImagePreview path={path} bank={unit.bank || "DE_THI"}
        token={token} localUrl={previews[key]} />}
    </div>;
  }

  async function save(event) {
    event.preventDefault();
    if ([1, 2, 3, 4].includes(unit.part) && !value.audioPath) {
      setError(`Part ${unit.part} cần tệp audio MP3.`); return;
    }
    if (unit.part === 1 && !value.imagePath) {
      setError("Part 1 cần ảnh mô tả."); return;
    }
    if (unit.part === 6 && [1, 2, 3, 4].some(index => !value.text.includes(`[${index}]`))) {
      setError("Đoạn văn Part 6 phải có đủ [1], [2], [3], [4]."); return;
    }
    if (value.documents.some(doc => doc._parseError)) {
      setError("Có tài liệu cũ không đọc được. Chưa lưu để tránh mất dữ liệu."); return;
    }
    if (unit.part === 7) {
      const issues = value.documents.flatMap(documentIssues);
      if (issues.length) { setError(issues.join(" ")); return; }
    }
    let translated = value.translation || null;
    if (unit.part === 7) {
      try { translated = serializePart7Translation(part7Translation, value.documents); }
      catch (failure) { setError(failure.message); return; }
    }
    setBusy(true); setError("");
    try {
      const result = await apiRequest(`/api/admin/content/groups/${unit.groupId}/unit`, {
        method: "PUT", token, timeoutMs: 120000,
        body: { text: value.text || null, translation: translated,
          audioPath: value.audioPath || null, imagePath: value.imagePath || null,
          documents: unit.part === 7 ? value.documents.map(documentPayload) : [] }
      });
      setValue(old => ({ ...old, translation: translated || "" }));
      setInitial(JSON.stringify({ value: { ...value, translation: translated || "" }, part7Translation }));
      if (unit.part === 7) clearPartSevenGroupDraft(token, unit.bank || "DE_THI", unit.groupId);
      onSaved(result);
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  const label = unit.part === 3 ? "Hội thoại chung" :
    unit.part === 4 ? "Bài nói chung" : unit.part === 6 ? "Passage" : "Ngữ liệu chung";
  return <section className="exam-group-edit" aria-label={`Sửa ngữ liệu nhóm ${unit.groupId}`}>
    <h3>Sửa {label.toLowerCase()} · Nhóm {unit.groupId}</h3>
    <p className="exam-edit-impact">Thay đổi ngữ liệu dùng chung áp dụng cho mọi câu trong nhóm.
      {unit.exams?.length > 0 && <> Đề liên quan: {unit.exams.map(exam =>
        `${exam.name} (#${exam.examId}, ${exam.status})`).join("; ")}.</>}
      {" "}Khi lưu, đề hiện hành liên quan sẽ được cập nhật. Bài thi đã bắt đầu vẫn dùng nội dung lúc bắt đầu.</p>
    {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {unit.part === 7 && draftNotice && <p role="status" className="exam-alert">{draftNotice}</p>}
    <form className="exam-author-form" onSubmit={save} onInvalidCapture={event => { const section = event.target.closest("details"); if (section) section.open = true; }}>
      {unit.part >= 3 && unit.part <= 6 && <label>{label}<textarea rows={unit.part === 6 ? 7 : 4}
        value={value.text} onChange={event => change("text", event.target.value)} /></label>}
      {[1, 2, 3, 4].includes(unit.part) && fileField("Audio MP3", value.audioPath,
        path => change("audioPath", path), ".mp3,audio/mpeg", "audio")}
      {[1, 3, 4].includes(unit.part) && fileField("Hình ảnh", value.imagePath,
        path => change("imagePath", path), ".jpg,.jpeg,.png,.webp,image/*", "image")}
      {unit.part === 7 && <div className="exam-author-docs"><div className="exam-editor-header"><h3>Tài liệu Part 7</h3>
        <button type="button" disabled={value.documents.length >= 3} onClick={() =>
          change("documents", [...value.documents, newDocument()])}>Thêm tài liệu</button></div>
        {value.documents.map((doc, index) => <PartSevenDocumentForm key={index} value={doc} index={index}
          fileField={(label, path, setter, accept) => fileField(label, path, setter, accept, `doc-${index}`)}
          canRemove={value.documents.length > 1} count={value.documents.length}
          onMove={direction => { change("documents", moveItem(value.documents, index, direction));
            setPart7Translation(old => ({ ...old, documents: moveItem(old.documents, index, direction) }));
            setPreviews({}); }}
          onRemove={() => { change("documents", value.documents.filter((_, at) => at !== index));
            setPart7Translation(old => ({ ...old, documents: old.documents.filter((_, at) => at !== index) }));
            setPreviews({}); }}
          onChange={(next, mutation) => { change("documents", value.documents.map((item, at) => at === index ? next : item));
            if (mutation) setPart7Translation(old => updateTranslationStructure(old, index, mutation)); }} />)}
      </div>}
      {unit.part === 7 && <PartSevenLivePreview documents={value.documents} questions={unit.questions || []}
        text={value.text} token={token} bank={unit.bank || "DE_THI"}
        issues={value.documents.flatMap(documentIssues)} />}
      {unit.part === 7 && <div className="exam-author-docs"><h3>Bản dịch tiếng Việt theo từng tài liệu</h3>
        {value.documents.map((doc, index) => <PartSevenTranslationForm key={index} original={doc} token={token} bank={unit.bank}
          value={part7Translation.documents[index]} index={index} onChange={next =>
            setPart7Translation(old => ({ ...old, documents: value.documents.map((_, at) =>
              at === index ? next : old.documents[at]) }))} />)}
        {part7Translation.invalid && <p role="alert" className="exam-alert exam-alert-error">Bản dịch có cấu trúc hiện tại không đọc được. Chưa lưu để bảo vệ dữ liệu cũ.</p>}
        <label>Bản dịch văn bản chung hoặc bản dịch cũ<textarea rows={3} value={part7Translation.legacyText}
          onChange={event => setPart7Translation(old => ({ ...old, legacyText: event.target.value }))} /></label>
      </div>}
      {[3, 4, 6, 7].includes(unit.part) && <details className="exam-optional-fields"><summary>Thông tin bổ sung</summary>
        {unit.part === 7 && <label>Ngữ liệu chung nếu có<textarea rows={3}
          value={value.text} onChange={event => change("text", event.target.value)} /></label>}
        {unit.part === 6 && value.translation && !part6Translation(value.translation, 1) &&
          <p className="exam-alert">Bản dịch cũ: {value.translation}. Hãy nhập bản dịch riêng cho bốn câu trước khi lưu.</p>}
        {unit.part === 6 ? Array.from({ length: 4 }, (_, index) => <label key={index}>Bản dịch câu {index + 1}
          <textarea rows={2} value={part6Translation(value.translation, index + 1)}
            onChange={event => change("translation", withPart6Translation(value.translation, index + 1, event.target.value))} />
          {!part6Translation(value.translation, index + 1) && <small className="exam-field-error">Cần nhập bản dịch câu {index + 1}.</small>}
        </label>) : unit.part === 7 ? null : <label>Bản dịch nếu có<textarea rows={3}
          value={value.translation} onChange={event => change("translation", event.target.value)} /></label>}
      </details>}
      <div className="exam-editor-actions"><button type="button" onClick={cancel} disabled={busy}>Hủy sửa</button>
        {unit.part === 7 && <button type="button" disabled={busy} onClick={() =>
          setDraftNotice(savePartSevenGroupDraft(token, unit.bank || "DE_THI", unit.groupId,
            source, value, part7Translation) ? "Đã lưu bản nháp chỉnh sửa trên trình duyệt này." :
            "Không lưu được bản nháp trên trình duyệt này.")}>Lưu nháp</button>}
        <button className="admin-button" type="submit" disabled={busy || !dirty}>{busy ? "Đang lưu…" : "Lưu ngữ liệu"}</button></div>
    </form>
  </section>;
}
