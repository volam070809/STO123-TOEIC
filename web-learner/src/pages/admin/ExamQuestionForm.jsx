import { useEffect, useRef, useState } from "react";
import { apiRequest } from "../../services/api";
import PartSevenDocumentForm from "./PartSevenDocumentForm";
import PartSevenTranslationForm from "./PartSevenTranslationForm";
import PartSevenLivePreview from "./PartSevenLivePreview";
import { documentPayload, moveItem, newDocument, partSevenIssues } from "./partSevenDocumentData";
import { serializePart7Translation, updateTranslationStructure } from "./partSevenTranslationData";
import { clearPartSevenDraft, loadPartSevenDraft, savePartSevenDraft } from "./partSevenDraft";
import PartSevenStudio from "./PartSevenStudio";

const emptyQuestion = () => ({ text: "", translation: "", a: "", b: "", c: "", d: "", correct: "A", difficulty: 2, explanation: "" });
const questionCount = part => ({ 1: 1, 2: 1, 3: 3, 4: 3, 5: 1, 6: 4, 7: 2 })[Number(part)] || 1;

export default function ExamQuestionForm({ token, onClose, onSaved, fixedBank, initialPart = 1, initialCount = null, examId = null, draftScope = null, inline = false, fixedPart = false }) {
  const [draft] = useState(() => Number(initialPart) === 7 ?
    loadPartSevenDraft(token, fixedBank || "DE_THI", examId) : null);
  const [part, setPart] = useState(Number(initialPart));
  const [bank, setBank] = useState(fixedBank || "DE_THI");
  const [text, setText] = useState(draft?.text || "");
  const [translation, setTranslation] = useState("");
  const [audioPath, setAudioPath] = useState("");
  const [imagePath, setImagePath] = useState("");
  const [documents, setDocuments] = useState(() => draft?.documents || (Number(initialPart) === 7 ? [newDocument()] : []));
  const [part7Translation, setPart7Translation] = useState(draft?.part7Translation || { legacyText: "", documents: [] });
  const [questions, setQuestions] = useState(() =>
    draft?.questions || Array.from({ length: initialCount || questionCount(initialPart) }, emptyQuestion));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [draftNotice, setDraftNotice] = useState(draft ? "Đã mở lại bản nháp Part 7 trên trình duyệt này." : "");
  const [fieldErrors, setFieldErrors] = useState({});
  const [dirty, setDirty] = useState(Boolean(draft));
  const [previews, setPreviews] = useState({});
  const previewUrls = useRef([]);

  useEffect(() => () => previewUrls.current.forEach(URL.revokeObjectURL), []);

  useEffect(() => {
    if (!dirty) return undefined;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    const guardLink = event => {
      const link = event.target.closest?.("a[href]");
      if (link && !window.confirm("Rời trang và bỏ câu hỏi chưa lưu?")) {
        event.preventDefault(); event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", guardLink, true);
    return () => { window.removeEventListener("beforeunload", warn);
      document.removeEventListener("click", guardLink, true); };
  }, [dirty]);

  function close() {
    if (busy) return;
    if (part === 7 && dirty && savePartSevenDraft(token, bank, examId,
      { text, documents, part7Translation, questions })) { onClose(); return; }
    if (dirty && !window.confirm("Bạn có thay đổi chưa lưu. Đóng và bỏ nội dung đã nhập?")) return;
    onClose();
  }

  function changePart(value) {
    const next = Number(value);
    if (next === part) return;
    if (dirty && !window.confirm("Đổi Part sẽ đặt lại ngữ liệu và số câu theo cấu trúc mới. Tiếp tục?")) return;
    const saved = next === 7 ? loadPartSevenDraft(token, bank, examId) : null;
    setPart(next); setDirty(true); setError("");
    setFieldErrors({});
    setQuestions(saved?.questions || Array.from({ length: questionCount(next) }, emptyQuestion));
    setDocuments(saved?.documents || (next === 7 ? [newDocument()] : []));
    setPart7Translation(saved?.part7Translation || { legacyText: "", documents: [] });
    setText(saved?.text || ""); setTranslation(""); setAudioPath(""); setImagePath("");
    setPreviews({});
  }

  function changeQuestion(index, key, value) {
    setQuestions(old => old.map((item, at) => at === index ? { ...item, [key]: value } : item));
    setFieldErrors(old => ({ ...old, [`${index}-${key}`]: "" }));
    setDirty(true);
  }

  function changeBank(next) {
    if (next === bank) return;
    if (part === 7) {
      if (dirty && !savePartSevenDraft(token, bank, examId, { text, documents, part7Translation, questions }) &&
        !window.confirm("Không lưu được bản nháp. Đổi ngân hàng và bỏ nội dung Part 7 đang nhập?")) return;
      const saved = loadPartSevenDraft(token, next, examId);
      setDocuments(saved?.documents || [newDocument()]);
      setQuestions(saved?.questions || Array.from({ length: 2 }, emptyQuestion));
      setPart7Translation(saved?.part7Translation || { legacyText: "", documents: [] });
      setText(saved?.text || "");
      setDraftNotice(saved ? "Đã mở lại bản nháp Part 7 của ngân hàng này." : "");
      setPreviews({});
      setDirty(Boolean(saved));
    } else setDirty(true);
    setBank(next);
  }

  async function upload(file, setter, key) {
    if (!file) return;
    setBusy(true); setError("");
    try {
      const form = new FormData(); form.append("file", file);
      const saved = await apiRequest(`/api/admin/content/media/upload?bank=${bank}`, {
        token, method: "POST", body: form, timeoutMs: 120_000
      });
      setter(saved.path); setDirty(true);
      const url = URL.createObjectURL(file);
      previewUrls.current.push(url);
      setPreviews(old => ({ ...old, [key]: url }));
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }

  function fileField(label, value, setter, accept, key = label) {
    return <div className="exam-author-file"><span>{label}</span><small>{value || "Chưa chọn tệp"}</small><div className="exam-unit-actions"><label>Tải tệp<input type="file" accept={accept} disabled={busy}
      onChange={event => { upload(event.target.files?.[0], setter, key); event.target.value = ""; }}/></label>{value && <button type="button" disabled={busy} onClick={() => { setter(""); setDirty(true); setPreviews(old => ({ ...old, [key]: null })); }}>Gỡ</button>}</div>
      {previews[key] && (accept.includes("audio") ? <audio controls src={previews[key]} /> : <img className="exam-media-preview" src={previews[key]} alt="Ảnh mới" />)}</div>;
  }

  async function save(event) {
    event.preventDefault();
    setError("");
    const invalid = {};
    questions.forEach((question, index) => {
      if ([3, 4, 5, 7].includes(Number(part)) && !question.text.trim())
        invalid[`${index}-text`] = `Câu ${index + 1}: cần nhập nội dung câu hỏi.`;
      if (Number(part) === 5 && !question.explanation.trim())
        invalid[`${index}-explanation`] = `Câu ${index + 1}: cần nhập giải thích.`;
      if (Number(part) === 6 && !question.translation.trim())
        invalid[`${index}-translation`] = `Câu ${index + 1}: cần nhập bản dịch câu hỏi.`;
    });
    setFieldErrors(invalid);
    if (Object.keys(invalid).length) { setError("Kiểm tra các trường được đánh dấu bên dưới."); return; }
    if ([1, 2, 3, 4].includes(Number(part)) && !audioPath) {
      setError(`Part ${part} cần tệp audio MP3.`); return;
    }
    if (Number(part) === 1 && !imagePath) {
      setError("Part 1 cần ảnh mô tả."); return;
    }
    if (Number(part) === 6 && !text.trim()) {
      setError("Part 6 cần đoạn văn chung có bốn chỗ trống [1]–[4]."); return;
    }
    if (Number(part) === 6 && [1, 2, 3, 4].some(index => !text.includes(`[${index}]`))) {
      setError("Đoạn văn Part 6 phải có đủ [1], [2], [3], [4]."); return;
    }
    if (part === 7 && (documents.length < 1 || documents.length > 3)) {
      setError("Part 7 cần từ 1 đến 3 tài liệu."); return;
    }
    if (part === 7) {
      const issues = partSevenIssues(documents, questions);
      if (issues.length) { setError(issues.join(" ")); return; }
    }
    let structuredTranslation = null;
    if (part === 7) {
      try { structuredTranslation = serializePart7Translation(part7Translation, documents); }
      catch (failure) { setError(failure.message); return; }
    }
    setBusy(true);
    try {
      const payload = {
        part, bank, text: part === 5 ? null : text || null,
        translation: part === 7 ? structuredTranslation : part === 5 ? null : translation || null,
        audioPath: part === 5 ? null : audioPath || null,
        imagePath: part === 5 ? null : imagePath || null,
        documents: part === 7 ? documents.map(documentPayload) : [],
        questions: questions.map(item => ({
          text: item.text || null, a: item.a, b: item.b, c: item.c,
          d: part === 2 ? null : item.d, correct: item.correct,
          difficulty: Number(item.difficulty), explanation: item.explanation || null,
          translation: [2, 6].includes(Number(part)) ? item.translation || null : null
        }))
      };
      const result = await apiRequest(examId ? `/api/admin/content/question-authoring/exam/${examId}` : "/api/admin/content/question-authoring", {
        token, method: "POST", body: payload, timeoutMs: 90_000
      });
      if (part === 7) clearPartSevenDraft(token, bank, examId);
      setDirty(false);
      onSaved(result);
    } catch (e) { setError(e.message); }
    finally { setBusy(false); }
  }

  if (part === 7) return <PartSevenStudio token={token} bank={bank} examId={examId} inline={inline}
    initialCount={initialCount} draftScope={draftScope}
    onClose={onClose} onSaved={onSaved} onPartChange={fixedPart ? undefined : changePart} />;
  return <div className={inline ? "admin-panel exam-inline-author" : "exam-drawer-backdrop"} onMouseDown={event => { if (!inline && event.target === event.currentTarget) close(); }}>
    <div className={inline ? "exam-author-inline" : `exam-drawer exam-author-drawer${part === 7 ? " is-part-seven" : ""}`} role={inline ? undefined : "dialog"} aria-modal={inline ? undefined : "true"} aria-label="Thêm câu hỏi">
      <div className="exam-drawer-head"><div><h2>{[1, 2, 5].includes(Number(part)) ? "Thêm câu hỏi" : "Thêm nhóm câu hỏi"} · Part {part}</h2><p>Nhập nội dung theo cấu trúc Part; lưu một lần khi hoàn tất.</p></div><button type="button" onClick={close} disabled={busy}>Đóng</button></div>
      {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
      {part === 7 && draftNotice && <p role="status" className="exam-alert">{draftNotice}</p>}
      <form onSubmit={save} onInvalidCapture={event => { const section = event.target.closest("details"); if (section) section.open = true; }} className="exam-author-form">
        <div className="exam-author-two"><label>Part<select value={part} onChange={event => changePart(event.target.value)} disabled={busy || fixedPart}>{Array.from({ length: 7 }, (_, index) => <option key={index + 1} value={index + 1}>Part {index + 1}</option>)}</select></label>
          {fixedBank ? <p className="exam-form-bank">Ngân hàng: <strong>{fixedBank === "DE_THI" ? "Câu hỏi đề thi" : "Câu hỏi luyện tập"}</strong></p> :
            <label>Ngân hàng<select value={bank} onChange={event => changeBank(event.target.value)} disabled={busy}><option value="DE_THI">Đề thi</option><option value="LUYEN_TAP">Luyện tập</option></select></label>}</div>
        {part !== 5 && <section><h3>{Number(part) === 7 ? "Tài liệu đọc" : Number(part) === 6 ? "Đoạn văn chung" : Number(part) <= 2 ? "Âm thanh và hình ảnh" : "Ngữ liệu nghe chung"}</h3>
          {[3, 4, 6].includes(Number(part)) && <label>{Number(part) === 3 ? "Hội thoại chung" : Number(part) === 4 ? "Bài nói chung" : "Đoạn văn có chỗ trống [1]–[4]"}
            <textarea value={text} onChange={event => { setText(event.target.value); setDirty(true); }} rows={Number(part) === 6 ? 7 : 4}/></label>}
          {[1, 2, 3, 4].includes(Number(part)) && <div className="exam-author-two">{fileField("Audio MP3", audioPath, setAudioPath, ".mp3,audio/mpeg")}
            {[1, 3, 4].includes(Number(part)) && fileField("Hình ảnh", imagePath, setImagePath, ".jpg,.jpeg,.png,.webp,image/*")}</div>}
          {part === 7 && <div className="exam-author-docs"><div className="exam-drawer-head"><h3>Tài liệu đọc</h3><button type="button" disabled={documents.length >= 3} onClick={() => { setDocuments(old => [...old, newDocument()]); setDirty(true); }}>Thêm tài liệu</button></div>
            {documents.map((doc, index) => <PartSevenDocumentForm key={index} index={index} value={doc} fileField={fileField}
              canRemove={documents.length > 1} count={documents.length}
              onMove={direction => { setDocuments(old => moveItem(old, index, direction));
                setPart7Translation(old => ({ ...old, documents: moveItem(old.documents, index, direction) }));
                setPreviews({}); setDirty(true); }}
              onRemove={() => { setDocuments(old => old.filter((_, at) => at !== index)); setPreviews({});
                setPart7Translation(old => ({ ...old, documents: old.documents.filter((_, at) => at !== index) })); setDirty(true); }}
              onChange={(next, mutation) => { setDocuments(old => old.map((item, at) => at === index ? next : item));
                if (mutation) setPart7Translation(old => updateTranslationStructure(old, index, mutation));
                setDirty(true); }}/>)}</div>}
          {part === 7 && <section className="exam-author-docs"><h3>Bản dịch tiếng Việt theo từng tài liệu</h3>
            {documents.map((doc, index) => <PartSevenTranslationForm key={index} original={doc} token={token} bank={bank}
              value={part7Translation.documents[index]} index={index} onChange={next => {
                setPart7Translation(old => ({ ...old, documents: documents.map((_, at) =>
                  at === index ? next : old.documents[at]) })); setDirty(true);
              }} />)}
            <label>Bản dịch văn bản chung hoặc bản dịch cũ (nếu có)<textarea rows={3}
              value={part7Translation.legacyText} onChange={event => { setPart7Translation(old =>
                ({ ...old, legacyText: event.target.value })); setDirty(true); }} /></label>
          </section>}
          {[3, 4, 6, 7].includes(Number(part)) && <details className="exam-optional-fields"><summary>Thông tin bổ sung</summary>
            {Number(part) === 7 && <label>Ngữ liệu chung nếu có<textarea value={text} onChange={event => { setText(event.target.value); setDirty(true); }} rows={3}/></label>}
            {Number(part) !== 6 && Number(part) !== 7 && <label>Bản dịch nếu có<textarea value={translation} onChange={event => { setTranslation(event.target.value); setDirty(true); }} rows={2}/></label>}
          </details>}
        </section>}
        <section><h3>Câu hỏi và đáp án</h3>{questions.map((item, index) => <div className="exam-author-question" key={index}><h4>Câu {index + 1}</h4><label>Nội dung câu hỏi<textarea value={item.text} rows={2} aria-invalid={!!fieldErrors[`${index}-text`]} onChange={event => changeQuestion(index, "text", event.target.value)}/>{fieldErrors[`${index}-text`] && <small className="exam-field-error" role="alert">{fieldErrors[`${index}-text`]}</small>}</label>
          {[2, 6].includes(Number(part)) && <label>Bản dịch câu hỏi{Number(part) === 2 ? " (nếu có)" : ""}<textarea value={item.translation} rows={2} aria-invalid={!!fieldErrors[`${index}-translation`]} onChange={event => changeQuestion(index, "translation", event.target.value)}/>{fieldErrors[`${index}-translation`] && <small className="exam-field-error" role="alert">{fieldErrors[`${index}-translation`]}</small>}</label>}
          <div className="exam-author-two">{["a", "b", "c", ...(part === 2 ? [] : ["d"])].map(key => <label key={key}>Đáp án {key.toUpperCase()}<input required maxLength={512} value={item[key]} onChange={event => changeQuestion(index, key, event.target.value)}/></label>)}</div>
          <div className="exam-author-two"><label>Đáp án đúng<select value={item.correct} onChange={event => changeQuestion(index, "correct", event.target.value)}>{["A", "B", "C", ...(part === 2 ? [] : ["D"])].map(value => <option key={value}>{value}</option>)}</select></label>
            <label>Độ khó<select value={item.difficulty} onChange={event => changeQuestion(index, "difficulty", event.target.value)}><option value={1}>Dễ</option><option value={2}>Trung bình</option><option value={3}>Khó</option></select></label></div>
          <label>Giải thích<textarea value={item.explanation} rows={2} aria-invalid={!!fieldErrors[`${index}-explanation`]} onChange={event => changeQuestion(index, "explanation", event.target.value)}/>{fieldErrors[`${index}-explanation`] && <small className="exam-field-error" role="alert">{fieldErrors[`${index}-explanation`]}</small>}</label></div>)}
          {part === 7 && <button type="button" disabled={questions.length >= 5} onClick={() => { setQuestions(old => [...old, emptyQuestion()]); setDirty(true); }}>Thêm câu Part 7</button>}
          {part === 7 && questions.length > 2 && <button type="button" onClick={() => { setQuestions(old => old.slice(0, -1)); setDirty(true); }}>Bớt một câu</button>}
        </section>
        {part === 7 && <PartSevenLivePreview documents={documents} questions={questions} text={text}
          token={token} bank={bank} issues={partSevenIssues(documents, questions)} />}
        {part === 7 && <div className="exam-editor-actions"><button type="button" disabled={busy}
          onClick={() => setDraftNotice(savePartSevenDraft(token, bank, examId,
            { text, documents, part7Translation, questions }) ?
            "Đã lưu bản nháp trên trình duyệt này. Có thể đóng và mở lại form." :
            "Không lưu được bản nháp trên trình duyệt này.")}>Lưu nháp</button></div>}
        <div className="exam-editor-actions"><button className="admin-button admin-button-secondary" type="button" onClick={close} disabled={busy}>Hủy</button><button className="admin-button" type="submit" disabled={busy}>{busy ? "Đang xử lý…" : [1, 2, 5].includes(Number(part)) ? "Lưu câu hỏi" : "Lưu nhóm câu"}</button></div>
      </form>
    </div>
  </div>;
}
