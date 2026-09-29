import { useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { authApi } from "../../services/authApi";
import { authError } from "../../services/authErrors";
import { AuthLayout, PasswordInput, PasswordRules, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
import { validPassword } from "../../services/passwordRules";
export default function ChangePasswordPage() {
  const navigate = useNavigate();
  const { token, user } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  async function submit(e) {
    e.preventDefault();
    if (loading) return;
    setError("");
    if (!validPassword(newPassword)) { setError("Mật khẩu mới cần ít nhất 8 ký tự, một chữ cái và một chữ số."); return; }
    if (newPassword !== confirm) { setError("Xác nhận mật khẩu chưa khớp."); return; }
    setLoading(true);
    try {
      await authApi.changePassword({ currentPassword, newPassword }, token);
      navigate("/profile", { replace: true, state: { notice: "Đổi mật khẩu thành công." } });
    } catch (err) { setError(authError(err, "Không thể đổi mật khẩu.")); }
    finally { setLoading(false); }
  }
  if (user?.hasPassword === false) return <Navigate to="/profile" replace />;
  return <AuthLayout title="Đổi mật khẩu" subtitle="Cập nhật mật khẩu để giữ tài khoản an toàn."
    footer={<Link to="/profile">Quay lại hồ sơ</Link>}>
    <form onSubmit={submit} className="auth-form">
      <PasswordInput id="currentPassword" label="Mật khẩu hiện tại" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} autoComplete="current-password" />
      <PasswordInput id="newPassword" label="Mật khẩu mới" value={newPassword} onChange={e => setNewPassword(e.target.value)} autoComplete="new-password" />
      <PasswordRules password={newPassword} />
      <PasswordInput id="confirmPassword" label="Xác nhận mật khẩu mới" value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password" />
      <AlertMessage>{error}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang cập nhật...">Đổi mật khẩu</LoadingButton>
    </form>
  </AuthLayout>;
}


