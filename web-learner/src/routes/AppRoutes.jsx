import { useEffect } from "react";
import { BrowserRouter, Navigate, Route, Routes, useLocation } from "react-router-dom";
import HomePage from "../pages/HomePage";
import VocabularyPage from "../pages/vocabulary/VocabularyPage";
import VocabularyPracticePage from "../pages/practice/VocabularyPracticePage";
import VocabularyPracticeHistoryPage from "../pages/practice/VocabularyPracticeHistoryPage";
import LoginPage from "../pages/auth/LoginPage";
import RegisterPage from "../pages/auth/RegisterPage";
import VerifyEmailPage from "../pages/auth/VerifyEmailPage";
import ForgotPasswordPage from "../pages/auth/ForgotPasswordPage";
import ResetPasswordPage from "../pages/auth/ResetPasswordPage";
import ProfilePage from "../pages/auth/ProfilePage";
import ChangePasswordPage from "../pages/auth/ChangePasswordPage";
import ProtectedRoute from "./ProtectedRoute";
import MockHomePage from "../pages/exam/MockHomePage";
import ExamPage from "../pages/exam/ExamPage";
import ExamResultPage from "../pages/exam/ExamResultPage";
import ExamReviewPage from "../pages/exam/ExamReviewPage";

function VocabularyRoute() {
  const location = useLocation();
  const rootNavigation = location.state?.vocabularyRoot;
  useEffect(() => {
    if (rootNavigation) window.scrollTo(0, 0);
  }, [location.key, rootNavigation]);
  return <VocabularyPage key={rootNavigation ? location.key : "vocabulary"} />;
}

export default function AppRoutes() {
  return <BrowserRouter><Routes>
    <Route path="/" element={<HomePage />} />
    <Route path="/vocabulary" element={<VocabularyRoute />} />
    <Route path="/practice/vocabulary" element={<VocabularyPracticePage />} />
    <Route path="/practice/vocabulary/history" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/practice/vocabulary/history/:id" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/mock-test" element={<ProtectedRoute><MockHomePage /></ProtectedRoute>} />
    <Route path="/exam/:attemptId" element={<ProtectedRoute><ExamPage /></ProtectedRoute>} />
    <Route path="/exam/:attemptId/result" element={<ProtectedRoute><ExamResultPage /></ProtectedRoute>} />
    <Route path="/exam/:attemptId/review" element={<ProtectedRoute><ExamReviewPage /></ProtectedRoute>} />
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
