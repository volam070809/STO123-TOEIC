import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";

const shareRead = createSharedRequest({ ttlMs: 10_000 });

export const adminPackageApi = {
  list(filters, token) {
    const params = new URLSearchParams();
    if (filters.search) params.set("search", filters.search);
    if (filters.status) params.set("status", filters.status);
    if (filters.courseId) params.set("courseId", String(filters.courseId));
    params.set("page", String(filters.page)); params.set("pageSize", String(filters.pageSize));
    const endpoint = `/api/admin/packages?${params}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  detail(id, token) {
    const endpoint = `/api/admin/packages/${encodeURIComponent(id)}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  create(data, token) { return apiRequest("/api/admin/packages", { method: "POST", body: data, token }); },
  update(id, data, token) { return apiRequest(`/api/admin/packages/${encodeURIComponent(id)}`, { method: "PUT", body: data, token }); },
  changeStatus(id, status, token) { return apiRequest(`/api/admin/packages/${encodeURIComponent(id)}/status`, { method: "PATCH", body: { status }, token }); },
  remove(id, token) { return apiRequest(`/api/admin/packages/${encodeURIComponent(id)}`, { method: "DELETE", token }); },
};
