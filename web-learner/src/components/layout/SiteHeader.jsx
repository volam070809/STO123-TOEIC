import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import Avatar from "../auth/Avatar";

export function BrandLogo() {
  return <span className="site-logo" aria-label="STO123">
    <span className="site-logo-icon">T</span><span>STO</span><span className="site-logo-numbers">123</span>
  </span>;
}

export default function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const { token, user, loading, sessionError, logout } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  function signOut() {
    logout();
    setMenuOpen(false);
    navigate("/", { replace: true });
  }
  return <header className="site-header">
    <div className="site-header-inner">
      <Link to="/" className="site-logo-link" onClick={() => setMenuOpen(false)}><BrandLogo /></Link>
      <button type="button" className="site-menu-toggle" aria-label={menuOpen ? "Đóng menu" : "Mở menu"}
        aria-expanded={menuOpen} aria-controls="site-navigation" onClick={() => setMenuOpen(!menuOpen)}>
        {menuOpen ? "Đóng" : "Menu"} <span aria-hidden="true">{menuOpen ? "×" : "☰"}</span>
      </button>
      <nav id="site-navigation" className={"site-navigation" + (menuOpen ? " is-open" : "")} aria-label="Điều hướng chính">
        <Link className={pathname === "/" ? "site-nav-active" : undefined} to="/" onClick={() => setMenuOpen(false)}>Trang chủ</Link>
        <Link className={pathname.startsWith("/courses") ? "site-nav-active" : undefined} to="/courses" onClick={() => setMenuOpen(false)}>Khóa học</Link>
        <Link className={pathname === "/vocabulary" ? "site-nav-active" : undefined} to="/vocabulary" state={{ vocabularyRoot: true }} onClick={() => setMenuOpen(false)}>Từ vựng</Link>
        <Link className={pathname.startsWith("/practice") ? "site-nav-active" : undefined} to="/practice" onClick={() => setMenuOpen(false)}>Luyện tập</Link>
        {user && <Link className={pathname === "/placement" ? "site-nav-active" : undefined} to="/placement" onClick={() => setMenuOpen(false)}>Lộ trình học</Link>}
        <Link className={pathname.startsWith("/mock-test") ? "site-nav-active" : undefined} to="/mock-test" onClick={() => setMenuOpen(false)}>Thi thử</Link>
      </nav>
      <div className={"site-account" + (menuOpen ? " is-open" : "")}>
        {loading ? <span className="site-account-loading">Đang tải...</span> : token && sessionError ? <span className="site-account-loading">Không thể tải tài khoản</span> : user ? <>
          <span className="site-account-identity"><Avatar user={user} /><span>{user.hoTen}</span></span>
          <Link className={pathname === "/profile" || pathname === "/change-password" ? "site-nav-active" : undefined}
            to="/profile" onClick={() => setMenuOpen(false)}>Hồ sơ</Link>
          <button type="button" data-exam-logout onClick={signOut}>Đăng xuất</button>
        </> : <>
          <Link to="/login" onClick={() => setMenuOpen(false)}>Đăng nhập</Link>
          <Link className="site-account-register" to="/register" onClick={() => setMenuOpen(false)}>Đăng ký</Link>
        </>}
      </div>
    </div>
  </header>;
}
