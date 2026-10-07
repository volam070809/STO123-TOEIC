import { useLayoutEffect } from "react";
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
import PartMockPage from "../pages/exam/PartMockPage";
import FixedExamPage from "../pages/exam/FixedExamPage";
import MockHistoryDetailPage from "../pages/exam/MockHistoryDetailPage";
import ExamPage from "../pages/exam/ExamPage";
import ExamResultPage from "../pages/exam/ExamResultPage";
import ExamReviewPage from "../pages/exam/ExamReviewPage";
import PlacementPage from "../pages/exam/PlacementPage";
import CoursePage from "../pages/exam/CoursePage";
import CourseListPage from "../pages/exam/CourseListPage";
import PaymentPage from "../pages/payment/PaymentPage";
import GoiHocPage from "../pages/package/GoiHocPage";
import GoiHocDetailPage from "../pages/package/GoiHocDetailPage";
import PracticeSetupPage from "../pages/practice/PracticeSetupPage";
import PracticePage from "../pages/practice/PracticePage";
import PracticeResultPage from "../pages/practice/PracticeResultPage";
import KhoaHocGoiPage from "../pages/package/KhoaHocGoiPage";

function VocabularyRoute() {
  const location = useLocation();
  const rootNavigation = location.state?.vocabularyRoot;
  return <VocabularyPage key={rootNavigation ? location.key : "vocabulary"} />;
}

function MockHistoryRoute({ kind }) {
  const location = useLocation();
  return <MockHistoryDetailPage key={location.pathname} kind={kind} />;
}

function RouteScroll() {
  const location = useLocation();
  useLayoutEffect(() => {
    if (location.hash) {
      requestAnimationFrame(() => document.getElementById(decodeURIComponent(location.hash.slice(1)))?.scrollIntoView());
    } else {
      window.scrollTo(0, 0);
    }
  }, [location.key, location.pathname, location.search, location.hash]);
  return null;
}

export default function AppRoutes() {
  return <BrowserRouter><RouteScroll /><Routes>
    <Route path="/" element={<HomePage />} />
    <Route path="/vocabulary" element={<VocabularyRoute />} />
    <Route path="/practice/vocabulary" element={<VocabularyPracticePage />} />
    <Route path="/practice/vocabulary/history" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/practice/vocabulary/history/:id" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/mock-test" element={<ProtectedRoute><MockHomePage /></ProtectedRoute>} />
    <Route path="/mock-test/parts" element={<ProtectedRoute><PartMockPage /></ProtectedRoute>} />
    <Route path="/mock-test/fixed" element={<ProtectedRoute><FixedExamPage /></ProtectedRoute>} />
    <Route path="/mock-test/history" element={<ProtectedRoute><MockHistoryRoute kind="all" /></ProtectedRoute>} />
    <Route path="/mock-test/history/random" element={<ProtectedRoute><MockHistoryRoute kind="random" /></ProtectedRoute>} />
    <Route path="/mock-test/history/part/:part" element={<ProtectedRoute><MockHistoryRoute kind="part" /></ProtectedRoute>} />
    <Route path="/placement" element={<ProtectedRoute><PlacementPage /></ProtectedRoute>} />
    <Route path="/courses" element={<ProtectedRoute><CourseListPage /></ProtectedRoute>} />
    <Route path="/courses/:courseId" element={<ProtectedRoute><CoursePage /></ProtectedRoute>} />
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
    <Route path="/goi-hoc" element={<ProtectedRoute><GoiHocPage /></ProtectedRoute>} />
    <Route path="/goi-hoc/:id" element={<ProtectedRoute><GoiHocDetailPage /></ProtectedRoute>} />
    <Route
      path="/khoa-hoc-goi/:id"
      element={
        <ProtectedRoute>
          <KhoaHocGoiPage />
        </ProtectedRoute>
      }
    />
    <Route path="/payment" element={<PaymentPage />} />
    <Route path="*" element={<Navigate to="/" replace />} />
    <Route path="/practice/toeic" element={ <ProtectedRoute> <PracticeSetupPage /> </ProtectedRoute> } />
    <Route path="/practice/toeic/:maKetQua" element={<ProtectedRoute><PracticePage /></ProtectedRoute>} />
    <Route
    path="/practice/toeic/:maKetQua/result"
    element={
        <ProtectedRoute>
            <PracticeResultPage />
        </ProtectedRoute>
    }
/>
  </Routes></BrowserRouter>;
}
