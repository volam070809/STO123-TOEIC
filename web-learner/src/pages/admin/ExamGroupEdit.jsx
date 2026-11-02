import { useEffect, useRef, useState } from "react";
import { apiRequest } from "../../services/api";
import PartSevenDocumentForm from "./PartSevenDocumentForm";
import { documentPayload, fromDocument, newDocument } from "./partSevenDocumentData";

export default function ExamGroupEdit({ unit, token, onCancel, onSaved }) {
  const [value, setValue] = useState(() => ({
    text: unit.resource?.text || "", translation: unit.resource?.translation || "",
    audioPath: unit.resource?.audio || "", imagePath: unit.resource?.image || "",
    documents: (unit.documents || []).map(fromDocument)
  }));
  const [previews, setPreviews] = useState({});
  const objectUrls = useRef([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [initial, setInitial] = useState(() => JSON.stringify({
    text: unit.resource?.text || "", translation: unit.resource?.translation || "",
    audioPath: unit.resource?.audio || "", imagePath: unit.resource?.image || "",
    documents: (unit.documents || []).map(fromDocument)
  }));
  const dirty = JSON.stringify(value) !== initial;
  useEffect(() => () => objectUrls.current.forEach(URL.revokeObjectURL), []);
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
      {previews[key] && (accept.includes("audio") ? <audio controls src={previews[key]} /> :
        <img className="exam-media-preview" src={previews[key]} alt={label} />)}
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
    setBusy(true); setError("");
    try {
      const result = await apiRequest(`/api/admin/content/groups/${unit.groupId}/unit`, {
        method: "PUT", token, timeoutMs: 120000,
        body: { text: value.text || null, translation: value.translation || null,
          audioPath: value.audioPath || null, imagePath: value.imagePath || null,
          documents: unit.part === 7 ? value.documents.map(documentPayload) : [] }
      });
      setInitial(JSON.stringify(value));
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
    <form className="exam-author-form" onSubmit={save} onInvalidCapture={event => { const section = event.target.closest("details"); if (section) section.open = true; }}>
      {unit.part >= 3 && unit.part <= 6 && <label>{label}<textarea rows={unit.part === 6 ? 7 : 4}
        value={value.text} onChange={event => change("text", event.target.value)} /></label>}
      {[1, 2, 3, 4].includes(unit.part) && fileField("Audio MP3", value.audioPath,
        path => change("audioPath", path), ".mp3,audio/mpeg", "audio")}
      {unit.part === 1 && fileField("Hình ảnh", value.imagePath,
        path => change("imagePath", path), ".jpg,.jpeg,.png,.webp,image/*", "image")}
      {unit.part === 7 && <div className="exam-author-docs"><div className="exam-editor-header"><h3>Tài liệu Part 7</h3>
        <button type="button" disabled={value.documents.length >= 3} onClick={() =>
          change("documents", [...value.documents, newDocument()])}>Thêm tài liệu</button></div>
        {value.documents.map((doc, index) => <PartSevenDocumentForm key={index} value={doc} index={index}
          fileField={(label, path, setter, accept) => fileField(label, path, setter, accept, `doc-${index}`)}
          canRemove={value.documents.length > 1}
          onRemove={() => change("documents", value.documents.filter((_, at) => at !== index))}
          onChange={next => change("documents", value.documents.map((item, at) => at === index ? next : item))} />)}
      </div>}
      {[3, 4, 6, 7].includes(unit.part) && <details className="exam-optional-fields"><summary>Thông tin bổ sung</summary>
        {unit.part === 7 && <label>Ngữ liệu chung nếu có<textarea rows={3}
          value={value.text} onChange={event => change("text", event.target.value)} /></label>}
        <label>Bản dịch nếu có<textarea rows={3}
          value={value.translation} onChange={event => change("translation", event.target.value)} /></label>
      </details>}
      <div className="exam-editor-actions"><button type="button" onClick={cancel} disabled={busy}>Hủy sửa</button>
        <button className="admin-button" type="submit" disabled={busy || !dirty}>{busy ? "Đang lưu…" : "Lưu ngữ liệu"}</button></div>
    </form>
  </section>;
}
