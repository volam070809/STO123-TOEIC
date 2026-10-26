import test from "node:test";
import assert from "node:assert/strict";
import { dashboardListPath } from "./adminDashboardLinks.js";

const range = { from: "2026-10-10T00:00:00", to: "2026-10-17T00:00:00", days: 8 };

test("Dashboard drill-downs preserve the selected date range and scoped status", () => {
  assert.equal(dashboardListPath("/admin/learners", range),
    "/admin/learners?dateFrom=2026-10-10&dateTo=2026-10-17");
  assert.equal(dashboardListPath("/admin/enrollments", range, { packageId: 4 }),
    "/admin/enrollments?dateFrom=2026-10-10&dateTo=2026-10-17&packageId=4");
  assert.equal(dashboardListPath("/admin/payments", range, { status: "THANH_CONG" }),
    "/admin/payments?dateFrom=2026-10-10&dateTo=2026-10-17&status=THANH_CONG");
});
test("Dashboard drill-down dates are dynamic and preserve existing filters", () => {
  assert.equal(dashboardListPath("/admin/payments?gateway=VNPAY", {
    from: "2026-09-01", to: "2026-09-30"
  }, { status: "THANH_CONG" }),
  "/admin/payments?gateway=VNPAY&dateFrom=2026-09-01&dateTo=2026-09-30&status=THANH_CONG");
});
