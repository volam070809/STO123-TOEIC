import { lazy, Suspense, useLayoutEffect } from "react";
import { BrowserRouter, Navigate, Route, Routes, useLocation } from "react-router-dom";
import HomePage from "../pages/HomePage";
import VocabularyPage from "../pages/vocabulary/VocabularyPage";
import VocabularyPracticePage from "../pages/practice/VocabularyPracticePage";
import VocabularyPracticeHistoryPage from "../pages/practice/VocabularyPracticeHistoryPage";
import PracticePartsPage from "../pages/practice/PracticePartsPage";
import PracticePartsOverviewPage from "../pages/practice/PracticePartsOverviewPage";
import PracticeChoosePage from "../pages/practice/PracticeChoosePage";
import LoginPage from "../pages/auth/LoginPage";
import RegisterPage from "../pages/auth/RegisterPage";
import VerifyEmailPage from "../pages/auth/VerifyEmailPage";
import ForgotPasswordPage from "../pages/auth/ForgotPasswordPage";
import ResetPasswordPage from "../pages/auth/ResetPasswordPage";
import ProfilePage from "../pages/auth/ProfilePage";
import ChangePasswordPage from "../pages/auth/ChangePasswordPage";
import ProtectedRoute from "./ProtectedRoute";
import MockHomePage from "../pages/exam/MockHomePage";
import MockHistoryDetailPage from "../pages/exam/MockHistoryDetailPage";
import ExamPage from "../pages/exam/ExamPage";
import ExamResultPage from "../pages/exam/ExamResultPage";
import ExamReviewPage from "../pages/exam/ExamReviewPage";
import PlacementPage from "../pages/exam/PlacementPage";
import CoursePage from "../pages/exam/CoursePage";
import CourseListPage from "../pages/exam/CourseListPage";
import ToeicStructurePage from "../pages/toeic/ToeicStructurePage";
import { paymentIdFromSearch } from "../services/paymentFlow";
import AdminRoute from "./AdminRoute";
import { adminRouteModules } from "./adminRouteModules";
import GuestAuthPromptProvider from "../components/auth/GuestAuthPrompt";
import SessionBoundary from "../components/auth/SessionBoundary";
import { useAuth } from "../contexts/AuthState";

const AdminLayout = lazy(adminRouteModules.layout);
const AdminLearnersPage = lazy(adminRouteModules.learners);
const AdminLearnerDetailPage = lazy(adminRouteModules.learnerDetail);
const AdminLearnerCreatePage = lazy(adminRouteModules.learnerCreate);
const AdminLearnerEditPage = lazy(adminRouteModules.learnerEdit);
const AdminCoursesPage = lazy(adminRouteModules.courses);
const AdminCourseDetailPage = lazy(adminRouteModules.courseDetail);
const AdminCourseCreatePage = lazy(adminRouteModules.courseCreate);
const AdminCourseEditPage = lazy(adminRouteModules.courseEdit);
const AdminPackagesPage = lazy(adminRouteModules.packages);
const AdminPackageDetailPage = lazy(adminRouteModules.packageDetail);
const AdminPackageCreatePage = lazy(adminRouteModules.packageCreate);
const AdminPackageEditPage = lazy(adminRouteModules.packageEdit);
const AdminEnrollmentsPage = lazy(adminRouteModules.enrollments);
const AdminEnrollmentDetailPage = lazy(adminRouteModules.enrollmentDetail);
const AdminPaymentsPage = lazy(adminRouteModules.payments);
const AdminPaymentDetailPage = lazy(adminRouteModules.paymentDetail);
const AdminDashboardPage = lazy(adminRouteModules.dashboard);
const AdminReportsPage = lazy(adminRouteModules.reports);
const AdminContentDashboardPage = lazy(adminRouteModules.contentDashboard);
const AdminCurriculumPage = lazy(adminRouteModules.curriculum);
const CourseStepPage = lazy(() => import("../pages/exam/CourseStepPage"));

const MANAGEMENT_ROLES = ["ADMIN_QUAN_LY"];

function AdminShell() {
  return <Suspense fallback={<main className="admin-gate"><section className="admin-gate-card" role="status"><div className="admin-spinner" aria-hidden="true" /><h1>Đang tải trang quản trị</h1><p>Vui lòng chờ trong giây lát.</p></section></main>}>
    <AdminRoute><AdminLayout /></AdminRoute>
  </Suspense>;
}

function VocabularyRoute() {
  const location = useLocation();
  const rootNavigation = location.state?.vocabularyRoot;
  return <VocabularyPage key={rootNavigation ? location.key : "vocabulary"} />;
}

function MockHistoryRoute({ kind }) {
  const location = useLocation();
  return <MockHistoryDetailPage key={location.pathname} kind={kind} />;
}

