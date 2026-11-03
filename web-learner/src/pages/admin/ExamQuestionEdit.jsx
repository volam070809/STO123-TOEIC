import { useEffect, useState } from "react";
import { apiRequest } from "../../services/api";
import QuestionContentPreview from "./QuestionContentPreview";

export default function ExamQuestionEdit({ id, token, onClose, onSaved, inline = false }) {
  const [value, setValue] = useState(null);
  const [unit, setUnit] = useState(null);
  const [initial, setInitial] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [fieldErrors, setFieldErrors] = useState({});
  const dirty = value && JSON.stringify(value) !== initial;

  useEffect(() => {
    let live = true;
    setLoading(true); setValue(null); setUnit(null); setInitial(""); setError(""); setFieldErrors({});
    Promise.all([apiRequest(`/api/admin/content/question-authoring/${id}`, { token }),
      apiRequest(`/api/admin/content/questions/units/${id}`, { token })])
      .then(([data, group]) => { if (live) { setValue(data); setInitial(JSON.stringify(data)); setUnit(group); } })
      .catch(e => { if (live) setError(e.message); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [id, token]);

  useEffect(() => {
    if (!dirty) return undefined;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    const guardLink = event => {
      const link = event.target.closest?.("a[href]");
      if (link && !window.confirm("Câu hỏi có thay đổi chưa lưu. Rời trang và bỏ thay đổi?")) {
        event.preventDefault(); event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", guardLink, true);
    return () => { window.removeEventListener("beforeunload", warn);
      document.removeEventListener("click", guardLink, true); };
  }, [dirty]);

  function close() {
    if (saving) return;
    if (dirty && !window.confirm("Bạn có thay đổi chưa lưu. Đóng và bỏ nội dung đã nhập?")) return;
    onClose();
  }

  function change(key, next) { setValue(old => ({ ...old, [key]: next })); setFieldErrors(old => ({ ...old, [key]: "" })); }
  async function save(event) {
    event.preventDefault(); setError("");
    const invalid = {};
    if ([3, 4, 5, 7].includes(Number(value.part)) && !value.text?.trim()) invalid.text = "Cần nhập nội dung câu hỏi.";
    if (Number(value.part) === 5 && !value.explanation?.trim()) invalid.explanation = "Cần nhập giải thích.";
    if (Number(value.part) === 6 && !value.translation?.trim()) invalid.translation = "Cần nhập bản dịch câu hỏi.";
    setFieldErrors(invalid);
    if (Object.keys(invalid).length) return;
    setSaving(true);
    try {
      await apiRequest(`/api/admin/content/question-authoring/${id}`, {
        method: "PUT", token, body: {
          text: value.text, a: value.a, b: value.b, c: value.c,
          d: value.part === 2 ? null : value.d, correct: value.correct,
          difficulty: Number(value.difficulty), explanation: value.explanation,
          translation: [2, 6].includes(Number(value.part)) ? value.translation : null
        }
      });
      await onSaved(); setInitial(JSON.stringify(value));
    } catch (e) { setError(e.message); }
    finally { setSaving(false); }
  }

  return <div className={inline ? "exam-inline-edit" : "exam-drawer-backdrop"} onMouseDown={event => { if (!inline && event.target === event.currentTarget) close(); }}><div className={inline ? "exam-author-inline" : "exam-drawer"} role={inline ? undefined : "dialog"} aria-modal={inline ? undefined : "true"} aria-label={`Sửa câu ${id}`}>
    <div className="exam-drawer-head"><div><h2>Sửa câu {id}{unit?.groupId ? ` · Nhóm ${unit.groupId}` : ""}</h2><p>{value ? `Part ${value.part} · ${unit?.questions?.length || 1} câu trong nhóm${dirty ? " · Chưa lưu" : ""}` : "Đang tải…"}</p></div><button type="button" onClick={close} disabled={saving}>Đóng</button></div>
    {loading && <p>Đang tải câu hỏi…</p>}{error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {unit?.groupId && <details className="exam-shared-context"><summary>Ngữ liệu chung của nhóm</summary><QuestionContentPreview unit={{ ...unit, questions: [] }} token={token} /></details>}
    {value && <form className="exam-author-form" onSubmit={save}>
      {value.examCount > 0 && <p className="exam-alert">Câu đang dùng trong {value.examCount} đề. Lưu sẽ cập nhật câu nguồn và đề hiện hành liên quan; lượt đang thi giữ phiên bản cũ.</p>}
      <label>Nội dung câu hỏi<textarea value={value.text || ""} rows={3} aria-invalid={!!fieldErrors.text} onChange={event => change("text", event.target.value)}/>{fieldErrors.text && <small className="exam-field-error" role="alert">{fieldErrors.text}</small>}</label>
      {[2, 6].includes(Number(value.part)) && <label>Bản dịch câu hỏi{Number(value.part) === 2 ? " (nếu có)" : ""}<textarea value={value.translation || ""} rows={2} aria-invalid={!!fieldErrors.translation} onChange={event => change("translation", event.target.value)}/>{fieldErrors.translation && <small className="exam-field-error" role="alert">{fieldErrors.translation}</small>}</label>}
      {["a", "b", "c", ...(value.part === 2 ? [] : ["d"])].map(key => <label key={key}>Đáp án {key.toUpperCase()}<input required maxLength={512} value={value[key] || ""} onChange={event => change(key, event.target.value)}/></label>)}
      <div className="exam-author-two"><label>Đáp án đúng<select value={value.correct} onChange={event => change("correct", event.target.value)}>{["A", "B", "C", ...(value.part === 2 ? [] : ["D"])].map(letter => <option key={letter}>{letter}</option>)}</select></label><label>Độ khó<select value={value.difficulty} onChange={event => change("difficulty", event.target.value)}><option value={1}>Dễ</option><option value={2}>Trung bình</option><option value={3}>Khó</option></select></label></div>
      <label>Giải thích<textarea rows={3} value={value.explanation || ""} aria-invalid={!!fieldErrors.explanation} onChange={event => change("explanation", event.target.value)}/>{fieldErrors.explanation && <small className="exam-field-error" role="alert">{fieldErrors.explanation}</small>}</label>
      <p className="exam-answer-correct">Đáp án đúng: {value.correct}. {value[value.correct?.toLowerCase()] || "Chưa nhập nội dung"}</p>
      <div className="exam-editor-actions"><button className="admin-button admin-button-secondary" type="button" onClick={close} disabled={saving}>Hủy</button><button className="admin-button" type="submit" disabled={saving || !dirty}>{saving ? "Đang lưu…" : "Lưu câu này"}</button></div>
    </form>}
  </div></div>;
}
