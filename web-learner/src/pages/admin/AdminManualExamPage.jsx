import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import ExamQuestionForm from "./ExamQuestionForm";
import ExamQuestionEdit from "./ExamQuestionEdit";
import ExamGroupEdit from "./ExamGroupEdit";
import QuestionContentPreview from "./QuestionContentPreview";
import "../../styles/admin-exams.css";
import "../../styles/admin-question-demo.css";

const targets = [6, 25, 39, 30, 30, 16, 54];
const root = "/api/admin/content/exams/manual/drafts";

export default function AdminManualExamPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const examId = Number(params.get("examId")) || null;
  const [name, setName] = useState("");
  const [part, setPart] = useState(1);
  const [draft, setDraft] = useState(null);
  const [units, setUnits] = useState([]);
  const [detail, setDetail] = useState(null);
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState(null);
  const [groupEditing, setGroupEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);
  const counts = targets.map((_, index) => units.filter(unit => unit.part === index + 1)
    .reduce((sum, unit) => sum + unit.questionIds.length, 0));
  const total = counts.reduce((sum, count) => sum + count, 0);
  const ready = counts.every((count, index) => count === targets[index]);

  useEffect(() => {
    if (!examId) return;
    let live = true;
    (async () => {
      const saved = await apiRequest(`${root}/${examId}`, { token });
      const unique = [...new Map(saved.links.map(link =>
        [link.groupId ? `g${link.groupId}` : `q${link.maCauHoi}`, link])).values()];
      const details = await Promise.all(unique.map(link =>
        apiRequest(`/api/admin/content/questions/units/${link.maCauHoi}`, { token })));
      if (!live) return;
      setDraft(saved); setName(saved.name);
      setUnits(details.map(unit => ({ ...unit, questionIds: unit.questions.map(q => q.id),
        order: saved.links.find(link => link.maCauHoi === unit.questions[0].id)?.thuTu ?? 0 }))
        .sort((a, b) => a.order - b.order));
    })().catch(e => { if (live) setError(e.message); });
    return () => { live = false; };
  }, [examId, revision, token]);

  async function saveProgress() {
    if (!name.trim()) { setError("Nhập tên đề trước khi lưu."); return; }
    setBusy(true); setError("");
    try {
      if (examId) {
        await apiRequest(`${root}/${examId}`, { method: "PUT", token, body: { name: name.trim() } });
        setRevision(value => value + 1);
      } else {
        const saved = await apiRequest(root, { method: "POST", token, body: { name: name.trim() } });
        setParams({ examId: String(saved.examId) });
      }
      setNotice("Đã lưu tiến độ vào cơ sở dữ liệu.");
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  async function remove(unit) {
    if (!window.confirm("Gỡ khỏi đề? Câu nguồn vẫn ở Ngân hàng câu hỏi.")) return;
    setBusy(true); setError("");
    try {
      await apiRequest(`${root}/${examId}/units/${unit.questionIds[0]}`, { method: "DELETE", token });
      setDetail(null); setRevision(value => value + 1);
      setNotice("Đã gỡ khỏi đề. Câu nguồn vẫn được giữ.");
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  async function publish() {
    if (!ready || !examId) return;
    setBusy(true); setError("");
    try {
      await apiRequest(`${root}/${examId}/publish`, {
        method: "POST", token, timeoutMs: 120000
      });
      navigate("/admin/content/exams");
    } catch (e) { setError(e.message); } finally { setBusy(false); }
  }

  if (adding) return <div className="admin-page admin-exams admin-manual-demo">
    <nav className="exam-breadcrumb" aria-label="Đường dẫn"><Link to="/admin/content/exams">Quản lý đề thi</Link><span>› {name || "Đề đang soạn"} › Part {part} › Thêm mới</span></nav>
    <div className="admin-page-header"><div><h1>Thêm {[1, 2, 5].includes(part) ? "câu hỏi" : "nhóm câu"} · Part {part}</h1>
      <p>Tạo trực tiếp và lưu đồng thời vào ngân hàng câu hỏi đề thi cùng Part {part}.</p></div></div>
    <ExamQuestionForm key={part} inline fixedPart token={token} examId={examId} fixedBank="DE_THI" initialPart={part}
      onClose={() => setAdding(false)} onSaved={() => { setAdding(false); setRevision(value => value + 1);
        setNotice(`Đã tạo câu nguồn trong DE_THI và liên kết vào Part ${part}.`); }} />
  </div>;

  if (detail) return <div className="admin-page admin-exams admin-manual-demo">
    <nav className="exam-breadcrumb" aria-label="Đường dẫn"><Link to="/admin/content/exams">Quản lý đề thi</Link><span>› {name || "Đề đang soạn"} › Part {part} › Chi tiết</span></nav>
    <div className="admin-page-header"><div><h1>Chi tiết {detail.groupId ? `nhóm ${detail.groupId}` : `câu ${detail.questionIds[0]}`}</h1>
      <p>Part {part} · {detail.questions.length} câu · Hiển thị ngữ liệu, đáp án đúng và giải thích.</p></div>
      {!editingId && !groupEditing && <button type="button" className="admin-button admin-button-secondary" onClick={() => setDetail(null)}>← Quay lại Part</button>}</div>
    {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {notice && <p role="status" className="exam-alert exam-alert-success">{notice}</p>}
    <section className="admin-panel question-detail">
      {editingId ? <ExamQuestionEdit inline id={editingId} token={token} onClose={nextId => setEditingId(nextId || null)}
        onSaved={() => { setEditingId(null); setDetail(null); setRevision(value => value + 1);
          setNotice("Đã lưu câu nguồn và cập nhật đề đang soạn."); }} /> :
      groupEditing ? <ExamGroupEdit unit={detail} token={token} onCancel={() => setGroupEditing(false)}
        onSaved={() => { setGroupEditing(false); setDetail(null); setRevision(value => value + 1);
          setNotice("Đã lưu ngữ liệu chung."); }} /> : <>
        <div className="exam-editor-header"><h2>Nội dung câu hỏi</h2>
          {draft?.status === "CHUA_XUAT_BAN" && detail.groupId && <button type="button" onClick={() => setGroupEditing(true)}>Sửa ngữ liệu chung</button>}</div>
        <QuestionContentPreview unit={detail} token={token} onEditQuestion={draft?.status === "CHUA_XUAT_BAN" ? setEditingId : undefined} />
      </>}
    </section>
  </div>;

  return <div className="admin-page admin-exams admin-manual-demo">
    <div className="admin-page-header"><div><h1>Soạn đề thủ công</h1><p>Tạo câu mới theo từng Part. Tiến độ được lưu vào cơ sở dữ liệu.</p></div><Link className="admin-button admin-button-secondary" to="/admin/content/exams">Quản lý đề thi</Link></div>
    {error && <p role="alert" className="exam-alert exam-alert-error">{error}</p>}
    {notice && <p role="status" className="exam-alert exam-alert-success">{notice}</p>}
    <section className="admin-panel exam-manual-summary"><label>Tên đề<input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder="Ví dụ: ETS 2024 Test 01" /></label>
      <span><strong>{total}/200 câu</strong> · {draft?.status === "CHUA_XUAT_BAN" || !draft ? "Chưa xuất bản" : draft.status}</span>
      <div className="exam-unit-actions"><button type="button" className="admin-button admin-button-secondary" disabled={busy} onClick={saveProgress}>Lưu tiến độ</button><button type="button" className="admin-button" disabled={busy || !ready || !examId} onClick={publish}>Xuất bản</button></div></section>
    <div className="exam-manual-workspace"><nav className="admin-panel exam-manual-parts" aria-label="TOEIC Part">{targets.map((target, index) => <button key={index} type="button" className={part === index + 1 ? "is-active" : ""} onClick={() => { setPart(index + 1); setAdding(false); setDetail(null); }}>Part {index + 1} <small>{counts[index]}/{target}</small></button>)}</nav>
    <section className="admin-panel exam-manual-content"><div className="exam-editor-header"><div><h2>Nội dung Part {part}</h2><p>{counts[part - 1]} / {targets[part - 1]} câu · {units.filter(unit => unit.part === part).length} nhóm hoặc câu riêng</p></div><button type="button" className="admin-button" disabled={!examId || counts[part - 1] >= targets[part - 1] || draft?.status !== "CHUA_XUAT_BAN"} onClick={() => { setAdding(true); setDetail(null); }}>{[1, 2, 5].includes(part) ? "Thêm câu mới" : "Thêm nhóm mới"}</button></div>
      <p className="exam-edit-impact">Tạo trực tiếp câu hoặc nhóm theo Part. Lưu sẽ ghi câu nguồn vào ngân hàng đề thi và liên kết với đề trong cùng giao dịch.</p>
      <div className="exam-group-list">{units.filter(unit => unit.part === part).map((unit, index) => <article key={unit.questionIds[0]} className="exam-group-row"><div><strong>{unit.groupId ? `Nhóm ${index + 1}` : `Câu ${index + 1}`}</strong><span>{unit.questionIds.length} câu · Mã {unit.questionIds.join(", ")}</span><p>{unit.resource?.text || unit.questions[0]?.text}</p></div><div className="exam-unit-actions"><button type="button" onClick={() => setDetail(unit)}>Xem</button><button type="button" disabled={busy || draft?.status !== "CHUA_XUAT_BAN"} onClick={() => remove(unit)}>Gỡ</button></div></article>)}{counts[part - 1] === 0 && <div className="exam-empty"><strong>Part {part} chưa có câu nào.</strong><p>Nhấn Thêm để tạo trực tiếp nội dung cho Part này.</p></div>}</div>
    </section>
    <aside className="admin-panel exam-manual-progress"><h2>Tiến độ đề</h2><strong>{total}<small> / 200 câu</small></strong><div className="exam-manual-progressbar"><span style={{ width: `${Math.min(100, total / 2)}%` }}/></div>
      {targets.map((target, index) => <div className="exam-manual-check" key={index}><span>Part {index + 1}</span><b>{counts[index]} / {target}</b></div>)}
      <p>{ready ? "Đã đủ 200 câu. Kiểm tra nội dung trước khi xuất bản." : "Có thể lưu tiến độ và quay lại hoàn thiện."}</p></aside></div>
  </div>;
}

