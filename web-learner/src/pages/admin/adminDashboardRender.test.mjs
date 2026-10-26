import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { Dashboard } = await vite.ssrLoadModule("/src/pages/admin/AdminDashboardPage.jsx");
const { DateRangeFilter } = await vite.ssrLoadModule("/src/pages/admin/AdminAnalyticsUi.jsx");
const { DateRangeFields } = await vite.ssrLoadModule("/src/pages/admin/AdminDateRangeFields.jsx");
const render = data => renderToStaticMarkup(React.createElement(MemoryRouter, null,
  React.createElement(Dashboard, { data })));

const data = {
  range: { from: "2026-10-10T00:00:00", to: "2026-10-17T00:00:00", days: 8 },
  newLearners: 2, enrollments: 3, successfulRevenue: 400000,
  pendingPayments: 4, failedPayments: 5,
  learnerGrowth: [{ date: "2026-10-10", count: 2 }],
  revenueTrend: [{ date: "2026-10-10", count: 1, amount: 400000 }],
  offeringPerformance: [{ id: 7, name: "Gói A", type: "PACKAGE", count: 3, revenue: 400000 }],
  recentPayments: [], recentEnrollments: []
};

test("Dashboard renders period-specific KPIs and filtered drill-down links", () => {
  const html = render(data);
  for (const label of ["Học viên mới trong kỳ", "Đăng ký trong kỳ", "Doanh thu xác nhận trong kỳ",
    "Chờ xử lý trong kỳ", "Thất bại trong kỳ"]) assert.match(html, new RegExp(label));
  assert.match(html, /admin\/learners\?dateFrom=2026-10-10(?:&amp;|&)dateTo=2026-10-17/);
  assert.match(html, /admin\/payments\?dateFrom=2026-10-10(?:&amp;|&)dateTo=2026-10-17(?:&amp;|&)status=THANH_CONG/);
  assert.match(html, /admin\/enrollments\?dateFrom=2026-10-10(?:&amp;|&)dateTo=2026-10-17(?:&amp;|&)packageId=7/);
});

test("Dashboard shows explicit empty states for a period without recent records", () => {
  const html = render(data);
  assert.match(html, /Không có giao dịch được tạo trong kỳ/);
  assert.match(html, /Không có đăng ký trong kỳ/);
});

test("annual Dashboard trends render chronological month labels", () => {
  const annual = { ...data,
    learnerGrowth: Array.from({ length: 10 }, (_, index) => ({
      date: `2026-${String(index + 1).padStart(2, "0")}-01`, count: index,
    })),
    revenueTrend: Array.from({ length: 10 }, (_, index) => ({
      date: `2026-${String(index + 1).padStart(2, "0")}-01`, count: index, amount: index * 1000,
    })),
  };
  const html = render(annual);
  assert.match(html, /Số lượt theo tháng/);
  assert.match(html, /T1\/2026/);
  assert.match(html, /T10\/2026/);
});

test("date filter actions stay together in one shared action group", () => {
  const listFilter = renderToStaticMarkup(React.createElement(DateRangeFields, {
    from: "", to: "", onApply: () => {}, onClear: () => {},
  }));
  const analyticsFilter = renderToStaticMarkup(React.createElement(DateRangeFilter, {
    from: "2026-01-01", to: "2026-10-23", maxDays: 366, onApply: () => {},
  }));
  assert.match(listFilter, /admin-filter-actions[^>]*><button[^>]*>Áp dụng<\/button><button[^>]*>Xóa bộ lọc<\/button>/);
  assert.match(analyticsFilter, /admin-filter-actions[^>]*><button[^>]*>Áp dụng<\/button><button[^>]*>Đặt lại<\/button>/);
  assert.match(analyticsFilter, /Theo năm/);
});

test("Reports filter exposes exactly ALL, YEAR, MONTH and CUSTOM without WEEK", () => {
  const reportsFilter = renderToStaticMarkup(React.createElement(DateRangeFilter, {
    from: "", to: "", maxDays: 366, allowAll: true, appliedMode: "ALL", onApply: () => {},
  }));
  const options = [...reportsFilter.matchAll(/<option[^>]*>([^<]+)<\/option>/g)]
    .map(match => match[1]);
  assert.deepEqual(options, ["Toàn bộ", "Theo năm", "Theo tháng", "Tùy chọn"]);
  assert.doesNotMatch(reportsFilter, /tuần|WEEK/i);
  assert.match(reportsFilter, /Toàn bộ dữ liệu đến hiện tại/);
});
