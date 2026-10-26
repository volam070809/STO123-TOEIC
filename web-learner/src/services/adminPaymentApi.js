import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";
import { adminPaymentLearnerHistoryPath, adminPaymentLearnersPath, adminPaymentListPath } from "./adminListPaths";

const shareRead = createSharedRequest({ ttlMs: 10_000 });

export const adminPaymentApi = {
  list(filters, token) {
    const endpoint = adminPaymentListPath(filters);
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  learners(filters, token) {
    const endpoint = adminPaymentLearnersPath(filters);
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  learnerHistory(learnerId, filters, token) {
    const endpoint = adminPaymentLearnerHistoryPath(learnerId, filters);
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
  detail(id, token) {
    const endpoint = `/api/admin/payments/${encodeURIComponent(id)}`;
    return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token }));
  },
};
