import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";

export function BrandLogo() {
  return <span className="site-logo" aria-label="STO123">
    <span className="site-logo-icon">T</span><span>STO</span><span className="site-logo-numbers">123</span>
  </span>;
}

export default function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false);
  const { token, user, loading, sessionError, logout } = useAuth();
  const navigate = useNavigate();
  const initials = user?.hoTen?.trim().split(/\s+/).slice(-2).map(part => part[0]).join("").toUpperCase() || "HV";
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
        <Link className="site-nav-active" to="/" onClick={() => setMenuOpen(false)}>Trang chủ</Link>
        <span>Lộ trình</span><span>Bài giảng</span>
        <Link to="/vocabulary" state={{ vocabularyRoot: true }} onClick={() => setMenuOpen(false)}>Từ vựng</Link>
        <Link to="/practice/vocabulary" onClick={() => setMenuOpen(false)}>Luyện tập</Link>
        {user && <Link to="/placement" onClick={() => setMenuOpen(false)}>Phân lớp</Link>}
        <Link to="/mock-test" onClick={() => setMenuOpen(false)}>Thi thử</Link>
      </nav>
      <div className={"site-account" + (menuOpen ? " is-open" : "")}>
        {loading ? <span className="site-account-loading">Đang tải...</span> : token && sessionError ? <span className="site-account-loading">Không thể tải tài khoản</span> : user ? <>
          <span className="site-account-identity"><span className="site-account-avatar">{initials}</span><span>{user.hoTen}</span></span>
          <Link to="/profile" onClick={() => setMenuOpen(false)}>Hồ sơ</Link>
          <button type="button" onClick={signOut}>Đăng xuất</button>
        </> : <>
          <Link to="/login" onClick={() => setMenuOpen(false)}>Đăng nhập</Link>
          <Link className="site-account-register" to="/register" onClick={() => setMenuOpen(false)}>Đăng ký</Link>
        </>}
      </div>
    </div>
  </header>;
}
