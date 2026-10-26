export const adminRouteModules = {
  layout: () => import("../layouts/AdminLayout"),
  dashboard: () => import("../pages/admin/AdminDashboardPage"),
  reports: () => import("../pages/admin/AdminReportsPage"),
  learners: () => import("../pages/admin/AdminLearnersPage"),
  learnerDetail: () => import("../pages/admin/AdminLearnerDetailPage"),
  learnerCreate: () => import("../pages/admin/AdminLearnerCreatePage"),
  learnerEdit: () => import("../pages/admin/AdminLearnerEditPage"),
  courses: () => import("../pages/admin/AdminCoursesPage"),
  courseDetail: () => import("../pages/admin/AdminCourseDetailPage"),
  courseCreate: () => import("../pages/admin/AdminCourseCreatePage"),
  courseEdit: () => import("../pages/admin/AdminCourseEditPage"),
  packages: () => import("../pages/admin/AdminPackagesPage"),
  packageDetail: () => import("../pages/admin/AdminPackageDetailPage"),
  packageCreate: () => import("../pages/admin/AdminPackageCreatePage"),
  packageEdit: () => import("../pages/admin/AdminPackageEditPage"),
  enrollments: () => import("../pages/admin/AdminEnrollmentsPage"),
  enrollmentDetail: () => import("../pages/admin/AdminEnrollmentDetailPage"),
  payments: () => import("../pages/admin/AdminPaymentsPage"),
  paymentDetail: () => import("../pages/admin/AdminPaymentDetailPage"),
};

const preloaded = new Map();

export function preloadAdminRoute(path) {
  const key = routeKey(path);
  if (!key) return Promise.resolve();
  if (!preloaded.has(key)) {
    const request = adminRouteModules[key]().catch(error => {
      preloaded.delete(key);
      throw error;
    });
    preloaded.set(key, request);
  }
  return preloaded.get(key);
}

export function routeKey(path) {
  const pathname = path.split("?")[0];
  if (pathname === "/admin") return "dashboard";
  if (pathname === "/admin/reports") return "reports";
  if (pathname === "/admin/learners") return "learners";
  if (pathname === "/admin/learners/new") return "learnerCreate";
  if (/^\/admin\/learners\/\d+\/edit$/.test(pathname)) return "learnerEdit";
  if (/^\/admin\/learners\/\d+$/.test(pathname)) return "learnerDetail";
  if (pathname === "/admin/courses") return "courses";
  if (pathname === "/admin/courses/new") return "courseCreate";
  if (/^\/admin\/courses\/\d+\/edit$/.test(pathname)) return "courseEdit";
  if (/^\/admin\/courses\/\d+$/.test(pathname)) return "courseDetail";
  if (pathname === "/admin/packages") return "packages";
  if (pathname === "/admin/packages/new") return "packageCreate";
  if (/^\/admin\/packages\/\d+\/edit$/.test(pathname)) return "packageEdit";
  if (/^\/admin\/packages\/\d+$/.test(pathname)) return "packageDetail";
  if (pathname === "/admin/enrollments") return "enrollments";
  if (/^\/admin\/enrollments\/\d+$/.test(pathname)) return "enrollmentDetail";
  if (pathname === "/admin/payments") return "payments";
  if (/^\/admin\/payments\/\d+$/.test(pathname)) return "paymentDetail";
  return null;
}
