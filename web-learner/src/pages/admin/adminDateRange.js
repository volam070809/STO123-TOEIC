export const ADMIN_DATE_MIN = "1753-01-01";
export const ADMIN_DATE_MAX = "9999-12-30";
export const ADMIN_DATE_FILTER_STATUS = Object.freeze({
  VALID_APPLIED: "VALID_APPLIED",
  DRAFT_CHANGED: "DRAFT_CHANGED",
  INVALID_FILTER: "INVALID_FILTER",
});
export const ADMIN_DATE_INVALID_TITLE = "Khoảng ngày chưa hợp lệ.";
export const ADMIN_DATE_INVALID_DETAIL = "Vui lòng kiểm tra lại ngày bắt đầu và ngày kết thúc, sau đó nhấn Áp dụng.";
export const ADMIN_DATE_CHANGED_TITLE = "Bộ lọc đã thay đổi.";
export const ADMIN_DATE_CHANGED_DETAIL = "Nhấn Áp dụng để cập nhật.";
export const ADMIN_ANALYTICS_MODE = Object.freeze({ ALL: "ALL", YEAR: "YEAR", MONTH: "MONTH", CUSTOM: "CUSTOM" });

function pad(value) { return String(value).padStart(2, "0"); }
function localDateValue(date) { return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`; }
function lastDayOfMonth(year, month) { return new Date(year, month, 0).getDate(); }

export function currentYearPeriod(now = new Date()) {
  const year = now.getFullYear();
  return { from: `${year}-01-01`, to: localDateValue(now) };
}

export function analyticsYearPeriod(year, now = new Date()) {
  const numericYear = Number(year);
  const currentYear = now.getFullYear();
  if (!Number.isInteger(numericYear) || numericYear < 1753 || numericYear > currentYear) return null;
  return { from: `${numericYear}-01-01`, to: numericYear === currentYear ? localDateValue(now) : `${numericYear}-12-31` };
}

export function analyticsMonthPeriod(year, month, now = new Date()) {
  const numericYear = Number(year); const numericMonth = Number(month);
  const currentYear = now.getFullYear(); const currentMonth = now.getMonth() + 1;
  if (!Number.isInteger(numericYear) || numericYear < 1753 || numericYear > currentYear ||
    !Number.isInteger(numericMonth) || numericMonth < 1 || numericMonth > 12 ||
    (numericYear === currentYear && numericMonth > currentMonth)) return null;
  const toDay = numericYear === currentYear && numericMonth === currentMonth ? now.getDate() : lastDayOfMonth(numericYear, numericMonth);
  return { from: `${numericYear}-${pad(numericMonth)}-01`, to: `${numericYear}-${pad(numericMonth)}-${pad(toDay)}` };
}

export function analyticsAppliedPeriod(dateFrom, dateTo, now = new Date()) {
  return !dateFrom && !dateTo ? currentYearPeriod(now) : { from: dateFrom || "", to: dateTo || "" };
}

export function reportsAppliedPeriod(dateFrom, dateTo) {
  return { from: dateFrom || "", to: dateTo || "" };
}

export function inferAnalyticsPeriodMode({ from, to }, now = new Date(), options = {}) {
  const { allowAll = false, preferredMode = null } = options;
  if (allowAll && !from && !to) return { mode: ADMIN_ANALYTICS_MODE.ALL, year: now.getFullYear(), month: now.getMonth() + 1 };
  const parsed = calendarDate(from); const parsedTo = calendarDate(to);
  if (!parsed || !parsedTo) return { mode: ADMIN_ANALYTICS_MODE.CUSTOM, year: now.getFullYear(), month: now.getMonth() + 1 };
  if ([ADMIN_ANALYTICS_MODE.YEAR, ADMIN_ANALYTICS_MODE.MONTH, ADMIN_ANALYTICS_MODE.CUSTOM].includes(preferredMode))
    return { mode: preferredMode, year: parsed.year, month: parsed.month };
  const yearPeriod = analyticsYearPeriod(parsed.year, now);
  if (yearPeriod?.from === from && yearPeriod.to === to)
    return { mode: ADMIN_ANALYTICS_MODE.YEAR, year: parsed.year,
      month: parsed.year === now.getFullYear() ? now.getMonth() + 1 : parsed.month };
  const monthPeriod = analyticsMonthPeriod(parsed.year, parsed.month, now);
  if (monthPeriod?.from === from && monthPeriod.to === to)
    return { mode: ADMIN_ANALYTICS_MODE.MONTH, year: parsed.year, month: parsed.month };
  return { mode: ADMIN_ANALYTICS_MODE.CUSTOM, year: parsed.year, month: parsed.month };
}

export function reportsAppliedMode(period, values, now = new Date()) {
  const requested = String(period || "").toUpperCase();
  const preferredMode = [ADMIN_ANALYTICS_MODE.YEAR, ADMIN_ANALYTICS_MODE.MONTH,
    ADMIN_ANALYTICS_MODE.CUSTOM].includes(requested) ? requested : null;
  return inferAnalyticsPeriodMode(values, now, { allowAll: true, preferredMode }).mode;
}

function calendarDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const [year, month, day] = value.split("-").map(Number);
  const days = new Date(Date.UTC(year, month, 0)).getUTCDate();
  if (month < 1 || month > 12 || day < 1 || day > days) return null;
  return { year, month, day };
}

function utcDay({ year, month, day }) {
  const date = new Date(0);
  date.setUTCHours(0, 0, 0, 0);
  date.setUTCFullYear(year, month - 1, day);
  return date.getTime();
}

export function validateAdminDateRange({ from = "", to = "" }, maxDays) {
  if (!from && !to) return { valid: true, error: "" };
  if (!from || !to) return { valid: false, error: "Vui lòng nhập đầy đủ Từ ngày và Đến ngày." };
  const parsedFrom = calendarDate(from);
  const parsedTo = calendarDate(to);
  if (!parsedFrom || !parsedTo)
    return { valid: false, error: "Ngày phải có định dạng đầy đủ YYYY-MM-DD và là ngày hợp lệ." };
  if (from < ADMIN_DATE_MIN || from > ADMIN_DATE_MAX || to < ADMIN_DATE_MIN || to > ADMIN_DATE_MAX)
    return { valid: false, error: "Ngày phải nằm trong phạm vi 01/01/1753 đến 30/12/9999." };
  if (from > to) return { valid: false, error: "Ngày bắt đầu không được sau ngày kết thúc." };
  const days = Math.round((utcDay(parsedTo) - utcDay(parsedFrom)) / 86_400_000) + 1;
  if (maxDays && days > maxDays)
    return { valid: false, error: `Khoảng ngày không được vượt quá ${maxDays} ngày.` };
  return { valid: true, error: "" };
}

export function applyAdminDateDraft(values, maxDays, onApply) {
  const result = validateAdminDateRange(values, maxDays);
  if (result.valid) onApply(values);
  return result;
}

export function adminDateDraftStatus(values, appliedValues, maxDays, validate = false) {
  if (values.from === appliedValues.from && values.to === appliedValues.to)
    return ADMIN_DATE_FILTER_STATUS.VALID_APPLIED;
  if (validate && !validateAdminDateRange(values, maxDays).valid)
    return ADMIN_DATE_FILTER_STATUS.INVALID_FILTER;
  return ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED;
}

export function shouldValidateAdminDateDraft({ from = "", to = "" }) {
  return /^\d{4}-\d{2}-\d{2}$/.test(from) && /^\d{4}-\d{2}-\d{2}$/.test(to);
}

export function adminDateDisplayState(draftStatus, appliedError = "") {
  if (appliedError) return {
    showResults: false,
    draftChanged: false,
    title: ADMIN_DATE_INVALID_TITLE,
    detail: ADMIN_DATE_INVALID_DETAIL,
  };
  if (draftStatus === ADMIN_DATE_FILTER_STATUS.INVALID_FILTER) return {
    showResults: true,
    draftChanged: true,
    title: ADMIN_DATE_INVALID_TITLE,
    detail: ADMIN_DATE_INVALID_DETAIL,
  };
  if (draftStatus === ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED) return {
    showResults: true,
    draftChanged: true,
    title: ADMIN_DATE_CHANGED_TITLE,
    detail: ADMIN_DATE_CHANGED_DETAIL,
  };
  return { showResults: true, draftChanged: false, title: "", detail: "" };
}
