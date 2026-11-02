import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/admin-exams.css";

const counts = [6, 25, 39, 30, 30, 16, 54];
const fresh = () => ({ name: "", parts: counts.map((_, index) => ({ part: index + 1, easy: 0, medium: 0, hard: 0 })) });
const root = "/api/admin/content/exam-criteria";

export default function AdminExamCriteriaPage() {
  const { token } = useAuth();
  const [items, setItems] = useState([]);
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState(fresh);
  const [initial, setInitial] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);
  const dirty = editing !== null && JSON.stringify(form) !== initial;
  const reload = useCallback(() => setRevision(value => value + 1), []);

  useEffect(() => {
    let live = true;
    apiRequest(root, { token }).then(data => { if (live) setItems(data.items || []); })
      .catch(e => { if (live) setError(e.message); });
    return () => { live = false; };
  }, [token, revision]);

  useEffect(() => {
    if (!dirty) return undefined;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  function open(item) {
    const value = item ? { name: item.name, parts: counts.map((count, index) =>
      item.parts.find(p => p.part === index + 1) || { part: index + 1, easy: 0, medium: count, hard: 0 }) } : fresh();
    setEditing(item?.id || "new"); setForm(value); setInitial(JSON.stringify(value)); setError("");
  }

  function close() {
    if (pending) return;
    if (dirty && !window.confirm("Bỏ những thay đổi chưa lưu?")) return;
    setEditing(null);
  }

  function changePart(index, key, value) {
    setForm(old => ({ ...old, parts: old.parts.map((part, at) => at === index ?
      { ...part, [key]: value === "" ? "" : Number(value) } : part) }));
  }

  const valid = form.name.trim().length > 0 && form.name.trim().length <= 64 &&
    form.parts.every((part, index) => [part.easy, part.medium, part.hard].every(n => Number.isInteger(n) && n >= 0) &&
      part.easy + part.medium + part.hard === counts[index]);

  async function save(event) {
    event.preventDefault();
    if (!valid) { setError("Mỗi Part phải đúng số câu quy định; tổng toàn đề là 200 câu."); return; }
    setPending(true); setError("");
    try {
      await apiRequest(editing === "new" ? root : `${root}/${editing}`, {
        method: editing === "new" ? "POST" : "PUT", token, body: form
      });
      setEditing(null); setNotice("Đã lưu cấu hình sinh đề vào Azure SQL."); reload();
    } catch (e) { setError(e.message); }
    finally { setPending(false); }
  }

  async function clone(item) {
    setPending(true); setError("");
    try { await apiRequest(`${root}/${item.id}/clone`, { method: "POST", token });
      setNotice("Đã nhân bản cấu hình."); reload(); }
    catch (e) { setError(e.message); } finally { setPending(false); }
  }

  async function remove(item) {
    if (!window.confirm(`Xóa vĩnh viễn cấu hình “${item.name}”?`)) return;
    setPending(true); setError("");
    try { await apiRequest(`${root}/${item.id}`, { method: "DELETE", token });
      setNotice("Đã xóa cấu hình."); reload(); }
    catch (e) { setError(e.message); } finally { setPending(false); }
  }

  async function generate(item) {
    if (!window.confirm(`Sinh đề mới từ cấu hình “${item.name}”? Hệ thống sẽ kiểm tra ngân hàng nguồn trước khi lưu.`)) return;
    setPending(true); setError("");
    try {
      const result = await apiRequest("/api/admin/mock-exams/generate", {
        method: "POST", token, body: { criteriaId: item.id }, timeoutMs: 120_000
      });
      setNotice(`Đã sinh đề ${result.examCode || result.examId} gồm ${result.questionCount} câu. Mở Quản lý đề thi để xem và xuất bản.`);
      reload();
    } catch (e) {
      const missing = e.data?.shortages?.map(row => `Part ${row.part}: thiếu dễ ${row.easyMissing}, trung bình ${row.mediumMissing}, khó ${row.hardMissing}`).join("; ");
      setError(missing ? `${e.message} ${missing}.` : e.message);
    } finally { setPending(false); }
  }

  return <div className="admin-page admin-exams"><div className="admin-page-header"><div><h1>Cấu hình sinh đề</h1><p>Phân bố độ khó cho 7 Part, đủ 200 câu TOEIC.</p></div><div className="exam-unit-actions"><button type="button" className="admin-button" onClick={() => open(null)}>Thêm cấu hình</button><Link className="admin-button admin-button-secondary" to="/admin/content/exams">Quản lý đề thi</Link></div></div>
    {notice && <p role="status" className="exam-alert exam-alert-success">{notice}</p>}
    {error && <p role="alert" className="exam-alert exam-alert-error">{error} <button type="button" onClick={reload}>Thử lại</button></p>}
    {pending && <p role="status" className="exam-working">Đang xử lý…</p>}
    <section className="admin-panel exam-group-list">{items.map(item => <article key={item.id} className="exam-group-row"><div><strong>{item.name}</strong><span>#{item.id} · {item.parts.reduce((sum, part) => sum + part.easy + part.medium + part.hard, 0)} câu · {item.examCount} đề đã dùng</span></div><div className="exam-unit-actions"><button type="button" disabled={pending} onClick={() => generate(item)}>Sinh đề</button><button type="button" disabled={pending || item.examCount > 0} title={item.examCount > 0 ? "Cấu hình đã dùng; hãy nhân bản để chỉnh" : undefined} onClick={() => open(item)}>Sửa</button><button type="button" disabled={pending} onClick={() => clone(item)}>Nhân bản</button><button type="button" disabled={pending || item.examCount > 0} onClick={() => remove(item)}>Xóa</button></div></article>)}{items.length === 0 && !error && <p>Chưa có cấu hình. Nhấn Thêm cấu hình để bắt đầu.</p>}</section>
    {editing !== null && <section className="admin-panel exam-criteria-editor" aria-label="Cấu hình sinh đề"><div className="exam-drawer-head"><div><h2>{editing === "new" ? "Thêm cấu hình" : "Sửa cấu hình"}</h2><p>Nhập số câu dễ, trung bình và khó cho từng Part.</p></div><button type="button" onClick={close} disabled={pending}>Đóng</button></div>
      <form onSubmit={save} className="exam-author-form"><label>Tên cấu hình<input required maxLength={64} value={form.name} onChange={event => setForm(old => ({ ...old, name: event.target.value }))}/></label>
        <div className="exam-criteria-grid exam-criteria-header"><span>Part</span><span>Dễ</span><span>Trung bình</span><span>Khó</span><span>Tổng</span></div>
        {form.parts.map((part, index) => <div className="exam-criteria-grid" key={part.part}><strong>Part {part.part}</strong>{["easy", "medium", "hard"].map(key => <input key={key} type="number" min="0" max={counts[index]} aria-label={`Part ${part.part} ${key}`} value={part[key]} onChange={event => changePart(index, key, event.target.value)}/>)}<span className={part.easy + part.medium + part.hard === counts[index] ? "" : "exam-criteria-invalid"}>{part.easy + part.medium + part.hard} / {counts[index]}</span></div>)}
        <p><strong>Tổng: {form.parts.reduce((sum, part) => sum + Number(part.easy) + Number(part.medium) + Number(part.hard), 0)} / 200 câu</strong></p>
        <div className="exam-editor-actions"><button type="button" className="admin-button admin-button-secondary" onClick={close} disabled={pending}>Hủy</button><button type="submit" className="admin-button" disabled={pending || !valid}>{pending ? "Đang lưu…" : "Lưu"}</button></div>
      </form></section>}
  </div>;
}
