import test from "node:test";
import assert from "node:assert/strict";
import { ADMIN_DATE_CHANGED_DETAIL, ADMIN_DATE_CHANGED_TITLE, ADMIN_DATE_FILTER_STATUS,
  ADMIN_DATE_INVALID_DETAIL, ADMIN_DATE_INVALID_TITLE, ADMIN_ANALYTICS_MODE,
  adminDateDisplayState, adminDateDraftStatus, analyticsAppliedPeriod, analyticsMonthPeriod,
  analyticsYearPeriod, applyAdminDateDraft, currentYearPeriod, inferAnalyticsPeriodMode, reportsAppliedMode,
  reportsAppliedPeriod, shouldValidateAdminDateDraft, validateAdminDateRange } from "./adminDateRange.js";

test("analytics defaults and reset use the dynamic current calendar year", () => {
  const now = new Date(2026, 9, 23);
  assert.deepEqual(currentYearPeriod(now), { from: "2026-01-01", to: "2026-10-23" });
  assert.deepEqual(analyticsAppliedPeriod("", "", now), { from: "2026-01-01", to: "2026-10-23" });
  assert.deepEqual(analyticsYearPeriod(2025, now), { from: "2025-01-01", to: "2025-12-31" });
  assert.deepEqual(analyticsYearPeriod(2026, now), { from: "2026-01-01", to: "2026-10-23" });
  assert.deepEqual(analyticsAppliedPeriod("2025-01-01", "2025-12-31", now),
    { from: "2025-01-01", to: "2025-12-31" });
  assert.deepEqual(analyticsAppliedPeriod("2025-01-01", "", now), { from: "2025-01-01", to: "" });
});

test("month periods end at today for the current month and month end for earlier months", () => {
  const now = new Date(2026, 9, 23);
  assert.deepEqual(analyticsMonthPeriod(2026, 10, now), { from: "2026-10-01", to: "2026-10-23" });
  assert.deepEqual(analyticsMonthPeriod(2024, 2, now), { from: "2024-02-01", to: "2024-02-29" });
  assert.equal(inferAnalyticsPeriodMode({ from: "2026-10-01", to: "2026-10-23" }, now).mode, ADMIN_ANALYTICS_MODE.MONTH);
  assert.equal(inferAnalyticsPeriodMode({ from: "2026-03-02", to: "2026-03-18" }, now).mode, ADMIN_ANALYTICS_MODE.CUSTOM);
});

test("Reports defaults and resets to ALL while Dashboard keeps the current-year default", () => {
  const now = new Date(2026, 9, 23);
  assert.deepEqual(reportsAppliedPeriod("", ""), { from: "", to: "" });
  assert.equal(reportsAppliedMode("", { from: "", to: "" }, now), ADMIN_ANALYTICS_MODE.ALL);
  assert.equal(reportsAppliedMode("week", { from: "", to: "" }, now), ADMIN_ANALYTICS_MODE.ALL);
  assert.deepEqual(analyticsAppliedPeriod("", "", now), { from: "2026-01-01", to: "2026-10-23" });
});

test("Reports preserves explicit YEAR, MONTH and CUSTOM modes through URL state", () => {
  const now = new Date(2026, 9, 23);
  assert.equal(reportsAppliedMode("year", { from: "2025-01-01", to: "2025-12-31" }, now),
    ADMIN_ANALYTICS_MODE.YEAR);
  assert.equal(reportsAppliedMode("month", { from: "2026-09-01", to: "2026-09-30" }, now),
    ADMIN_ANALYTICS_MODE.MONTH);
  assert.equal(reportsAppliedMode("custom", { from: "2026-09-02", to: "2026-09-18" }, now),
    ADMIN_ANALYTICS_MODE.CUSTOM);
});

test("ALL has no 366-day client limit while explicit ranges retain it", () => {
  assert.equal(validateAdminDateRange({ from: "", to: "" }, 366).valid, true);
  assert.equal(validateAdminDateRange({ from: "2024-01-01", to: "2025-01-01" }, 366).valid, false);
});

test("partial and invalid calendar dates are rejected when Apply validates them", () => {
  assert.equal(validateAdminDateRange({ from: "0002-10-10", to: "2026-10-17" }).valid, false);
  assert.equal(validateAdminDateRange({ from: "2026-02-30", to: "2026-03-01" }).valid, false);
  assert.equal(validateAdminDateRange({ from: "2026-10", to: "2026-10-17" }).valid, false);
  assert.equal(validateAdminDateRange({ from: "2026-10-10", to: "" }).valid, false);
});

