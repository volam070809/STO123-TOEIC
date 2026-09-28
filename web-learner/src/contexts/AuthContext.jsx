import { createContext, useContext, useEffect, useState } from "react";
import { authApi } from "../services/authApi";

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [token, setToken] = useState(() =>
    sessionStorage.getItem("accessToken")
  );

  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  async function loadCurrentUser(currentToken = token) {
    if (!currentToken) {
      setUser(null);
      setLoading(false);
      return;
    }

    try {
      const me = await authApi.getMe(currentToken);
      setUser(me);
    } catch (error) {
      if (error.status === 401) {
        sessionStorage.removeItem("accessToken");
        sessionStorage.removeItem("expiresAtUtc");
        setToken(null);
        setUser(null);
      }
    } finally {
      setLoading(false);
    }
  }

  async function login(email, password) {
    const result = await authApi.login({
      email,
      password,
    });

    sessionStorage.setItem("accessToken", result.token);

    if (result.expiresAtUtc) {
      sessionStorage.setItem("expiresAtUtc", result.expiresAtUtc);
    }

    setToken(result.token);

    const me = await authApi.getMe(result.token);
    setUser(me);

    return result;
  }

  function logout() {
    sessionStorage.removeItem("accessToken");
    sessionStorage.removeItem("expiresAtUtc");

    setToken(null);
    setUser(null);
  }

  useEffect(() => {
    loadCurrentUser();
  }, []);

  return (
    <AuthContext.Provider
      value={{
        token,
        user,
        loading,
        isAuthenticated: !!token && !!user,
        login,
        logout,
        loadCurrentUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error("useAuth must be used inside AuthProvider");
  }

  return context;
}