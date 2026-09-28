import { apiRequest } from "./api";

export const authApi = {
  register(data) {
    return apiRequest("/api/auth/register", {
      method: "POST",
      body: data,
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
    });
  },

  login(data) {
    return apiRequest("/api/auth/login", {
      method: "POST",
      body: data,
    });
  },

  getMe(token) {
    return apiRequest("/api/auth/me", {
      token,
    });
  },

  forgotPassword(email) {
    return apiRequest("/api/auth/forgot-password", {
      method: "POST",
      body: { email },
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