function PracticeRoute() {
  const { search } = useLocation();
  const params = new URLSearchParams(search);
  if (params.has("attempt") || params.has("review")) {
    return <Navigate to={`/practice/parts${search}`} replace />;
  }
  return <PracticeChoosePage />;
}

function PracticePartsRoute() {
  const { search } = useLocation();
  const { isAuthenticated } = useAuth();
  const params = new URLSearchParams(search);
  if (params.has("attempt") || params.has("review"))
    return <ProtectedRoute><PracticePartsPage /></ProtectedRoute>;
  return <SessionBoundary>{isAuthenticated ? <PracticePartsPage /> : <PracticePartsOverviewPage />}</SessionBoundary>;
}

function LegacyPaymentRoute() {
  const { search } = useLocation();
  const paymentId = paymentIdFromSearch(search);
  return <Navigate to={paymentId ? `/courses?paymentId=${paymentId}` : "/courses"} replace />;
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
  return <BrowserRouter><GuestAuthPromptProvider><RouteScroll /><Routes>
    <Route path="/" element={<HomePage />} />
    <Route path="/toeic/structure" element={<ToeicStructurePage />} />
    <Route path="/vocabulary" element={<VocabularyRoute />} />
    <Route path="/practice/vocabulary" element={<VocabularyPracticePage />} />
    <Route path="/practice" element={<PracticeRoute />} />
    <Route path="/practice/parts" element={<PracticePartsRoute />} />
    <Route path="/practice/vocabulary/parts" element={<Navigate to="/practice/parts" replace />} />
    <Route path="/practice/vocabulary/history" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/practice/vocabulary/history/:id" element={<ProtectedRoute><VocabularyPracticeHistoryPage /></ProtectedRoute>} />
    <Route path="/mock-test" element={<MockHomePage />} />
    <Route path="/mock-test/parts" element={<Navigate to="/mock-test" replace />} />
    <Route path="/mock-test/fixed" element={<Navigate to="/mock-test" replace />} />
    <Route path="/mock-test/history" element={<ProtectedRoute><MockHistoryRoute kind="all" /></ProtectedRoute>} />
    <Route path="/mock-test/history/random" element={<Navigate to="/mock-test/history" replace />} />
    <Route path="/mock-test/history/part/:part" element={<Navigate to="/mock-test/history" replace />} />
    <Route path="/placement" element={<PlacementPage />} />
    <Route path="/courses" element={<CourseListPage />} />
    <Route path="/courses/:courseId" element={<CoursePage />} />
    <Route path="/courses/:courseId/steps/:stepId" element={<ProtectedRoute><CourseStepPage /></ProtectedRoute>} />
    <Route path="/packages" element={<Navigate to="/courses" replace />} />
    <Route path="/packages/:id" element={<Navigate to="/courses" replace />} />
    <Route path="/payment/result" element={<ProtectedRoute><LegacyPaymentRoute /></ProtectedRoute>} />
    <Route path="/payment/history" element={<Navigate to="/courses" replace />} />
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
    <Route path="/admin" element={<AdminShell />}>
      <Route index element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminDashboardPage /></AdminRoute>} />
      <Route path="reports" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminReportsPage /></AdminRoute>} />
      <Route path="learners" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminLearnersPage /></AdminRoute>} />
      <Route path="learners/new" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminLearnerCreatePage /></AdminRoute>} />
      <Route path="learners/:learnerId/edit" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminLearnerEditPage /></AdminRoute>} />
      <Route path="learners/:learnerId" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminLearnerDetailPage /></AdminRoute>} />
      <Route path="courses" element={<AdminCoursesPage />} />
      <Route path="courses/new" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminCourseCreatePage /></AdminRoute>} />
      <Route path="courses/:courseId/edit" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminCourseEditPage /></AdminRoute>} />
      <Route path="courses/:courseId" element={<AdminCourseDetailPage />} />
      <Route path="courses/:courseId/content" element={<AdminCurriculumPage />} />
      <Route path="content" element={<AdminContentDashboardPage />} />
      <Route path="packages" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPackagesPage /></AdminRoute>} />
      <Route path="packages/new" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPackageCreatePage /></AdminRoute>} />
      <Route path="packages/:packageId/edit" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPackageEditPage /></AdminRoute>} />
      <Route path="packages/:packageId" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPackageDetailPage /></AdminRoute>} />
      <Route path="enrollments" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminEnrollmentsPage /></AdminRoute>} />
      <Route path="enrollments/:enrollmentId" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminEnrollmentDetailPage /></AdminRoute>} />
      <Route path="payments" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPaymentsPage /></AdminRoute>} />
      <Route path="payments/:paymentId" element={<AdminRoute allowedRoles={MANAGEMENT_ROLES}><AdminPaymentDetailPage /></AdminRoute>} />
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></GuestAuthPromptProvider></BrowserRouter>;
}
