import { apiRequest } from "./api";

export const paymentApi = {
  quote: (courseIds, token) => apiRequest("/api/payment/quote", {
    method: "POST", body: { courseIds, expectedTotal: 0 }, token,
  }),
  createCourses: (courseIds, expectedTotal, token) => apiRequest("/api/payment/create-courses", {
    method: "POST", body: { courseIds, expectedTotal }, token,
  }),
  create: (packageId, token) => apiRequest("/api/payment/create", {
    method: "POST", body: { maGoiHoc: packageId }, token,
  }),
  status: (paymentId, token) => apiRequest(`/api/payment/status/${paymentId}`,
    { token, cache: "no-store" }),
  history: token => apiRequest("/api/payment/history", { token, cache: "no-store" }),
};
