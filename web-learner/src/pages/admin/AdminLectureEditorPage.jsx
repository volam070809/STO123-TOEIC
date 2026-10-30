import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminContentApi } from "../../services/adminContentApi";
import ContentDialog, { ContentField } from "./ContentDialog";
import "../../styles/admin-content.css";

const initial = { title: "", summary: "", content: "", categoryId: "", level: "", partId: "", status: "DANG_MO" };
const kinds = [["HINH_ANH", "Hình ảnh"], ["PDF", "PDF"], ["AUDIO", "Âm thanh"], ["VIDEO", "Video MP4"],
  ["WORD", "Word DOC/DOCX"], ["POWERPOINT", "PowerPoint PPT/PPTX"]];
const accepts = { HINH_ANH: ".jpg,.jpeg,.png,.webp", PDF: ".pdf", AUDIO: ".mp3,.wav", VIDEO: ".mp4",
  WORD: ".doc,.docx", POWERPOINT: ".ppt,.pptx", EXCEL: ".xls,.xlsx" };

export default function AdminLectureEditorPage() {
  const { lectureId } = useParams(); const creating = !lectureId;
  const { token, user } = useAuth(); const navigate = useNavigate();
  const [form, setForm] = useState(initial); const [lecture, setLecture] = useState(null);
  const [categories, setCategories] = useState([]); const [parts, setParts] = useState([]); const [loading, setLoading] = useState(!creating);
  const [saving, setSaving] = useState(false); const [error, setError] = useState(""); const [notice, setNotice] = useState("");
  const [capabilities, setCapabilities] = useState({ maxFileBytes: 250000000, excelEnabled: false });
  const [progress, setProgress] = useState(null);
  const [resource, setResource] = useState({ name: "", kind: "PDF", file: null });
  const [replaceId, setReplaceId] = useState(null); const [deleteId, setDeleteId] = useState(null);
  const [renameId, setRenameId] = useState(null); const [renameName, setRenameName] = useState("");
  const canEdit = creating || user?.vaiTro === "ADMIN_QUAN_LY" || Number(user?.maNguoiDung) === lecture?.ownerId;

  async function load() {
    const [cats, partList, item, resourceCapabilities] = await Promise.all([adminContentApi.categories(token), adminContentApi.parts(token),
      creating ? Promise.resolve(null) : adminContentApi.lecture(lectureId, token), adminContentApi.resourceCapabilities(token)]);
    setCategories(cats); setParts(partList); setLecture(item); setCapabilities(resourceCapabilities); setLoading(false);
    if (item) setForm({ title: item.title, summary: item.summary || "", content: item.content || "",
      categoryId: String(item.categoryId || ""), level: String(item.level || ""), partId: String(item.partId || ""), status: item.status });
  }
  useEffect(() => { let live = true;
    Promise.all([adminContentApi.categories(token), adminContentApi.parts(token),
      creating ? Promise.resolve(null) : adminContentApi.lecture(lectureId, token), adminContentApi.resourceCapabilities(token)])
      .then(([cats, partList, item, resourceCapabilities]) => { if (!live) return;
        setCategories(cats); setParts(partList); setLecture(item); setCapabilities(resourceCapabilities); setLoading(false);
        if (item) setForm({ title: item.title, summary: item.summary || "", content: item.content || "",
          categoryId: String(item.categoryId || ""), level: String(item.level || ""), partId: String(item.partId || ""), status: item.status }); })
      .catch(() => live && (setError("Không tải được bài giảng."), setLoading(false)));
    return () => { live = false; }; }, [token, lectureId, creating]);

  async function save(e) {
    e.preventDefault(); setSaving(true); setError("");
    try {
      const body = { title: form.title.trim(), summary: form.summary.trim() || null, content: form.content.trim() || null,
        categoryId: form.categoryId ? Number(form.categoryId) : null, level: form.level ? Number(form.level) : null,
        partId: form.partId ? Number(form.partId) : null, status: form.status };
      if (!body.title || body.title.length > 128) throw new Error("Tiêu đề phải có 1–128 ký tự.");
      if (creating) { const saved = await adminContentApi.addLecture(body, token); navigate(`/admin/content/lectures/${saved.id}`, { replace: true }); }
      else { await adminContentApi.editLecture(lectureId, body, token); setNotice("Đã lưu bài giảng."); await load(); }
    } catch (e) { setError(e.data?.message || e.message || "Không lưu được bài giảng."); }
    finally { setSaving(false); }
  }
  async function upload(e) {
    e.preventDefault(); if (!resource.file || !resource.name.trim()) { setError("Chọn tệp và nhập tên tài nguyên."); return; }
    if (resource.file.size > capabilities.maxFileBytes) { setError("Tệp vượt quá giới hạn dung lượng."); return; }
    setSaving(true); setError(""); setProgress(0);
    try {
      const data = new FormData(); data.set("name", resource.name.trim()); data.set("kind", resource.kind); data.set("file", resource.file);
      await adminContentApi.uploadResource(lectureId, replaceId, data, token, setProgress);
      setResource({ name: "", kind: "PDF", file: null }); setReplaceId(null); setNotice("Đã tải tài nguyên lên và lưu liên kết."); await load();
    } catch (e) { setError(e.data?.message || "Không tải được tài nguyên."); }
    finally { setSaving(false); setProgress(null); }
  }
  async function renameResource() {
    if (!renameName.trim() || renameName.trim().length > 128) { setError("Tên tài nguyên phải có 1–128 ký tự."); return; }
    setSaving(true); setError("");
    try { await adminContentApi.renameResource(lectureId, renameId, renameName.trim(), token);
      setRenameId(null); setNotice("Đã đổi tên tài nguyên."); await load(); }
    catch (e) { setError(e.data?.message || "Không đổi được tên tài nguyên."); }
    finally { setSaving(false); }
  }
  async function moveResource(index, delta) {
    const next = [...lecture.resources]; const other = index + delta;
    if (other < 0 || other >= next.length) return;
    [next[index], next[other]] = [next[other], next[index]];
    setSaving(true); setError("");
    try { await adminContentApi.orderResources(lectureId, next.map(x => x.id), token);
      setNotice("Đã cập nhật thứ tự tài nguyên."); await load(); }
    catch (e) { setError(e.data?.message || "Không sắp xếp được tài nguyên."); }
    finally { setSaving(false); }
  }
  async function deleteResource() {
    setSaving(true); setError("");
    try { await adminContentApi.deleteResource(lectureId, deleteId, token); setDeleteId(null); setNotice("Đã xóa tài nguyên."); await load(); }
    catch (e) { setError(e.data?.message || "Không xóa được tài nguyên."); }
    finally { setSaving(false); }
  }
  return <div className="admin-page"><Link className="admin-back-link" to="/admin/content/lectures">← Danh sách bài giảng</Link>
    <div className="admin-page-header"><div><h1>{creating ? "Tạo bài giảng" : lecture?.title || "Bài giảng"}</h1>
      <p>Nội dung bài giảng hiển thị cho học viên khi được gắn vào bước học trong khóa đã mua.</p></div></div>
    {loading ? <p role="status">Đang tải…</p> : <>
      {notice && <p className="admin-feedback is-success" role="status">{notice}</p>}
      {error && <p className="admin-feedback is-error" role="alert">{error}</p>}
      <form className="admin-panel content-editor" onSubmit={save}>
        <label>Tiêu đề<input required maxLength={128} disabled={!canEdit} value={form.title} onChange={e => setForm({ ...form, title: e.target.value })}/></label>
        <label>Tóm tắt<textarea maxLength={512} rows={2} disabled={!canEdit} value={form.summary} onChange={e => setForm({ ...form, summary: e.target.value })}/></label>
        <label>Nội dung<textarea maxLength={100000} rows={10} disabled={!canEdit} value={form.content} onChange={e => setForm({ ...form, content: e.target.value })}/></label>
        <div className="content-editor-grid"><label>Danh mục<select disabled={!canEdit} value={form.categoryId} onChange={e => setForm({ ...form, categoryId: e.target.value })}>
          <option value="">Không có</option>{categories.filter(x => x.status === "DANG_MO" || String(x.id) === form.categoryId)
            .map(x => <option value={x.id} key={x.id}>{x.name}</option>)}</select></label>
          <label>Cấp độ<select disabled={!canEdit} value={form.level} onChange={e => setForm({ ...form, level: e.target.value })}>
            <option value="">Không chỉ định</option><option value="1">1</option><option value="2">2</option><option value="3">3</option></select></label>
          <label>Part TOEIC<select disabled={!canEdit} value={form.partId} onChange={e => setForm({ ...form, partId: e.target.value })}>
            <option value="">Không chỉ định</option>{parts.map(x => <option value={x.maPart} key={x.maPart}>Part {x.soPart}</option>)}</select></label>
          <label>Trạng thái<select disabled={!canEdit} value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>
            <option value="DANG_MO">Đang mở</option><option value="DA_DONG">Đã đóng</option></select></label></div>
        {canEdit && <button className="admin-button" disabled={saving} type="submit">{saving ? "Đang lưu…" : "Lưu bài giảng"}</button>}
      </form>
      {!creating && <section className="admin-panel content-editor"><h2>Tài nguyên</h2>
        {!lecture?.resources.length && <p className="content-empty">Bài giảng chưa có tệp. Nội dung văn bản vẫn có thể được học.</p>}
        {lecture?.resources.map((x, index) => <div className="content-resource-row" key={x.id}><span>{x.order}. {x.name} · {x.kind}</span>
          {canEdit && <span><button disabled={saving || index === 0} aria-label={`Đưa ${x.name} lên`} onClick={() => moveResource(index, -1)}>↑</button>
            <button disabled={saving || index === lecture.resources.length - 1} aria-label={`Đưa ${x.name} xuống`} onClick={() => moveResource(index, 1)}>↓</button>
            <button disabled={saving} onClick={() => { setRenameId(x.id); setRenameName(x.name); }}>Đổi tên</button>
            <button disabled={saving} onClick={() => { setReplaceId(x.id); setResource({ name: x.name, kind: x.kind, file: null }); }}>Thay tệp</button>
            <button disabled={saving} className="content-text-danger" onClick={() => setDeleteId(x.id)}>Xóa</button></span>}</div>)}
        {canEdit && <form className="content-resource-form" onSubmit={upload}><h3>{replaceId ? "Thay tài nguyên" : "Thêm tài nguyên"}</h3>
          <input aria-label="Tên tài nguyên" required maxLength={128} placeholder="Tên tài nguyên" value={resource.name}
            onChange={e => setResource({ ...resource, name: e.target.value })}/>
          <select aria-label="Loại tài nguyên" value={resource.kind} onChange={e => setResource({ ...resource, kind: e.target.value })}>
            {[...kinds, ...(capabilities.excelEnabled ? [["EXCEL", "Excel XLS/XLSX"]] : [])]
              .map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select>
          <input aria-label="Tệp tài nguyên" type="file" accept={accepts[resource.kind]} required
            onChange={e => { const file = e.target.files?.[0] || null;
              setResource({ ...resource, file, name: resource.name || file?.name || "" }); }}/>
          <small>Tối đa {(capabilities.maxFileBytes / 1000000).toFixed(0)} MB. Máy chủ kiểm tra chữ ký tệp, MIME và phần mở rộng.</small>
          {progress !== null && <div role="status"><progress max="100" value={progress}/><span> Đang tải lên {progress}%</span></div>}
          <div className="content-actions"><button className="admin-button" disabled={saving} type="submit">{replaceId ? "Lưu tệp thay thế" : "Tải lên"}</button>
            {replaceId && <button type="button" onClick={() => { setReplaceId(null); setResource({ name: "", kind: "PDF", file: null }); }}>Hủy thay tệp</button>}</div>
        </form>}</section>}
      {deleteId && <ContentDialog title="Xóa tài nguyên?" busy={saving} error={error} onCancel={() => setDeleteId(null)}
        onSubmit={deleteResource} danger submitLabel="Xác nhận xóa"><p>Tệp sẽ bị gỡ sau khi SQL lưu thành công.</p></ContentDialog>}
      {renameId && <ContentDialog title="Đổi tên tài nguyên" busy={saving} error={error} onCancel={() => setRenameId(null)}
        onSubmit={renameResource} submitLabel="Lưu tên"><ContentField label="Tên hiển thị">
          <input required maxLength={128} value={renameName} onChange={e => setRenameName(e.target.value)}/>
        </ContentField></ContentDialog>}
    </>}
  </div>;
}
