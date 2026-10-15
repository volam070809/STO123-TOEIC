import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { AlertMessage } from "../../components/auth/AuthUI";
import Avatar from "../../components/auth/Avatar";
import { avatarApi } from "../../services/avatarApi";
import { authApi } from "../../services/authApi";
import SiteLayout from "../../layouts/SiteLayout";
import { prepareProfileUpdate, profileDraft } from "../../services/profileData";

const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
const ALLOWED_TYPES = new Set(["image/jpeg", "image/png", "image/webp"]);

export default function ProfilePage() {
  const { user, token, updateAvatar, updateProfile } = useAuth();
  const location = useLocation();
  const inputRef = useRef(null);
  const saveLock = useRef(false);
  const [preview, setPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(() => profileDraft(user));
  const [saving, setSaving] = useState(false);

  useEffect(() => () => { if (preview?.url) URL.revokeObjectURL(preview.url); }, [preview]);

  async function chooseImage(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setError(""); setNotice(""); setConfirmDelete(false);
    const extension = file.name.split(".").pop()?.toLowerCase();
    if (!ALLOWED_TYPES.has(file.type) || !["jpg", "jpeg", "png", "webp"].includes(extension)) {
      setError("Chỉ chấp nhận ảnh JPG, PNG hoặc WEBP."); return;
    }
    if (file.size > MAX_AVATAR_BYTES) { setError("Ảnh không được vượt quá 2 MB."); return; }
    try {
      const bitmap = await createImageBitmap(file);
      bitmap.close();
      setPreview({ file, url: URL.createObjectURL(file) });
    } catch { setError("Tệp đã chọn không phải ảnh hợp lệ."); }
  }

  async function saveImage() {
    if (!preview || busy) return;
    setBusy(true); setError("");
    try {
      const result = await avatarApi.upload(preview.file, token);
      updateAvatar({ hasCustomAvatar: true, avatarVersion: result.avatarVersion });
      setPreview(null);
      setNotice("Đã cập nhật ảnh đại diện.");
    } catch (err) { setError(err.message || "Không thể lưu ảnh đại diện."); }
    finally { setBusy(false); }
  }

  async function deleteImage() {
    if (busy) return;
    setBusy(true); setError("");
    try {
      const result = await avatarApi.remove(token);
      updateAvatar({ hasCustomAvatar: false, avatarVersion: null, anhDaiDien: result.anhDaiDien });
      setConfirmDelete(false);
      setNotice("Đã xóa ảnh đại diện.");
    } catch (err) { setError(err.message || "Không thể xóa ảnh đại diện."); }
    finally { setBusy(false); }
  }

  function cancelEdit() {
    setDraft(profileDraft(user));
    setEditing(false);
    setError("");
  }

  async function saveProfile(event) {
    event.preventDefault();
    if (saveLock.current) return;
    const { payload, error: validationError, changed } = prepareProfileUpdate(user, draft);
    setError(""); setNotice("");
    if (validationError) { setError(validationError); return; }
    if (!changed) { setEditing(false); return; }
    saveLock.current = true;
    setSaving(true);
    try {
      const updated = await authApi.updateProfile(payload, token);
      updateProfile(updated);
      setDraft(profileDraft(updated));
      setEditing(false);
      setNotice("Đã cập nhật thông tin cá nhân.");
    } catch (err) { setError(err.message || "Không thể cập nhật thông tin cá nhân."); }
    finally { saveLock.current = false; setSaving(false); }
  }

  return <SiteLayout className="profile-page"><div className="site-container">
    <div className="page-heading"><h1>Hồ sơ của tôi</h1><p>Quản lý thông tin cá nhân và tài khoản STO123.</p></div>
    <AlertMessage type="success">{notice || (error ? "" : location.state?.notice)}</AlertMessage>
    <AlertMessage type="error">{error}</AlertMessage>
    <div className="profile-grid">
      <section className="profile-panel profile-personal" aria-label="Thông tin cá nhân">
        <div className="profile-panel-heading"><h2>Thông tin cá nhân</h2>
          {!editing && <button type="button" className="outline-button" disabled={busy || saving}
            onClick={() => { setDraft(profileDraft(user)); setError(""); setEditing(true); }}>Chỉnh sửa thông tin</button>}
        </div>
        <div className="profile-identity">
          <Avatar user={user} size={72} />
          <div className="profile-identity-details"><strong>{user.hoTen}</strong><span>{user.email}</span>
            <div className="profile-avatar-actions">
              <input ref={inputRef} type="file" accept="image/jpeg,image/png,image/webp" onChange={chooseImage} hidden />
              <button type="button" disabled={busy || saving} onClick={() => inputRef.current?.click()}>Đổi ảnh đại diện</button>
              {user.hasCustomAvatar && <button type="button" disabled={busy || saving} onClick={() => { setConfirmDelete(true); setPreview(null); setError(""); }}>Xóa ảnh</button>}
            </div>
          </div>
        </div>
        {preview && <div className="profile-avatar-preview"><img src={preview.url} alt="Xem trước ảnh đại diện" />
          <div><span>Xem trước ảnh đại diện</span><div className="profile-avatar-actions">
            <button type="button" disabled={busy} onClick={() => setPreview(null)}>Hủy</button>
            <button type="button" disabled={busy} onClick={saveImage}>{busy ? "Đang lưu..." : "Lưu ảnh"}</button>
          </div></div></div>}
        {confirmDelete && <div className="profile-avatar-confirm" role="group" aria-label="Xác nhận xóa ảnh đại diện">
          <span>Bạn có chắc muốn xóa ảnh đại diện?</span><div className="profile-avatar-actions">
            <button type="button" disabled={busy} onClick={() => setConfirmDelete(false)}>Hủy</button>
            <button type="button" disabled={busy} onClick={deleteImage}>{busy ? "Đang xóa..." : "Xóa ảnh"}</button>
          </div></div>}
        {editing ? <form className="profile-edit-form" onSubmit={saveProfile} noValidate>
          <div className="profile-edit-field"><label htmlFor="profile-name">Họ và tên</label>
            <input id="profile-name" value={draft.hoTen} maxLength={64} autoComplete="name"
              onChange={event => setDraft(current => ({ ...current, hoTen: event.target.value }))} disabled={saving} /></div>
          <div className="profile-edit-field"><label htmlFor="profile-phone">Số điện thoại <span>(không bắt buộc)</span></label>
            <input id="profile-phone" type="tel" value={draft.soDienThoai} maxLength={16} autoComplete="tel"
              placeholder="Ví dụ: 0912345678" onChange={event => setDraft(current => ({ ...current, soDienThoai: event.target.value }))}
              disabled={saving} /><small>9–15 chữ số, có thể bắt đầu bằng +.</small></div>
          <div className="profile-form-actions">
            <button className="primary-button" type="submit" disabled={saving || busy} aria-busy={saving}>{saving ? "Đang lưu..." : "Lưu thay đổi"}</button>
            <button className="outline-button" type="button" disabled={saving} onClick={cancelEdit}>Hủy</button>
          </div>
        </form> : <dl className="profile-fields"><div><dt>Họ và tên</dt><dd>{user.hoTen}</dd></div>
          <div><dt>Email</dt><dd>{user.email}</dd></div>
          <div><dt>Số điện thoại</dt><dd>{user.soDienThoai || "Chưa cập nhật"}</dd></div></dl>}
      </section>
      <section className="profile-panel profile-account" aria-label="Tài khoản"><div className="profile-panel-heading"><h2>Tài khoản</h2></div>
        <dl className="profile-fields"><div><dt>Email</dt><dd>{user.email}</dd></div>
          <div><dt>Phương thức đăng nhập</dt><dd>{user.hasPassword === false ? "Google" : "Email"}</dd></div>
          <div><dt>Vai trò</dt><dd>{user.vaiTro === "HOC_VIEN" ? "Học viên" : user.vaiTro}</dd></div>
          <div><dt>Trạng thái</dt><dd>{user.trangThai === "HOAT_DONG" ? "Đang hoạt động" : user.trangThai}</dd></div></dl>
        {user.hasPassword !== false && <Link className="profile-account-link" to="/change-password">Đổi mật khẩu</Link>}
      </section>
    </div>
    <section className="profile-activity" aria-label="Hoạt động học tập"><h2>Hoạt động học tập</h2><div className="profile-activity-links">
      <Link to="/courses">Xem khóa học</Link>
      <Link to="/practice/vocabulary/history">Lịch sử luyện từ vựng</Link>
      <Link to="/mock-test/history">Kết quả thi thử</Link>
    </div>
    </section>
  </div></SiteLayout>;
}
