export function protectedReturnPath(value) {
  const adminPath = adminReturnPath(value);
  if (adminPath) return adminPath;
  if (value === "/vocabulary") return value;
  const vocabularyTopic = /^\/vocabulary\?topic=([1-9]\d{0,9})$/.exec(value ?? "");
  if (vocabularyTopic && Number.isSafeInteger(Number(vocabularyTopic[1]))) return vocabularyTopic[0];
  if (["/placement", "/mock-test", "/practice", "/practice/parts", "/practice/vocabulary"].includes(value)) return value;
  if (value === "/courses") return value;
  const coursesPayment = /^\/courses\?paymentId=([1-9]\d{0,9})$/.exec(value ?? "");
  if (coursesPayment && Number.isSafeInteger(Number(coursesPayment[1]))) return coursesPayment[0];
  if (value === "/packages" || value === "/payment/history") return "/courses";
  const match = /^\/courses\/([1-9]\d{0,9})$/.exec(value ?? "");
  if (match && Number.isSafeInteger(Number(match[1]))) return match[0];
  const courseStep = /^\/courses\/([1-9]\d{0,9})\/steps\/([1-9]\d{0,9})$/.exec(value ?? "");
  if (courseStep && courseStep.slice(1).every(id => Number.isSafeInteger(Number(id)))) return courseStep[0];
  const packageMatch = /^\/packages\/([1-9]\d{0,9})$/.exec(value ?? "");
  if (packageMatch && Number.isSafeInteger(Number(packageMatch[1]))) return "/courses";
  const resultMatch = /^\/payment\/result\?paymentId=([1-9]\d{0,9})$/.exec(value ?? "");
  return resultMatch && Number.isSafeInteger(Number(resultMatch[1])) ?
    `/courses?paymentId=${resultMatch[1]}` : null;
}

function adminReturnPath(value) {
  if (typeof value !== "string" || !value.startsWith("/")) return null;
  try {
    const url = new URL(value, "https://sto123.local");
    if (url.origin !== "https://sto123.local" || url.hash) return null;
    const isList = url.pathname === "/admin" || url.pathname === "/admin/content" ||
      url.pathname === "/admin/learners" ||
      url.pathname === "/admin/learners/new" || url.pathname === "/admin/courses" ||
      url.pathname === "/admin/courses/new" || url.pathname === "/admin/packages" ||
      url.pathname === "/admin/packages/new" || url.pathname === "/admin/enrollments" ||
      url.pathname === "/admin/payments" || url.pathname === "/admin/reports";
    const detail = /^\/admin\/learners\/([1-9]\d{0,9})$/.exec(url.pathname);
    const edit = /^\/admin\/learners\/([1-9]\d{0,9})\/edit$/.exec(url.pathname);
    const courseDetail = /^\/admin\/courses\/([1-9]\d{0,9})$/.exec(url.pathname);
    const courseEdit = /^\/admin\/courses\/([1-9]\d{0,9})\/edit$/.exec(url.pathname);
    const courseContent = /^\/admin\/courses\/([1-9]\d{0,9})\/content$/.exec(url.pathname);
    const packageDetail = /^\/admin\/packages\/([1-9]\d{0,9})$/.exec(url.pathname);
    const packageEdit = /^\/admin\/packages\/([1-9]\d{0,9})\/edit$/.exec(url.pathname);
    const enrollmentDetail = /^\/admin\/enrollments\/([1-9]\d{0,9})$/.exec(url.pathname);
    const paymentDetail = /^\/admin\/payments\/([1-9]\d{0,9})$/.exec(url.pathname);
    if (!isList && !detail && !edit && !courseDetail && !courseEdit && !courseContent && !packageDetail && !packageEdit && !enrollmentDetail && !paymentDetail) return null;
    const id = detail?.[1] ?? edit?.[1] ?? courseDetail?.[1] ?? courseEdit?.[1] ?? courseContent?.[1] ?? packageDetail?.[1] ?? packageEdit?.[1] ?? enrollmentDetail?.[1] ?? paymentDetail?.[1];
    if (id && !Number.isSafeInteger(Number(id))) return null;
    const allowed = new Set(["search", "status", "stage", "courseId", "packageId", "gateway", "type", "view", "period", "dateFrom", "dateTo", "page", "pageSize"]);
    if ([...url.searchParams.keys()].some(key => !allowed.has(key))) return null;
    if ([...allowed].some(key => url.searchParams.getAll(key).length > 1)) return null;
    const reportTypes = new Set(["overview", "learners", "enrollments", "packages-courses", "payments", "toeic-results"]);
    if (url.pathname === "/admin/reports" && url.searchParams.has("type") && !reportTypes.has(url.searchParams.get("type"))) return null;
    const reportPeriods = new Set(["year", "month", "custom"]);
    if (url.searchParams.has("period") && (url.pathname !== "/admin/reports" || !reportPeriods.has(url.searchParams.get("period")))) return null;
    if (url.pathname === "/admin/payments" && url.searchParams.has("view") &&
      !["learners", "transactions"].includes(url.searchParams.get("view"))) return null;
    return url.pathname + url.search;
  } catch {
    return null;
  }
}
