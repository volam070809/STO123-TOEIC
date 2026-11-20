import { apiRequest } from "./api";

function singleCourseIds(courseId) {
  if (!Number.isSafeInteger(courseId) || courseId <= 0) throw new TypeError("Invalid course ID");
  return [courseId];
}

export const paymentApi = {
  quoteCourse: (courseId, token) => apiRequest("/api/payment/quote", {
    method: "POST", body: { courseIds: singleCourseIds(courseId), expectedTotal: 0 }, token,
  }),
  createCourse: (courseId, expectedTotal, token) => apiRequest("/api/payment/create-courses", {
    method: "POST", body: { courseIds: singleCourseIds(courseId), expectedTotal }, token,
  }),
  create: (packageId, token) => apiRequest("/api/payment/create", {
    method: "POST", body: { maGoiHoc: packageId }, token,
  }),
  status: (paymentId, token) => apiRequest(`/api/payment/status/${paymentId}`,
    { token, cache: "no-store" }),
  history: token => apiRequest("/api/payment/history", { token, cache: "no-store" }),
};
