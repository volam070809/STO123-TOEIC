import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "../contexts/AuthState";
import { protectedReturnPath } from "./protectedReturnPath";
export default function ProtectedRoute({ children }) {
  const location = useLocation();
  const { token, isAuthenticated, loading, sessionError, loadCurrentUser } = useAuth();
  if (loading) return <div className="route-loading" role="status">Đang kiểm tra phiên đăng nhập...</div>;
  if (token && sessionError) return <div className="route-loading"><div className="retry-panel" role="alert">
    <p>{sessionError}</p><button className="primary-button" type="button" onClick={() => loadCurrentUser()}>Thử lại</button>
  </div></div>;
  if (!isAuthenticated) return <Navigate to="/login" replace
    state={{ returnTo: protectedReturnPath(location.pathname) }} />;
  return children;
}

