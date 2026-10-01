import { useEffect, useRef, useState } from "react";
import { authApi } from "../services/authApi";
import { AuthContext } from "./AuthState";
import { disableGoogleAutoSelect } from "../services/googleIdentity";

const ACCOUNT_LOAD_ERROR = "Không thể tải tài khoản. Vui lòng thử lại.";

export function AuthProvider({ children }) {
  const [token, setToken] = useState(() => sessionStorage.getItem("accessToken"));
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(() => !!sessionStorage.getItem("accessToken"));
  const [sessionError, setSessionError] = useState("");
  const tokenRef = useRef(sessionStorage.getItem("accessToken"));
  const generationRef = useRef(0);
  const startupRequestedRef = useRef(false);

  function clearSession() {
    generationRef.current += 1;
    tokenRef.current = null;
    sessionStorage.removeItem("accessToken");
    sessionStorage.removeItem("expiresAtUtc");
    setToken(null);
    setUser(null);
    setSessionError("");
    setLoading(false);
  }

  async function loadCurrentUser(currentToken = tokenRef.current) {
    if (!currentToken || currentToken !== tokenRef.current) return;
    const generation = ++generationRef.current;
    setLoading(true);
    setSessionError("");
    try {
      const currentUser = await authApi.getMe(currentToken);
      if (generation === generationRef.current && currentToken === tokenRef.current) setUser(currentUser);
    } catch (error) {
      if (generation !== generationRef.current || currentToken !== tokenRef.current) return;
      if (error.status === 401) clearSession();
      else {
        setUser(null);
        setSessionError(error.network ? error.message : ACCOUNT_LOAD_ERROR);
      }
    } finally {
      if (generation === generationRef.current) setLoading(false);
    }
  }

  async function completeLogin(result, generation) {
    if (generation !== generationRef.current) throw new Error("Đăng nhập đã bị hủy.");
    tokenRef.current = result.token;
    sessionStorage.setItem("accessToken", result.token);
    if (result.expiresAtUtc) sessionStorage.setItem("expiresAtUtc", result.expiresAtUtc);
    else sessionStorage.removeItem("expiresAtUtc");
    setToken(result.token);
    setUser(null);
    setLoading(true);
    setSessionError("");
    try {
      const currentUser = await authApi.getMe(result.token);
      if (generation === generationRef.current && tokenRef.current === result.token) setUser(currentUser);
    } catch (error) {
      if (generation === generationRef.current && tokenRef.current === result.token) {
        if (error.status === 401) clearSession();
        else setSessionError(error.network ? error.message : ACCOUNT_LOAD_ERROR);
      }
      throw error;
    } finally {
      if (generation === generationRef.current) setLoading(false);
    }
    return result;
  }

  async function login(email, password) {
    const generation = ++generationRef.current;
    return completeLogin(await authApi.login({ email, password }), generation);
  }

  async function loginWithGoogle(idToken) {
    const generation = ++generationRef.current;
    return completeLogin(await authApi.googleLogin(idToken), generation);
  }

  function logout() {
    disableGoogleAutoSelect();
    clearSession();
    sessionStorage.removeItem("pendingVerificationEmail");
    sessionStorage.removeItem("passwordResetEmail");
  }

  async function renewToken() {
    const current = tokenRef.current;
    if (!current) throw new Error("Phiên đăng nhập đã kết thúc.");
    const result = await authApi.renew(current);
    if (tokenRef.current !== current) return;
    tokenRef.current = result.token;
    sessionStorage.setItem("accessToken", result.token);
    sessionStorage.setItem("expiresAtUtc", result.expiresAtUtc);
    setToken(result.token);
    return result.token;
  }

  useEffect(() => {
    if (!startupRequestedRef.current && tokenRef.current) {
      startupRequestedRef.current = true;
      loadCurrentUser(tokenRef.current);
    }
    const expire = event => {
      if (event.detail?.token === tokenRef.current) clearSession();
    };
    window.addEventListener("auth:session-expired", expire);
    return () => window.removeEventListener("auth:session-expired", expire);
  }, []);

  return <AuthContext.Provider value={{
    token, user, loading, sessionError, isAuthenticated: !!token && !!user,
    login, loginWithGoogle, logout, loadCurrentUser, renewToken,
  }}>{children}</AuthContext.Provider>;
}
