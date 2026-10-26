import { Link, Navigate, useLocation } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import { protectedReturnPath } from "./protectedReturnPath";

const ADMIN_ROLES = ["ADMIN_QUAN_LY", "ADMIN_NOI_DUNG"];

export default function AdminRoute({ children, allowedRoles = ADMIN_ROLES }) {
  const location = useLocation();
  const { token, user, isAuthenticated, loading, sessionError, loadCurrentUser, logout } = useAuth();

  if (loading) return <AdminState title="Đang kiểm tra quyền truy cập" detail="Vui lòng chờ trong giây lát." busy />;
  if (token && sessionError) return <AdminState title="Không thể kiểm tra phiên đăng nhập" detail={sessionError}
    action={<button type="button" className="admin-button" onClick={() => loadCurrentUser()}>Thử lại</button>} />;
  if (!isAuthenticated) {
    const returnTo = protectedReturnPath(location.pathname + location.search) ?? "/admin";
    return <Navigate to="/login" replace state={{ returnTo }} />;
  }
  if (!allowedRoles.includes(user?.vaiTro)) {
    return <AdminState title="Bạn không có quyền truy cập" detail="Tài khoản hiện tại không có quyền sử dụng khu vực quản trị này."
      action={<div className="admin-state-actions"><Link className="admin-button admin-button-secondary" to="/">Về trang học viên</Link>
        <button type="button" className="admin-button" onClick={logout}>Đăng xuất</button></div>} />;
  }
  return children;
}

function AdminState({ title, detail, action, busy = false }) {
  return <main className="admin-gate"><section className="admin-gate-card" role={busy ? "status" : "alert"}>
    <div className={busy ? "admin-spinner" : "admin-gate-icon"} aria-hidden="true">{busy ? "" : "!"}</div>
    <h1>{title}</h1><p>{detail}</p>{action}
  </section></main>;
}
