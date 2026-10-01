import { apiRequest } from "./api";

export const authApi = {
  register(data) {
    return apiRequest("/api/auth/register", {
      method: "POST",
      body: data,
      timeoutMs: 120_000,
    });
  },

  verifyEmail(data) {
    return apiRequest("/api/auth/verify-email", {
      method: "POST",
      body: data,
    });
  },

  resendOtp(email) {
    return apiRequest("/api/auth/resend-otp", {
      method: "POST",
      body: { email },
      timeoutMs: 90_000,
    });
  },

  login(data) {
    return apiRequest("/api/auth/login", {
      method: "POST",
      body: data,
    });
  },

  googleLogin(idToken) {
    return apiRequest("/api/auth/google", { method: "POST", body: { idToken }, timeoutMs: 60_000 });
  },

  getMe(token) {
    return apiRequest("/api/auth/me", {
      token,
    });
  },

  renew(token) {
    return apiRequest("/api/auth/renew", { method: "POST", token });
  },

  forgotPassword(email) {
    return apiRequest("/api/auth/forgot-password", {
      method: "POST",
      body: { email },
      timeoutMs: 90_000,
    });
  },

  resetPassword(data) {
    return apiRequest("/api/auth/reset-password", {
      method: "POST",
      body: data,
    });
  },

  changePassword(data, token) {
    return apiRequest("/api/auth/change-password", {
      method: "POST",
      body: data,
      token,
    });
  },
};
