import { Suspense, useState } from "react";
import { Link, NavLink, Outlet, useLocation } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import { preloadAdminRoute } from "../routes/adminRouteModules";
import { prefetchAdminData } from "../services/adminNavigationPrefetch";

const managementItems = [
  ["/admin", "Tổng quan", "overview", true],
  ["/admin/learners", "Học viên", "learners"],
  ["/admin/enrollments", "Đăng ký", "enrollments"],
  ["/admin/payments", "Thanh toán", "payments"],
  ["/admin/reports", "Báo cáo", "reports"],
];

const contentItems = [
  ["/admin/content", "Tổng quan nội dung", "overview", true],
  ["/admin/courses", "Khóa học & lộ trình", "courses"],
];

export default function AdminLayout() {
  const { token, user, logout } = useAuth();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const initials = user?.hoTen?.split(/\s+/).filter(Boolean).slice(-2).map(part => part[0]).join("").toUpperCase() || "AD";
  const items = user?.vaiTro === "ADMIN_QUAN_LY"
    ? [managementItems[0], ...contentItems, ...managementItems.slice(1)]
    : contentItems;
  const currentTitle = items.find(([path, , , end]) => end ? location.pathname === path : location.pathname.startsWith(path))?.[1] || "Quản trị";
  const warm = path => { Promise.all([preloadAdminRoute(path), prefetchAdminData(path, token)]).catch(() => {}); };
  const preload = path => ({ onPointerEnter: () => warm(path), onFocus: () => warm(path), onPointerDown: () => warm(path) });

  return <div className="admin-shell">
    <aside className={"admin-sidebar " + (menuOpen ? "is-open" : "")}>
      <Link className="admin-brand" to={user?.vaiTro === "ADMIN_QUAN_LY" ? "/admin" : "/admin/content"} onClick={() => setMenuOpen(false)}>
        <span className="admin-brand-mark">STO</span><span><strong>STO123</strong><small>Quản trị</small></span>
      </Link>
      <nav className="admin-nav" aria-label="Điều hướng quản trị">
        {items.map(([path, label, icon, end]) => <NavLink key={path} to={path} end={end} {...preload(path)} onClick={() => setMenuOpen(false)}
          className={({ isActive }) => "admin-nav-link " + (isActive ? "is-active" : "")}>
          <AdminNavIcon name={icon}/><span>{label}</span>
        </NavLink>)}
      </nav>
      <div className="admin-sidebar-footer">
        <Link to="/" className="admin-site-link"><HomeIcon/><span>Về trang học viên</span><ArrowIcon/></Link>
      </div>
    </aside>
    {menuOpen && <button className="admin-sidebar-backdrop" type="button" aria-label="Đóng menu" onClick={() => setMenuOpen(false)}/>}
    <div className="admin-workspace">
      <header className="admin-header">
        <button className="admin-menu-button" type="button" aria-label="Mở menu" aria-expanded={menuOpen} onClick={() => setMenuOpen(value => !value)}><MenuIcon/></button>
        <div className="admin-header-title"><strong>{currentTitle}</strong><span>STO123</span></div>
        <div className="admin-account">
          <span className="admin-avatar" aria-hidden="true">{initials}</span>
          <span className="admin-account-copy"><strong>{user?.hoTen}</strong><small>{user?.vaiTro === "ADMIN_QUAN_LY" ? "Quản lý" : "Nội dung"}</small></span>
          <button type="button" className="admin-logout" onClick={logout}>Đăng xuất</button>
        </div>
      </header>
      <main className="admin-main"><Suspense fallback={<AdminContentLoading/>}><Outlet/></Suspense></main>
    </div>
  </div>;
}

function AdminNavIcon({ name }) {
  const paths = {
    overview: <><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></>,
    learners: <><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></>,
    courses: <><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></>,
    lectures: <><path d="M4 4h16v16H4zM8 9h8M8 13h8M8 17h5"/></>,
    packages: <><path d="m21 8-9 5-9-5 9-5 9 5z"/><path d="m3 8 9 5 9-5M3 12l9 5 9-5M3 16l9 5 9-5"/></>,
    enrollments: <><path d="M9 11l3 3L22 4"/><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/></>,
    payments: <><rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20M6 15h3"/></>,
    reports: <><path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/></>,
  };
  return <svg className="admin-nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}

function MenuIcon() { return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16"/></svg>; }

function HomeIcon() { return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m3 10 9-7 9 7"/><path d="M5 9v11h14V9M9 20v-6h6v6"/></svg>; }

function ArrowIcon() { return <svg className="admin-site-link-arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>; }

function AdminContentLoading() {
  return <div className="admin-page admin-content-loading" role="status" aria-label="Đang tải nội dung quản trị">
    <div className="admin-skeleton admin-skeleton-heading"/><div className="admin-skeleton-grid"><div className="admin-skeleton"/><div className="admin-skeleton"/><div className="admin-skeleton"/></div><div className="admin-skeleton admin-skeleton-panel"/>
  </div>;
}
