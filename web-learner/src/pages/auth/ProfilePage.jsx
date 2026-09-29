import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { AlertMessage } from "../../components/auth/AuthUI";
import SiteLayout from "../../layouts/SiteLayout";

export default function ProfilePage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const initials = user.hoTen?.trim().split(/\s+/).slice(-2).map(part => part[0]).join("").toUpperCase() || "HV";
  function signOut() { logout(); navigate("/", { replace: true }); }
  return <SiteLayout className="profile-page"><div className="site-container">
    <div className="page-heading"><h1>Tài khoản của tôi</h1><p>Quản lý thông tin cá nhân và bảo mật tài khoản.</p></div>
    <AlertMessage type="success">{location.state?.notice}</AlertMessage>
    <div className="profile-grid">
      <section className="profile-panel" aria-label="Thông tin cá nhân"><span className="panel-eyebrow">THÔNG TIN CÁ NHÂN</span>
        <div className="profile-identity">
          {user.anhDaiDien ? <img className="avatar" src={user.anhDaiDien} alt={"Ảnh đại diện của " + user.hoTen} />
            : <span className="avatar avatar-placeholder" aria-label="Ảnh đại diện">{initials}</span>}
          <strong>{user.hoTen}</strong>
        </div>
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


