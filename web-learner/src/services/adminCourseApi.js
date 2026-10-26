import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";
import { buildCourseImageFormData } from "../pages/admin/adminCourseFormData";

const shareRead = createSharedRequest({ ttlMs: 10_000 });

export const adminCourseApi = {
  list(filters, token) {
    const params = new URLSearchParams();
    if (filters.search) params.set("search", filters.search);
    if (filters.status) params.set("status", filters.status);
    if (filters.stage) params.set("stage", String(filters.stage));
    params.set("page", String(filters.page));
    params.set("pageSize", String(filters.pageSize));
    const endpoint = `/api/admin/courses?${params}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  detail(courseId, token) {
    const endpoint = `/api/admin/courses/${encodeURIComponent(courseId)}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },

  create(data, token, imageFile = null) {
    return apiRequest(imageFile ? "/api/admin/courses/with-image" : "/api/admin/courses",
      { method: "POST", body: imageFile ? buildCourseImageFormData(data, imageFile) : data, token });
  },

  update(courseId, data, token, imageFile = null) {
    const endpoint = `/api/admin/courses/${encodeURIComponent(courseId)}${imageFile ? "/with-image" : ""}`;
    return apiRequest(endpoint,
      { method: "PUT", body: imageFile ? buildCourseImageFormData(data, imageFile) : data, token });
  },

  changeStatus(courseId, status, token) {
    return apiRequest(`/api/admin/courses/${encodeURIComponent(courseId)}/status`,
      { method: "PATCH", body: { status }, token });
  },

  remove(courseId, token) {
    return apiRequest(`/api/admin/courses/${encodeURIComponent(courseId)}`,
      { method: "DELETE", token });
  },
};
