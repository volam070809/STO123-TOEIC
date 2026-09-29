import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { authApi } from "../../services/authApi";
import GoogleSignIn from "../../components/auth/GoogleSignIn";
import { authError } from "../../services/authErrors";
import { AuthLayout, FormInput, PasswordInput, AlertMessage, LoadingButton, PasswordRules } from "../../components/auth/AuthUI";
import { validPassword } from "../../services/passwordRules";
export default function RegisterPage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ hoTen: "", email: "", soDienThoai: "", password: "", confirmPassword: "" });
  const [error, setError] = useState("");
  const [registrationMayHaveCompleted, setRegistrationMayHaveCompleted] = useState(false);
  const [loading, setLoading] = useState(false);
  const change = key => e => setForm({ ...form, [key]: e.target.value });
  async function submit(e) {
    e.preventDefault();
    if (loading) return;
    setError(""); setRegistrationMayHaveCompleted(false);
    if (!validPassword(form.password)) { setError("Mật khẩu cần ít nhất 8 ký tự, một chữ cái và một chữ số."); return; }
    if (form.password !== form.confirmPassword) { setError("Xác nhận mật khẩu chưa khớp."); return; }
    setLoading(true);
    const email = form.email.trim().toLowerCase();
    try {
      await authApi.register({ hoTen: form.hoTen.trim(), email, soDienThoai: form.soDienThoai.trim(), password: form.password });
      sessionStorage.setItem("pendingVerificationEmail", email);
      navigate("/verify-email", { replace: true });
    } catch (err) {
      if (err.status === 500 && err.data?.message?.startsWith("Account created, but")) {
        sessionStorage.setItem("pendingVerificationEmail", email);
        setRegistrationMayHaveCompleted(true);
        setError("Tài khoản đã được tạo, nhưng email xác thực chưa gửi được. Hãy thử gửi lại mã OTP.");
      } else if (err.timeout) {
        sessionStorage.setItem("pendingVerificationEmail", email);
        setRegistrationMayHaveCompleted(true);
        setError("Yêu cầu đã hết thời gian chờ. Tài khoản có thể đã được tạo. Hãy kiểm tra email hoặc thử gửi lại mã xác thực trước khi đăng ký lại.");
      } else setError(authError(err, "Không thể đăng ký. Vui lòng thử lại."));
    } finally { setLoading(false); }
  }
  return <AuthLayout title="Tạo tài khoản" subtitle="Bắt đầu luyện tập TOEIC cùng STO123." wide
    footer={<>Đã có tài khoản? <Link to="/login">Đăng nhập</Link></>}>
    <form onSubmit={submit} className="auth-form">
      <FormInput id="hoTen" label="Họ tên" value={form.hoTen} onChange={change("hoTen")} autoComplete="name" maxLength={64} />
      <FormInput id="email" label="Email" type="email" value={form.email} onChange={change("email")} autoComplete="email" />
      <FormInput id="soDienThoai" label="Số điện thoại" type="tel" value={form.soDienThoai} onChange={change("soDienThoai")} autoComplete="tel" required={false} maxLength={16} />
      <PasswordInput id="password" label="Mật khẩu" value={form.password} onChange={change("password")} autoComplete="new-password" />
      <PasswordRules password={form.password} />
      <PasswordInput id="confirmPassword" label="Xác nhận mật khẩu" value={form.confirmPassword} onChange={change("confirmPassword")} autoComplete="new-password" />
      <AlertMessage>{error}{registrationMayHaveCompleted && <> <Link to="/verify-email">Đến trang xác thực</Link></>}</AlertMessage>
      <LoadingButton loading={loading} busyText="Đang tạo tài khoản...">Đăng ký</LoadingButton>
    </form>
    <GoogleSignIn />
  </AuthLayout>;
}



