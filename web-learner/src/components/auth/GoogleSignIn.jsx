import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { authError } from "../../services/authErrors";
import { clearGoogleSignInHandler, renderGoogleSignIn } from "../../services/googleIdentity";
import { AlertMessage } from "./AuthUI";

const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID?.trim();

export default function GoogleSignIn() {
  const buttonRef = useRef(null);
  const busyRef = useRef(false);
  const handlerRef = useRef(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const navigate = useNavigate();
  const { loginWithGoogle } = useAuth();

  async function handleCredential(idToken) {
    if (!idToken || busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setError("");
    try {
      await loginWithGoogle(idToken);
      navigate("/", { replace: true });
    } catch (failure) {
      if (failure.status === 409) setError("Email này đã có tài khoản STO123. Vui lòng đăng nhập bằng phương thức ban đầu.");
      else if (failure.status === 401) setError("Phiên đăng nhập Google không hợp lệ. Vui lòng thử lại.");
      else if (failure.status === 403) setError("Email Google chưa được xác thực hoặc tài khoản chưa hoạt động.");
      else if (failure.status === 503) setError("Đăng nhập Google hiện chưa sẵn sàng. Vui lòng thử lại sau.");
      else setError(authError(failure, "Không thể đăng nhập bằng Google."));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }
  useEffect(() => { handlerRef.current = handleCredential; });

  useEffect(() => {
    if (!clientId || !buttonRef.current) return;
    let active = true;
    const handler = credential => { if (active) handlerRef.current?.(credential); };
    renderGoogleSignIn(buttonRef.current, clientId, handler).catch(() => {
      if (active) setError("Không thể tải đăng nhập Google. Vui lòng thử lại sau.");
    });
    return () => { active = false; clearGoogleSignInHandler(handler); };
  }, []);

  return <div className="google-sign-in">
    <span className="google-divider">hoặc</span>
    {clientId ? <div className={busy ? "google-button-wrap is-busy" : "google-button-wrap"} ref={buttonRef} aria-label="Đăng nhập bằng Google" />
      : <p className="google-setup-note">Đăng nhập bằng Google cần cấu hình mã ứng dụng.</p>}
    {busy && <p role="status">Đang đăng nhập bằng Google...</p>}
    <AlertMessage>{error}</AlertMessage>
  </div>;
}


