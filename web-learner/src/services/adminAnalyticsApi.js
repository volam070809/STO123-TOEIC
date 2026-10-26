import { apiRequest } from "./api";
import { createSharedRequest } from "./sharedRequest";

const shareRead = createSharedRequest({ ttlMs: 10_000 });
function query(filters = {}) { const params = new URLSearchParams(); if (filters.dateFrom) params.set("dateFrom", filters.dateFrom); if (filters.dateTo) params.set("dateTo", filters.dateTo); return params.toString(); }
function get(path, filters, token) { const endpoint = `${path}?${query(filters)}`; return shareRead(`${token}|${endpoint}`, () => apiRequest(endpoint, { token })); }

export const adminAnalyticsApi = {
  dashboard(filters, token) { return get("/api/admin/dashboard", filters, token); },
  report(type, filters, token) { return get(`/api/admin/reports/${type}`, filters, token); },
};
