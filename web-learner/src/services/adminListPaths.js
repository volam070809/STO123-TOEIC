export function adminLearnerListPath(filters) {
  const params = new URLSearchParams();
  if (filters.search) params.set("search", filters.search);
  if (filters.status) params.set("status", filters.status);
  if (filters.dateFrom) params.set("dateFrom", filters.dateFrom);
  if (filters.dateTo) params.set("dateTo", filters.dateTo);
  params.set("page", String(filters.page));
  params.set("pageSize", String(filters.pageSize));
  return `/api/admin/learners?${params}`;
}
export function adminEnrollmentListPath(filters) {
  const params = new URLSearchParams();
  for (const key of ["search", "status", "packageId", "courseId", "dateFrom", "dateTo"]) {
    if (filters[key]) params.set(key, String(filters[key]));
  }
  params.set("page", String(filters.page));
  params.set("pageSize", String(filters.pageSize));
  return `/api/admin/enrollments?${params}`;
}

export function adminPaymentListPath(filters) {
  return adminPaymentPath("/api/admin/payments", filters);
}

export function adminPaymentLearnersPath(filters) {
  return adminPaymentPath("/api/admin/payments/learners", filters);
}

export function adminPaymentLearnerHistoryPath(learnerId, filters) {
  return adminPaymentPath(`/api/admin/payments/learners/${encodeURIComponent(learnerId)}`, filters);
}

function adminPaymentPath(base, filters) {
  const params = new URLSearchParams();
  for (const key of ["search", "status", "gateway", "dateFrom", "dateTo"]) {
    if (filters[key]) params.set(key, String(filters[key]));
  }
  params.set("page", String(filters.page));
  params.set("pageSize", String(filters.pageSize));
  return `${base}?${params}`;
}
