import { useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import GoogleSignIn from "../../components/auth/GoogleSignIn";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, PasswordInput, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
import { protectedReturnPath } from "../../routes/protectedReturnPath";
import { loginDestination, preservePendingAuthReturnTo, requiresEmailVerification } from "../../routes/loginDestination";
export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const returnTo = protectedReturnPath(location.state?.returnTo);
  const { login } = useAuth();
  const [email, setEmail] = useState(() => typeof location.state?.email === "string" ? location.state.email : "");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [unverified, setUnverified] = useState(false);
  const [loading, setLoading] = useState(false);
  async function handleSubmit(e) {
    e.preventDefault();
    if (loading) return;
    setError(""); setUnverified(false); setLoading(true);
    try {
      const session = await login(email.trim(), password);
      navigate(loginDestination(session.user?.vaiTro, returnTo), { replace: true });
    }
    catch (err) {
      if (err.status === 401) setError("Email hoặc mật khẩu không đúng.");
      else if (requiresEmailVerification(err)) {
        sessionStorage.setItem("pendingVerificationEmail", email.trim());
        preservePendingAuthReturnTo(returnTo, sessionStorage);
        setUnverified(true);
        setError("Tài khoản chưa xác thực email.");
      } else setError(authError(err, "Đăng nhập không thành công. Vui lòng thử lại."));
    } finally { setLoading(false); }
  }
  return <AuthLayout entry="login" title="Chào mừng bạn trở lại" subtitle="Đăng nhập để tiếp tục lộ trình TOEIC của bạn."
    footer={<>Chưa có tài khoản? <Link to="/register" state={returnTo ? { returnTo } : undefined}>Đăng ký ngay</Link></>}>
    <AlertMessage type="success">{location.state?.notice}</AlertMessage>
    <form onSubmit={handleSubmit} className="auth-form">
      <FormInput id="email" label="Email" type="email" value={email} onChange={e => setEmail(e.target.value)}
        autoComplete="email" />
      <PasswordInput id="password" label="Mật khẩu" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" />
      <div className="form-end"><Link to="/forgot-password">Quên mật khẩu?</Link></div>
      <AlertMessage>{error}{unverified && <> <Link to="/verify-email"
        state={returnTo ? { returnTo } : undefined}>Xác thực ngay</Link></>}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang đăng nhập...">Đăng nhập</LoadingButton>
    </form>
    <GoogleSignIn returnTo={returnTo} />
  </AuthLayout>;
}




