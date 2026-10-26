function datePart(value) {
  if (!value) return "";
  const text = String(value);
  return /^\d{4}-\d{2}-\d{2}/.test(text) ? text.slice(0, 10) : "";
}
export function dashboardListPath(path, range, filters = {}) {
  const [pathname, existing = ""] = path.split("?");
  const params = new URLSearchParams(existing);
  const dateFrom = datePart(range?.from);
  const dateTo = datePart(range?.to);
  if (dateFrom) params.set("dateFrom", dateFrom);
  if (dateTo) params.set("dateTo", dateTo);
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") params.set(key, String(value));
  });
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}
