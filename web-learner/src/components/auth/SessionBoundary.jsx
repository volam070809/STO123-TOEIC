import { useAuth } from "../../contexts/AuthState";

// Public pages must wait for a stored session to resolve before showing guest copy.
export default function SessionBoundary({ children }) {
  const { token, loading, sessionError, loadCurrentUser, logout } = useAuth();

  if (loading) return <div className="site-container"><div className="route-loading" role="status">Đang kiểm tra phiên đăng nhập...</div></div>;
  if (token && sessionError) return <div className="site-container"><div className="route-loading">
    <div className="retry-panel" role="alert"><p>{sessionError}</p>
      <div className="action-row"><button className="primary-button" type="button" onClick={() => loadCurrentUser()}>Thử lại</button>
        <button className="outline-button" type="button" onClick={logout}>Tiếp tục với tư cách khách</button></div>
    </div>
  </div></div>;
  return children;
}