test("date order, SQL datetime bounds and page-specific maximum are enforced", () => {
  assert.equal(validateAdminDateRange({ from: "2026-10-18", to: "2026-10-17" }).valid, false);
  assert.equal(validateAdminDateRange({ from: "1752-12-31", to: "2026-10-17" }).valid, false);
  assert.equal(validateAdminDateRange({ from: "9999-12-30", to: "9999-12-30" }).valid, true);
  assert.equal(validateAdminDateRange({ from: "2026-01-01", to: "2026-04-01" }, 90).valid, false);
  assert.equal(validateAdminDateRange({ from: "2026-10-10", to: "2026-10-17" }, 90).valid, true);
});

test("only an explicit valid Apply operation publishes draft dates", () => {
  const applied = [];
  const publish = value => applied.push(value);
  applyAdminDateDraft({ from: "0002-10-10", to: "2026-10-17" }, null, publish);
  applyAdminDateDraft({ from: "2026-10-10", to: "" }, null, publish);
  assert.deepEqual(applied, []);
  applyAdminDateDraft({ from: "2026-10-10", to: "2026-10-17" }, null, publish);
  assert.deepEqual(applied, [{ from: "2026-10-10", to: "2026-10-17" }]);
});

test("partial typing is draft-only and is not aggressively marked invalid", () => {
  const applied = { from: "2026-10-12", to: "2044-10-16" };
  const partial = { from: "2026-10-12", to: "2000-10" };
  const appliedResult = { totalItems: 37, summary: { total: 37 }, items: [{ paymentId: 1 }] };
  assert.equal(shouldValidateAdminDateDraft(partial), false);
  assert.equal(adminDateDraftStatus(partial, applied, null, false), ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED);
  const display = adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED);
  assert.deepEqual(display, {
    showResults: true, draftChanged: true, title: ADMIN_DATE_CHANGED_TITLE, detail: ADMIN_DATE_CHANGED_DETAIL,
  });
  assert.strictEqual(display.showResults ? appliedResult : null, appliedResult);
  assert.equal((display.showResults ? appliedResult : null).totalItems, 37);
});

test("editing draft dates does not publish URL or API effects before Apply", () => {
  const effects = [];
  const status = adminDateDraftStatus(
    { from: "0020-10-12", to: "2026-10-16" },
    { from: "2026-10-12", to: "2026-10-16" }, null, false);
  assert.equal(status, ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED);
  assert.deepEqual(effects, []);
});

test("invalid complete draft preserves previous results without publishing a request", () => {
  const appliedValues = { from: "2026-10-12", to: "2044-10-16" };
  const invalidDraft = { from: "2026-10-12", to: "2000-10-16" };
  const published = [];
  assert.equal(shouldValidateAdminDateDraft(invalidDraft), true);
  assert.equal(adminDateDraftStatus(invalidDraft, appliedValues, null, true), ADMIN_DATE_FILTER_STATUS.INVALID_FILTER);
  assert.deepEqual(adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.INVALID_FILTER), {
    showResults: true, draftChanged: true, title: ADMIN_DATE_INVALID_TITLE, detail: ADMIN_DATE_INVALID_DETAIL,
  });
  applyAdminDateDraft(invalidDraft, null, value => published.push(value));
  assert.deepEqual(published, []);
});

test("valid Apply, Clear and URL navigation restore the applied result state", () => {
  const appliedValues = { from: "2026-10-12", to: "2026-10-16" };
  const nextValues = { from: "2026-10-13", to: "2026-10-17" };
  assert.equal(adminDateDraftStatus(nextValues, appliedValues, null, true), ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED);
  assert.equal(adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.DRAFT_CHANGED).showResults, true);
  assert.equal(adminDateDraftStatus(appliedValues, appliedValues, null, true), ADMIN_DATE_FILTER_STATUS.VALID_APPLIED);
  assert.equal(adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.VALID_APPLIED).showResults, true);
  assert.equal(adminDateDraftStatus({ from: "", to: "" }, { from: "", to: "" }, null, true), ADMIN_DATE_FILTER_STATUS.VALID_APPLIED);
  assert.equal(adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.VALID_APPLIED).showResults, true);
  assert.equal(adminDateDisplayState(ADMIN_DATE_FILTER_STATUS.VALID_APPLIED, "URL không hợp lệ").showResults, false);
});
