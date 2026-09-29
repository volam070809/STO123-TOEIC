import { useState } from "react";
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom";
import { authApi } from "../../services/authApi";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, PasswordInput, PasswordRules, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
import { validPassword } from "../../services/passwordRules";
export default function ResetPasswordPage() {
  const email = sessionStorage.getItem("passwordResetEmail");
  const navigate = useNavigate();
  const location = useLocation();
  const [otp, setOtp] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  if (!email) return <Navigate to="/forgot-password" replace />;
  async function submit(e) {
    e.preventDefault();
    if (loading) return;
    setError("");
    if (!validPassword(newPassword)) { setError("Mật khẩu cần ít nhất 8 ký tự, một chữ cái và một chữ số."); return; }
    if (newPassword !== confirm) { setError("Xác nhận mật khẩu chưa khớp."); return; }
    setLoading(true);
    try {
      await authApi.resetPassword({ email, otp: otp.trim(), newPassword });
      sessionStorage.removeItem("passwordResetEmail");
      navigate("/login", { replace: true, state: { notice: "Đã đặt lại mật khẩu. Bạn có thể đăng nhập." } });
    } catch (err) { setError(authError(err, "Không thể đặt lại mật khẩu.")); }
    finally { setLoading(false); }
  }
  return <AuthLayout title="Đặt lại mật khẩu" subtitle="Dùng mã OTP trong email để tạo mật khẩu mới."
    footer={<Link to="/forgot-password">Gửi lại yêu cầu</Link>}>
    <AlertMessage type="success">{location.state?.notice}</AlertMessage>
    <form onSubmit={submit} className="auth-form">
      <FormInput id="email" label="Email" type="email" value={email} readOnly />
      <FormInput id="otp" label="Mã OTP" value={otp} onChange={e => setOtp(e.target.value)} autoComplete="one-time-code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} />
      <PasswordInput id="newPassword" label="Mật khẩu mới" value={newPassword} onChange={e => setNewPassword(e.target.value)} autoComplete="new-password" />
      <PasswordRules password={newPassword} />
      <PasswordInput id="confirmPassword" label="Xác nhận mật khẩu mới" value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password" />
      <AlertMessage>{error}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang đặt lại...">Đặt lại mật khẩu</LoadingButton>
    </form>
  </AuthLayout>;
}
