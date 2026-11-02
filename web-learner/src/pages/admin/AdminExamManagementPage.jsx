import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { Link, useNavigate } from "react-router-dom";
import { apiRequest } from "../../services/api";
import QuestionContentPreview from "./QuestionContentPreview";
import ExamQuestionEdit from "./ExamQuestionEdit";
import ExamGroupEdit from "./ExamGroupEdit";
import "../../styles/admin-exams.css";

const root = "/api/admin/content/exams";
const partTargets = [6, 25, 39, 30, 30, 16, 54];

function toUnits(snapshot) {
  const groups = new Map(snapshot.groups.map(group => [group.id, group]));
  const seen = new Set();
  return snapshot.questions.map(question => {
    if (question.groupId && seen.has(question.groupId)) return null;
    if (question.groupId) seen.add(question.groupId);
    const members = question.groupId
      ? snapshot.questions.filter(item => item.groupId === question.groupId) : [question];
    const group = groups.get(question.groupId);
    return { key: question.groupId ? `g${question.groupId}` : `q${question.id}`,
      part: question.part, questionIds: members.map(item => item.id),
      preview: group?.text || question.text || `Câu ${question.id}`,
      groupId: question.groupId || null };
  }).filter(Boolean);
}

function snapshotUnit(snapshot, unit) {
  if (unit.detail) return { ...unit.detail, bank: "DE_THI" };
  const group = snapshot?.groups?.find(item => item.id === unit.groupId);
  return { groupId: unit.groupId, part: unit.part, bank: "DE_THI",
    resource: group ? { text: group.text, translation: group.translation,
      audio: group.audio, image: group.image } : null,
    documents: group?.documents?.map(doc => ({ ...doc, image: doc.imagePath })) || [],
    questions: (snapshot?.questions || []).filter(item => unit.questionIds.includes(item.id)) };
}

