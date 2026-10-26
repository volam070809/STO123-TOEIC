import { adminAnalyticsApi } from "./adminAnalyticsApi";
import { adminCourseApi } from "./adminCourseApi";
import { adminEnrollmentApi } from "./adminEnrollmentApi";
import { adminLearnerApi } from "./adminLearnerApi";
import { adminPackageApi } from "./adminPackageApi";
import { adminPaymentApi } from "./adminPaymentApi";

const firstPage = { search: "", status: "", page: 1, pageSize: 20 };

export function prefetchAdminData(path, token) {
  if (!token) return Promise.resolve();
  const pathname = path.split("?")[0];
  if (pathname === "/admin") return adminAnalyticsApi.dashboard({}, token);
  if (pathname === "/admin/reports") return adminAnalyticsApi.report("overview", {}, token);
  if (pathname === "/admin/learners") return adminLearnerApi.list(firstPage, token);
  if (pathname === "/admin/courses") return adminCourseApi.list({ ...firstPage, stage: "" }, token);
  if (pathname === "/admin/packages") return adminPackageApi.list({ ...firstPage, courseId: "" }, token);
  if (pathname === "/admin/enrollments") return adminEnrollmentApi.list({ ...firstPage, packageId: "", courseId: "", dateFrom: "", dateTo: "" }, token);
  if (pathname === "/admin/payments") return adminPaymentApi.learners({ ...firstPage, gateway: "", dateFrom: "", dateTo: "" }, token);
  return Promise.resolve();
}
