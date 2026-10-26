import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";
import { adminEnrollmentListPath } from "./adminListPaths";

const shareRead = createSharedRequest({ ttlMs: 10_000 });

export const adminEnrollmentApi = {
  list(filters, token) {
    const endpoint = adminEnrollmentListPath(filters);
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  detail(id, token) {
    const endpoint = `/api/admin/enrollments/${encodeURIComponent(id)}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
};
