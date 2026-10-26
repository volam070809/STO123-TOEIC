import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";
import { adminLearnerListPath } from "./adminListPaths";

const shareRead = createSharedRequest({ ttlMs: 10_000 });

export const adminLearnerApi = {
  list(filters, token) {
    const endpoint = adminLearnerListPath(filters);
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  detail(learnerId, token) {
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  overview(learnerId, token) {
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}/analytics/overview`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  vocabulary(learnerId, token) {
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}/analytics/vocabulary`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  practice(learnerId, filters, token) {
    const params = new URLSearchParams({ page: String(filters.page), pageSize: String(filters.pageSize) });
    if (filters.part) params.set("part", String(filters.part));
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}/analytics/practice?${params}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  exams(learnerId, filters, token) {
    const params = new URLSearchParams({ mockPage: String(filters.mockPage),
      placementPage: String(filters.placementPage), pageSize: String(filters.pageSize) });
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}/analytics/exams?${params}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  courses(learnerId, token) {
    const endpoint = `/api/admin/learners/${encodeURIComponent(learnerId)}/analytics/courses`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  create(data, token) {
    return apiRequest("/api/admin/learners", { method: "POST", body: data, token, timeoutMs: 120_000 });
  },

  update(learnerId, data, token) {
    return apiRequest(`/api/admin/learners/${encodeURIComponent(learnerId)}`,
      { method: "PUT", body: data, token });
  },

  changeStatus(learnerId, status, token) {
    return apiRequest(`/api/admin/learners/${encodeURIComponent(learnerId)}/status`,
      { method: "PATCH", body: { status }, token });
  },

  remove(learnerId, token) {
    return apiRequest(`/api/admin/learners/${encodeURIComponent(learnerId)}`,
      { method: "DELETE", token });
  },
};
