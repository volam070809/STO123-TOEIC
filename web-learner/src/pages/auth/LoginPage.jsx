import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import GoogleSignIn from "../../components/auth/GoogleSignIn";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, PasswordInput, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [unverified, setUnverified] = useState(false);
  const [loading, setLoading] = useState(false);
  async function handleSubmit(e) {
    e.preventDefault();
    if (loading) return;
    setError(""); setUnverified(false); setLoading(true);
    try { await login(email.trim(), password); navigate("/", { replace: true }); }
    catch (err) {
      if (err.status === 401) setError("Email hoặc mật khẩu không đúng.");
      else if (err.status === 403) {
        sessionStorage.setItem("pendingVerificationEmail", email.trim());
        setUnverified(true);
        setError("Tài khoản chưa xác thực email.");
      } else setError(authError(err, "Đăng nhập không thành công. Vui lòng thử lại."));
    } finally { setLoading(false); }
  }
  return <AuthLayout title="Chào mừng bạn trở lại" subtitle="Đăng nhập để tiếp tục lộ trình TOEIC của bạn."
    footer={<>Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link></>}>
    <AlertMessage type="success">{location.state?.notice}</AlertMessage>
    <form onSubmit={handleSubmit} className="auth-form">
      <FormInput id="email" label="Email" type="email" value={email} onChange={e => setEmail(e.target.value)}
        autoComplete="email" />
      <PasswordInput id="password" label="Mật khẩu" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" />
      <div className="form-end"><Link to="/forgot-password">Quên mật khẩu?</Link></div>
      <AlertMessage>{error}{unverified && <> <Link to="/verify-email">Xác thực ngay</Link></>}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang đăng nhập...">Đăng nhập</LoadingButton>
    </form>
    <GoogleSignIn />
  </AuthLayout>;
}




