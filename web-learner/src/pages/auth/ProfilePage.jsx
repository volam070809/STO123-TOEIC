import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { AlertMessage } from "../../components/auth/AuthUI";
import Avatar from "../../components/auth/Avatar";
import { avatarApi } from "../../services/avatarApi";
import SiteLayout from "../../layouts/SiteLayout";

const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
const ALLOWED_TYPES = new Set(["image/jpeg", "image/png", "image/webp"]);

export default function ProfilePage() {
  const { user, token, logout, updateAvatar } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const inputRef = useRef(null);
  const [preview, setPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

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

  function signOut() { logout(); navigate("/", { replace: true }); }
  return <SiteLayout className="profile-page"><div className="site-container">
    <div className="page-heading"><h1>Tài khoản của tôi</h1><p>Quản lý thông tin cá nhân và bảo mật tài khoản.</p></div>
    <AlertMessage type="success">{location.state?.notice || notice}</AlertMessage>
    <AlertMessage type="error">{error}</AlertMessage>
    <div className="profile-grid">
      <section className="profile-panel" aria-label="Thông tin cá nhân"><span className="panel-eyebrow">THÔNG TIN CÁ NHÂN</span>
        <div className="profile-identity">
          <Avatar user={user} size={72} />
          <div className="profile-identity-details"><strong>{user.hoTen}</strong>
            <div className="profile-avatar-actions">
              <input ref={inputRef} type="file" accept="image/jpeg,image/png,image/webp" onChange={chooseImage} hidden />
              <button type="button" disabled={busy} onClick={() => inputRef.current?.click()}>Đổi ảnh đại diện</button>
              {user.hasCustomAvatar && <button type="button" disabled={busy} onClick={() => { setConfirmDelete(true); setPreview(null); setError(""); }}>Xóa ảnh</button>}
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
        <dl className="profile-fields"><div><dt>Họ và tên</dt><dd>{user.hoTen}</dd></div>
          <div><dt>Email</dt><dd>{user.email}</dd></div>
          <div><dt>Số điện thoại</dt><dd>{user.soDienThoai || "Chưa cập nhật"}</dd></div></dl>
      </section>
      <section className="profile-panel" aria-label="Trạng thái tài khoản"><span className="panel-eyebrow">TÀI KHOẢN</span>
        <dl className="profile-fields"><div><dt>Vai trò</dt><dd>{user.vaiTro === "HOC_VIEN" ? "Học viên" : user.vaiTro}</dd></div>
          <div><dt>Trạng thái</dt><dd>{user.trangThai === "HOAT_DONG" ? "Đang hoạt động" : user.trangThai}</dd></div></dl>
        <p className="profile-note">{user.hasPassword === false ? "Tài khoản này đăng nhập bằng Google và chưa có mật khẩu email." : "Thông tin được tải từ tài khoản STO123 của bạn."}</p>
      </section>
    </div>
    <section className="profile-settings"><span className="panel-eyebrow">CÀI ĐẶT</span>
      {user.hasPassword !== false && <div className="settings-row"><div><strong>Đổi mật khẩu</strong><p>Bảo vệ tài khoản bằng mật khẩu mới</p></div>
        <Link to="/change-password">Xem →</Link></div>}
      <div className="settings-row"><div><strong>Đăng xuất</strong><p>Quay lại trang chủ ở chế độ khách</p></div>
        <button type="button" onClick={signOut}>Đăng xuất →</button></div>
    </section>
  </div></SiteLayout>;
}
