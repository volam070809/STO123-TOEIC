import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { authApi } from "../../services/authApi";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, AlertMessage, LoadingButton } from "../../components/auth/AuthUI";
export default function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  async function submit(e) {
    e.preventDefault();
    if (loading) return;
    setError(""); setLoading(true);
    const normalized = email.trim().toLowerCase();
    try {
      await authApi.forgotPassword(normalized);
      sessionStorage.setItem("passwordResetEmail", normalized);
      navigate("/reset-password", { replace: true, state: { notice: "Nếu email tồn tại, mã đặt lại mật khẩu đã được gửi. Vui lòng kiểm tra hộp thư." } });
    } catch (err) { setError(authError(err, "Không thể gửi yêu cầu. Vui lòng thử lại.")); }
    finally { setLoading(false); }
  }
  return <AuthLayout title="Quên mật khẩu" subtitle="Nhập email để nhận mã đặt lại mật khẩu."
    footer={<Link to="/login">Quay lại đăng nhập</Link>}>
    <form onSubmit={submit} className="auth-form">
      <FormInput id="email" label="Email" type="email" value={email} onChange={e => setEmail(e.target.value)} autoComplete="email" />
      <AlertMessage>{error}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang gửi yêu cầu...">Gửi mã đặt lại</LoadingButton>
    </form>
  </AuthLayout>;
}

