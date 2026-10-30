import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminContentApi } from "../../services/adminContentApi";
import ContentDialog, { ContentField } from "./ContentDialog";
import "../../styles/admin-content.css";

export default function AdminLecturesPage() {
  const { token, user } = useAuth(); const [params, setParams] = useSearchParams();
  const search = params.get("search") || ""; const page = Math.max(1, Number(params.get("page")) || 1);
  const [draftSearch, setDraftSearch] = useState(search);
  const [categories, setCategories] = useState([]); const [result, setResult] = useState(null);
  const [error, setError] = useState(""); const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0); const [dialog, setDialog] = useState(null);
  const [form, setForm] = useState({ name: "", description: "", status: "DANG_MO" });
  const [busy, setBusy] = useState(false); const [dialogError, setDialogError] = useState("");
  function reload() { setRevision(x => x + 1); }
  useEffect(() => {
    let live = true;
    Promise.all([adminContentApi.categories(token), adminContentApi.lectures(search, page, token)])
      .then(([cats, list]) => { if (live) { setCategories(cats); setResult(list); } })
      .catch(() => live && setError("Không tải được danh sách bài giảng."));
    return () => { live = false; };
  }, [token, search, page, revision]);
  function open(type, mode, item) {
    setDialog({ type, mode, item }); setDialogError("");
    setForm({ name: item?.name || "", description: item?.description || "", status: item?.status || "DANG_MO" });
  }
  async function submit() {
    if (!dialog) return; setBusy(true); setDialogError("");
    try {
      if (dialog.type === "category") {
        const body = { ...form, name: form.name.trim(), description: form.description.trim() || null };
        if (dialog.mode === "create") await adminContentApi.addCategory(body, token);
        if (dialog.mode === "edit") await adminContentApi.editCategory(dialog.item.id, body, token);
        if (dialog.mode === "delete") await adminContentApi.deleteCategory(dialog.item.id, token);
      } else await adminContentApi.deleteLecture(dialog.item.id, token);
      setDialog(null); setNotice("Đã cập nhật bài giảng."); reload();
    } catch (e) { setDialogError(e.data?.message || "Không thực hiện được thao tác."); }
    finally { setBusy(false); }
  }
  function applySearch(e) { e.preventDefault(); setResult(null); setError(""); setParams(draftSearch ? { search: draftSearch } : {}); }
  return <div className="admin-page"><div className="admin-page-header"><div><h1>Bài giảng & tài nguyên</h1>
    <p>Bài giảng có thể được tham chiếu bởi nhiều khóa học. Sửa nội dung đang có tiến độ sẽ bị chặn.</p></div>
    <Link className="admin-button" to="/admin/content/lectures/new">Tạo bài giảng</Link></div>
    {notice && <p className="admin-feedback is-success" role="status">{notice}</p>}
    <section className="admin-panel"><div className="content-panel-head"><h2>Danh mục</h2>
      <button className="admin-button admin-button-secondary" onClick={() => open("category", "create")}>Thêm danh mục</button></div>
      {!categories.length && <p className="content-empty">Chưa có danh mục. Bài giảng vẫn có thể tạo không cần danh mục.</p>}
      <div className="content-category-list">{categories.map(cat => <div className="content-category" key={cat.id}>
        <span><strong>{cat.name}</strong> · {cat.lectureCount} bài · {cat.status === "DANG_MO" ? "Đang mở" : "Đã đóng"}</span>
        <span><button onClick={() => open("category", "edit", cat)}>Sửa</button><button onClick={() => open("category", "delete", cat)}>Xóa</button></span>
      </div>)}</div></section>
    <section className="admin-panel"><div className="content-panel-head"><h2>Danh sách bài giảng</h2></div>
      <form className="admin-search-form" onSubmit={applySearch}><label className="admin-search"><span className="sr-only">Tìm bài giảng</span>
        <input value={draftSearch} onChange={e => setDraftSearch(e.target.value)} placeholder="Tìm theo tiêu đề"/></label>
        <button className="admin-button" type="submit">Tìm</button></form>
      {error ? <p role="alert" className="content-error">{error} <button onClick={reload}>Thử lại</button></p> : !result ?
        <p role="status">Đang tải…</p> : result.items.length ? <><div className="admin-table-wrap"><table className="admin-table">
          <thead><tr><th>Bài giảng</th><th>Danh mục</th><th>Trạng thái</th><th>Tài nguyên</th><th>Trong lộ trình</th><th></th></tr></thead>
          <tbody>{result.items.map(x => <tr key={x.id}><td><Link to={`/admin/content/lectures/${x.id}`}>{x.title}</Link></td>
            <td>{x.categoryName || "—"}</td><td>{x.status === "DANG_MO" ? "Đang mở" : "Đã đóng"}</td>
            <td>{x.resourceCount}</td><td>{x.courseStepCount}</td><td>{(user?.vaiTro === "ADMIN_QUAN_LY" || user?.maNguoiDung === x.ownerId) &&
              <button className="content-text-danger" onClick={() => open("lecture", "delete", x)}>Xóa</button>}</td></tr>)}</tbody>
        </table></div><div className="content-pager"><button disabled={page <= 1} onClick={() => setParams({ search, page: String(page - 1) })}>Trước</button>
          <span>Trang {page} · {result.total} bài</span><button disabled={page * result.pageSize >= result.total}
            onClick={() => setParams({ search, page: String(page + 1) })}>Sau</button></div></> :
        <p className="content-empty">Chưa có bài giảng phù hợp.</p>}
    </section>
    {dialog && <ContentDialog title={dialog.type === "category" ? `${dialog.mode === "create" ? "Thêm" : dialog.mode === "edit" ? "Sửa" : "Xóa"} danh mục` : "Xóa bài giảng"}
      busy={busy} error={dialogError} onCancel={() => !busy && setDialog(null)} onSubmit={submit}
      danger={dialog.mode === "delete"} submitLabel={dialog.mode === "delete" ? "Xác nhận xóa" : "Lưu"}>
      {dialog.mode === "delete" ? <p>Chỉ xóa được khi không còn dữ liệu phụ thuộc.</p> : <>
        <ContentField label="Tên danh mục"><input required maxLength={64} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })}/></ContentField>
        <ContentField label="Mô tả"><textarea maxLength={512} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })}/></ContentField>
        <ContentField label="Trạng thái"><select value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>
          <option value="DANG_MO">Đang mở</option><option value="DA_DONG">Đã đóng</option></select></ContentField></>}
    </ContentDialog>}
  </div>;
}