export default function AdminExamManagementPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [exams, setExams] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [part, setPart] = useState(1);
  const [snapshot, setSnapshot] = useState(null);
  const [loadedExamKey, setLoadedExamKey] = useState("");
  const [units, setUnits] = useState([]);
  const [missing, setMissing] = useState([]);
  const [editIndex, setEditIndex] = useState(null);
  const [addPart, setAddPart] = useState(null);
  const [isComposing, setIsComposing] = useState(false);
  const [createdReplacementIds, setCreatedReplacementIds] = useState(null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [detail, setDetail] = useState(null);
  const [detailIndex, setDetailIndex] = useState(null);
  const [examEdit, setExamEdit] = useState(null);
  const [sourceDetail, setSourceDetail] = useState(null);
  const [groupEditing, setGroupEditing] = useState(false);
  const [revision, setRevision] = useState(0);
  const publishKey = useRef(null);
  const exam = exams.find(item => item.examId === selectedId);
  const examKey = exam ? `${exam.examId}:${exam.currentVersionId ?? "draft"}` : "";
  const examReady = !!examKey && loadedExamKey === examKey;
  const initialIds = useMemo(() => snapshot?.questions.map(q => q.id) || [], [snapshot]);
  const chosenIds = units.flatMap(unit => unit.questionIds);
  const partCounts = useMemo(() => partTargets.map((_, index) => units.filter(unit => unit.part === index + 1)
    .reduce((sum, unit) => sum + unit.questionIds.length, 0)), [units]);
  const structureValid = partCounts.every((count, index) => count === partTargets[index]);
  const dirty = chosenIds.length !== initialIds.length || chosenIds.some((id, index) => id !== initialIds[index]);
  const editPart = addPart ?? (editIndex === null ? null : units[editIndex]?.part);
  const editCount = editIndex === null ? null : units[editIndex]?.questionIds.length;

  const reloadExams = useCallback(async () => {
    const data = await apiRequest(root, { token });
    setExams(data);
    setSelectedId(current => current && data.some(e => e.examId === current) ? current : null);
  }, [token]);

  useEffect(() => {
    let active = true;
    setBusy("Đang tải danh sách đề…"); setError("");
    reloadExams().catch(e => active && setError(e.message))
      .finally(() => active && setBusy(""));
    return () => { active = false; };
  }, [reloadExams, revision]);

  useEffect(() => {
    if (!selectedId) return;
    let active = true;
    setLoadedExamKey("");
    setBusy("Đang tải phiên bản đề…"); setError("");
    Promise.all([
      exam?.currentVersionId ? apiRequest(`${root}/${selectedId}/versions/${exam.currentVersionId}/preview`, { token, timeoutMs: 90_000 }) : Promise.resolve(null),
      exam?.currentVersionId ? apiRequest(`${root}/${selectedId}/versions/current-source-status`, { token }) : Promise.resolve(null),
    ]).then(([current, source]) => {
      if (!active) return;
      setSnapshot(current);
      setLoadedExamKey(`${selectedId}:${exam?.currentVersionId ?? "draft"}`);
      setUnits(current ? toUnits(current) : []);
      setMissing(source?.missingQuestionIds || []);
      setEditIndex(null); setAddPart(null); setIsComposing(false); setCreatedReplacementIds(null); publishKey.current = null;
    }).catch(e => active && setError(e.message)).finally(() => active && setBusy(""));
    return () => { active = false; };
  }, [selectedId, exam?.currentVersionId, token]);

  useEffect(() => {
    if (!detail?.questions?.length || detailIndex === null) {
      setSourceDetail(null); return;
    }
    let live = true;
    apiRequest(`/api/admin/content/questions/units/${detail.questions[0].id}`, { token })
      .then(value => { if (live) setSourceDetail(value); })
      .catch(e => { if (live) setError(e.message); });
    return () => { live = false; };
  }, [detail, detailIndex, token]);

  useEffect(() => {
    if (!dirty) return undefined;
    const warn = event => { event.preventDefault(); event.returnValue = ""; };
    const warnNavigation = event => {
      const link = event.target.closest?.("a[href]");
      if (!link || !new URL(link.href).pathname.startsWith("/admin/")) return;
      if (!window.confirm("Bạn có thay đổi đề chưa lưu. Rời trang và bỏ các thay đổi này?")) {
        event.preventDefault(); event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", warn);
    document.addEventListener("click", warnNavigation, true);
    return () => { window.removeEventListener("beforeunload", warn);
      document.removeEventListener("click", warnNavigation, true); };
  }, [dirty]);

  function chooseExam(id) {
    if (dirty && !window.confirm("Bạn có thay đổi chưa lưu. Chuyển đề và bỏ các thay đổi này?")) return;
    setLoadedExamKey(""); setSnapshot(null); setUnits([]); setIsComposing(false);
    setPart(1);
    setSelectedId(current => current === id ? null : id); setNotice(""); setError("");
  }

  function replace(candidate) {
    if (editIndex === null && addPart === null) return;
    const replacement = {
      key: `new-${candidate.questionIds.join("-")}`, part: candidate.part,
      questionIds: candidate.questionIds, preview: candidate.preview || `Câu ${candidate.questionIds[0]}`,
      groupId: candidate.groupId,
      detail: candidate,
    };
    setUnits(current => editIndex === null ? [...current, replacement].sort((a, b) => a.part - b.part) :
      current.map((unit, index) => index === editIndex ? replacement : unit));
    setEditIndex(null); setAddPart(null); setCreatedReplacementIds(null); publishKey.current = null;
    setNotice("Đã tạo câu nguồn và đưa vào bản đang soạn. Xuất bản để cập nhật đề cho lượt mới.");
  }

  async function attachCreated(ids) {
    setCreatedReplacementIds(ids); setError("");
    try {
      const data = await apiRequest(`/api/admin/content/questions/units/${ids[0]}`, { token });
      replace({ ...data, questionIds: ids, questionCount: ids.length,
        preview: data.resource?.text || data.questions[0]?.text });
    } catch (e) { setError(`Câu nguồn ${ids.join(", ")} đã lưu, nhưng chưa gắn được vào bản đang soạn: ${e.message}. Nhấn “Gắn câu vừa tạo” để thử lại.`); }
  }

  function move(index, offset) {
    const next = index + offset;
    if (next < 0 || next >= units.length || units[next].part !== units[index].part) return;
    setUnits(current => { const result = [...current]; [result[index], result[next]] = [result[next], result[index]]; return result; });
    publishKey.current = null;
  }

  async function publish() {
    if (!exam || !examReady || busy || (snapshot && !structureValid)) return;
    if (!window.confirm(`Lưu nội dung hiện hành của “${exam.name}”? Lượt đang thi giữ nguyên nội dung cũ.`)) return;
    publishKey.current ||= crypto.randomUUID();
    setBusy("Đang kiểm tra, sao lưu media và xuất bản…"); setError(""); setNotice("");
    try {
      await apiRequest(`${root}/${exam.examId}/versions/${dirty ? "publish-selection" : "publish"}`, {
        token, method: "POST", timeoutMs: 240_000,
        body: dirty ? { expectedCurrentVersionId: exam.currentVersionId,
          requestKey: publishKey.current, orderedQuestionIds: chosenIds } :
          { expectedCurrentVersionId: exam.currentVersionId, requestKey: publishKey.current },
      });
      publishKey.current = null; setIsComposing(false); setNotice("Đã cập nhật nội dung hiện hành. Lượt mới dùng nội dung mới.");
      await reloadExams();
    } catch (e) { setError(e.message); }
    finally { setBusy(""); }
  }

  async function changeStatus() {
    if (!exam || busy) return;
    const next = exam.status === "OPEN" ? "CLOSE" : "OPEN";
    if (!window.confirm(`${next === "OPEN" ? "Mở" : "Đóng"} đề “${exam.name}”?`)) return;
    setBusy("Đang lưu trạng thái…"); setError("");
    try {
      await apiRequest(`${root}/${exam.examId}/status`, { token, method: "PUT", body: { status: next } });
      await reloadExams(); setNotice(next === "OPEN" ? "Đã mở đề." : "Đã đóng đề. Lượt đang làm vẫn được tiếp tục.");
    } catch (e) { setError(e.message); }
    finally { setBusy(""); }
  }

  function openDetail(unit, index = null) { setDetail(unit); setDetailIndex(index); }

  return <div className={detail ? "admin-page admin-exams exam-show-detail" : "admin-page admin-exams"}>
    <div className="admin-page-header"><div><h1>Quản lý đề thi</h1><p>Xem nội dung hiện hành và tiếp tục biên soạn đề chưa xuất bản.</p></div><div className="exam-unit-actions"><Link className="admin-button" to="/admin/content/exams/new">Soạn đề thủ công</Link><Link className="admin-button admin-button-secondary" to="/admin/content/exam-criteria">Cấu hình sinh đề</Link></div></div>
    {notice && <p className="exam-alert exam-alert-success" role="status">{notice}</p>}
    {error && <p className="exam-alert exam-alert-error" role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Thử lại</button></p>}
    {busy && <p className="exam-working" role="status">{busy}</p>}
    {!busy && exams.length === 0 && <div className="admin-panel"><p>Chưa có đề thi nào trong hệ thống.</p></div>}
    {exams.length > 0 && <div className="exam-management-grid">
      {!selectedId && <nav className="admin-panel exam-picker" aria-label="Danh sách đề thi">
        <h2>Danh sách đề thi</h2><p>Chọn một đề để xem từng Part và nội dung câu hỏi.</p>{exams.map(item => <button type="button" key={item.examId}
          className={selectedId === item.examId ? "exam-picker-item is-active" : "exam-picker-item"}
          onClick={() => item.currentVersionId == null ? navigate(`/admin/content/exams/new?examId=${item.examId}`) : chooseExam(item.examId)}><span><strong>{item.name}</strong><small>#{item.examId} · {item.questionCount}/200 câu · 120 phút</small></span>
          <span className="exam-picker-status">{item.currentVersionId == null ? "Đang biên soạn" : item.status === "OPEN" ? "Đang mở" : "Đã đóng"}</span>
          <span className="exam-picker-action">{item.currentVersionId == null ? "Tiếp tục biên soạn →" : "Xem và chỉnh sửa →"}</span></button>)}
      </nav>}
      {exam && <section className="admin-panel exam-editor">
        <button type="button" className="exam-back-link" disabled={!!examEdit || groupEditing} onClick={() => chooseExam(null)}>← Danh sách đề</button>
        {exam.currentVersionId == null && <p><Link className="admin-button" to={`/admin/content/exams/new?examId=${exam.examId}`}>Tiếp tục soạn đề</Link></p>}
        <div className="exam-editor-header"><div><h2>{exam.name}</h2><p>{exam.questionCount} câu · {exam.duration} phút · {exam.status === "OPEN" ? "Đang mở" : "Đã đóng"}</p></div>
          <div className="exam-unit-actions">{snapshot && !isComposing && <button type="button" disabled={!!busy || !examReady} onClick={() => setIsComposing(true)}>Chỉnh sửa đề</button>}
            <button className="admin-button admin-button-secondary" type="button" disabled={!!busy} onClick={changeStatus}>{exam.status === "OPEN" ? "Đóng đề" : "Mở đề"}</button></div></div>
        {!examReady ? <p className="exam-working" role="status">Đang tải phiên bản đề…</p> : snapshot ? <>
          <div className="exam-version-line"><strong>Nội dung hiện hành</strong><span>{missing.length ? `${missing.length} câu nguồn đã xóa, cần thay trước khi lưu` : "Nguồn hiện còn đủ câu"}</span></div>
          <div className="exam-demo-workspace"><nav className="admin-panel exam-demo-parts" aria-label="Chọn Part của đề">{partTargets.map((target, index) => <button type="button" key={index}
            className={part === index + 1 ? "is-active" : ""} disabled={!!examEdit || groupEditing} onClick={() => { setPart(index + 1); setDetail(null); }}>
            <span>Part {index + 1}</span><small>{partCounts[index]}/{target}</small></button>)}</nav>
          {[part].map(part => <section className="exam-part" key={part}>
            <div className="exam-part-title">Part {part} <span>{partCounts[part - 1]}/{partTargets[part - 1]} câu</span></div>
            {isComposing && partCounts[part - 1] < partTargets[part - 1] && <button type="button" onClick={() => { setAddPart(part); setEditIndex(null); setCreatedReplacementIds(null); }}>Tạo câu/nhóm mới</button>}
            <div className="exam-unit-list">{units.map((unit, index) => unit.part === part && <article className="exam-unit" key={unit.key}>
              <div><button className="exam-unit-title" type="button" onClick={() => openDetail(snapshotUnit(snapshot, unit), index)}>{unit.groupId ? `Nhóm ${unit.groupId}` : `Câu ${unit.questionIds[0]}`}</button><span>{unit.questionIds.join(", ")} · {unit.questionIds.length} câu{unit.questionIds.some(id => missing.includes(id)) && <em> · Thiếu câu nguồn</em>}</span><p>{unit.preview}</p></div>
              <div className="exam-unit-actions"><button type="button" onClick={() => openDetail(snapshotUnit(snapshot, unit), index)}>Xem chi tiết</button></div>
            </article>)}</div>
          </section>)}
          <aside className="admin-panel exam-demo-progress"><h3>Tiến độ đề</h3><strong>{chosenIds.length}<small> / 200 câu</small></strong>
            <div className="exam-manual-progressbar"><span style={{ width: `${Math.min(100, chosenIds.length / 2)}%` }} /></div>
            {partTargets.map((target, index) => <div className="exam-manual-check" key={index}><span>Part {index + 1}</span><b>{partCounts[index]}/{target}</b></div>)}
          </aside></div>
          {isComposing && <div className="exam-editor-actions"><button className="admin-button admin-button-secondary" type="button" disabled={!!busy} onClick={() => { setUnits(toUnits(snapshot)); setIsComposing(false); publishKey.current = null; }}>Hủy biên soạn</button>
            <button className="admin-button" type="button" disabled={!!busy || !structureValid} onClick={publish}>{dirty ? "Xuất bản nội dung đã sắp xếp" : "Xuất bản từ câu nguồn hiện tại"}</button></div>}
        </> : <div className="exam-empty"><p>Đề chưa xuất bản · {exam.questionCount}/200 câu.</p>
          {exam.questionCount === 200 ? <button className="admin-button" type="button" disabled={!!busy} onClick={publish}>Xuất bản đề</button> :
            <Link className="admin-button" to={`/admin/content/exams/new?examId=${exam.examId}`}>Tiếp tục soạn đề</Link>}</div>}
      </section>}
    </div>}
    {(editIndex !== null && units[editIndex] || addPart !== null) && <div className="exam-drawer-backdrop">
      <aside className="exam-drawer exam-author-drawer" role="dialog" aria-modal="true" aria-label="Tạo câu hỏi trực tiếp">
        {createdReplacementIds ? <div className="exam-recovery"><h2>Câu nguồn đã được lưu</h2><p>Mã câu: {createdReplacementIds.join(", ")}. Hãy gắn câu vừa tạo vào bản đang soạn.</p>
          <button type="button" className="admin-button" onClick={() => attachCreated(createdReplacementIds)}>Gắn câu vừa tạo</button>
          <button type="button" className="admin-button admin-button-secondary" onClick={() => { setEditIndex(null); setAddPart(null); }}>Quay lại đề</button></div> :
          <ExamQuestionForm key={`${editPart}-${editCount || "add"}`} inline fixedPart fixedBank="DE_THI"
            initialPart={editPart} initialCount={editCount} token={token}
            onClose={() => { setEditIndex(null); setAddPart(null); }}
            onSaved={result => attachCreated(result.questionIds)} />}</aside></div>}
    {detail && <section className="admin-panel question-detail" aria-label="Chi tiết câu hỏi"><nav className="exam-breadcrumb" aria-label="Đường dẫn">Quản lý đề thi › {exam?.name} › Part {detail.part} › Chi tiết</nav><div className="exam-drawer-head"><div><h2>Chi tiết {detail.groupId ? `nhóm ${detail.groupId}` : `câu ${detail.questions[0]?.id}`}</h2><p>{detail.questions.length} câu · Part {detail.part}</p></div><button type="button" disabled={!!examEdit || groupEditing} onClick={() => { setDetail(null); setExamEdit(null); setGroupEditing(false); }}>← Quay lại Part</button></div>
      {!examEdit && !groupEditing && <QuestionContentPreview unit={detail} token={token}
        onEditQuestion={detailIndex !== null && !examEdit && !groupEditing ? id => { setExamEdit({ id }); setGroupEditing(false); } : undefined} />
      }{!examEdit && !groupEditing && sourceDetail?.exams?.length > 0 && <div className="exam-usage-list"><h3>Đề đang sử dụng</h3>
        {sourceDetail.exams.map(item => <p key={item.examId}>{item.name} (#{item.examId}) · {item.status}</p>)}</div>}
      {detailIndex !== null && sourceDetail?.groupId && !groupEditing && !examEdit && <button type="button"
        className="admin-button admin-button-secondary" onClick={() => { setGroupEditing(true); setExamEdit(null); }}>Sửa ngữ liệu và media chung</button>}
      {groupEditing && sourceDetail && <ExamGroupEdit unit={sourceDetail} token={token}
        onCancel={() => setGroupEditing(false)} onSaved={async () => {
          setGroupEditing(false); setDetail(null); setNotice("Đã cập nhật ngữ liệu nguồn và đề hiện hành liên quan. Lượt đang thi giữ nội dung cũ.");
          await reloadExams(); setRevision(value => value + 1);
        }} />}
      {examEdit && <ExamQuestionEdit inline id={examEdit.id} token={token}
        onClose={nextId => setExamEdit(nextId ? { id: nextId } : null)}
        onSaved={async () => {
          setExamEdit(null); setDetail(null); setNotice("Đã cập nhật câu nguồn và đề hiện hành liên quan. Lượt đang thi giữ nội dung cũ.");
          await reloadExams(); setRevision(value => value + 1);
        }} />}
      {detailIndex !== null && isComposing && !examEdit && !groupEditing && <div className="exam-editor-actions"><button type="button" onClick={() => { move(detailIndex, -1); setDetail(null); }}>Đưa lên</button><button type="button" onClick={() => { move(detailIndex, 1); setDetail(null); }}>Đưa xuống</button>
        {detail.questions.some(question => missing.includes(question.id)) && <button type="button" onClick={() => { setEditIndex(detailIndex); setAddPart(null); setCreatedReplacementIds(null); setDetail(null); }}>Tạo câu mới thay nguồn đã xóa</button>}
        <button type="button" onClick={() => { if (window.confirm("Gỡ khỏi bản đang soạn? Câu nguồn và phiên bản đã xuất bản không bị xóa.")) { setUnits(current => current.filter((_, at) => at !== detailIndex)); setDetail(null); } }}>Gỡ khỏi bản đang soạn</button></div>}
    </section>}
  </div>;
}
