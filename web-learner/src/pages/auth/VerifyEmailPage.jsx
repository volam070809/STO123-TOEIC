import { useEffect, useState } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom";
import { authApi } from "../../services/authApi";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
import { protectedReturnPath } from "../../routes/protectedReturnPath";
export default function VerifyEmailPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [email] = useState(() => sessionStorage.getItem("pendingVerificationEmail"));
  const returnTo = protectedReturnPath(location.state?.returnTo ?? sessionStorage.getItem("pendingAuthReturnTo"));
  const [otp, setOtp] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [loading, setLoading] = useState(false);
  const [resending, setResending] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(cooldown - 1), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);
  if (!email) return <Navigate to="/register" replace state={returnTo ? { returnTo } : undefined} />;
  async function verify(e) {
    e.preventDefault();
    if (loading) return;
    setError(""); setLoading(true);
    try {
      await authApi.verifyEmail({ email, otp: otp.trim() });
      sessionStorage.removeItem("pendingVerificationEmail");
      sessionStorage.removeItem("pendingAuthReturnTo");
      navigate("/login", { replace: true, state: {
        notice: "Email đã được xác thực. Bạn có thể đăng nhập.", email,
        ...(returnTo ? { returnTo } : {}),
      } });
    } catch (err) { setError(authError(err, "Không thể xác thực email.")); }
    finally { setLoading(false); }
  }
  async function resend() {
    if (resending || cooldown) return;
    setError(""); setNotice(""); setResending(true);
    try {
      await authApi.resendOtp(email);
      setNotice("Nếu tài khoản cần xác thực, mã OTP đã được gửi. Vui lòng kiểm tra email.");
      setCooldown(60);
    } catch (err) { setError(authError(err, "Không thể gửi lại mã OTP.")); }
    finally { setResending(false); }
  }
  return <AuthLayout title="Xác thực email" subtitle="Nhập mã OTP được gửi đến email của bạn."
    footer={<>Đã xác thực? <Link to="/login" state={returnTo ? { returnTo } : undefined}>Đăng nhập</Link></>}>
    <form onSubmit={verify} className="auth-form">
      <FormInput id="email" label="Email xác thực" type="email" value={email} readOnly />
      <FormInput id="otp" label="Mã OTP" value={otp} onChange={e => setOtp(e.target.value)} autoComplete="one-time-code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} />
      <AlertMessage>{error}</AlertMessage><AlertMessage type="success">{notice}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang xác thực...">Xác thực email</LoadingButton>
    </form>
    <div className="secondary-action">Chưa nhận được mã? <button type="button" disabled={resending || cooldown > 0} onClick={resend}>
      {resending ? "Đang gửi..." : cooldown ? "Gửi lại sau " + cooldown + " giây" : "Gửi lại OTP"}</button></div>
  </AuthLayout>;
}
