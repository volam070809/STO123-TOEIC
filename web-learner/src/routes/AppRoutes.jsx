import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import HomePage from "../pages/HomePage";
import VocabularyPage from "../pages/vocabulary/VocabularyPage";
import LoginPage from "../pages/auth/LoginPage";
import RegisterPage from "../pages/auth/RegisterPage";
import VerifyEmailPage from "../pages/auth/VerifyEmailPage";
import ForgotPasswordPage from "../pages/auth/ForgotPasswordPage";
import ResetPasswordPage from "../pages/auth/ResetPasswordPage";
import ProfilePage from "../pages/auth/ProfilePage";
import ChangePasswordPage from "../pages/auth/ChangePasswordPage";
import ProtectedRoute from "./ProtectedRoute";

export default function AppRoutes() {
  return <BrowserRouter><Routes>
    <Route path="/" element={<HomePage />} />
    <Route path="/vocabulary" element={<VocabularyPage />} />
    <Route path="/login" element={<LoginPage />} />
    <Route path="/register" element={<RegisterPage />} />
    <Route path="/verify-email" element={<VerifyEmailPage />} />
    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
    <Route path="/reset-password" element={<ResetPasswordPage />} />
    <Route path="/profile" element={<ProtectedRoute><ProfilePage /></ProtectedRoute>} />
    <Route path="/change-password" element={<ProtectedRoute><ChangePasswordPage /></ProtectedRoute>} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></BrowserRouter>;
}
