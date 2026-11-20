import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { CourseCard } = await vite.ssrLoadModule("/src/pages/exam/CourseListPage.jsx");
const { default: CoursePurchaseDialog } = await vite.ssrLoadModule("/src/pages/exam/CoursePurchaseDialog.jsx");
const { paymentApi } = await vite.ssrLoadModule("/src/services/paymentApi.js");
const course = { courseId: 7, name: "TOEIC Foundation", stage: 1, maxTargetScore: 550,
  retailPrice: 199000, accessDays: 30, accessStatus: "NOT_PURCHASED", coverImageUrl: null };
const card = overrides => renderToStaticMarkup(React.createElement(MemoryRouter, null,
  React.createElement(CourseCard, { course: { ...course, ...overrides }, isGuest: false,
    onPurchase() {}, requireAuthentication() {} })));
const dialog = quote => renderToStaticMarkup(React.createElement(CoursePurchaseDialog, {
  course, quote, quoteError: "", error: "", busy: false, onClose() {}, onConfirm() {}
}));

test("course cards expose one status-specific action without a cart checkbox", () => {
  const newCourse = card({});
  assert.match(newCourse, /Mua khóa học/);
  assert.match(newCourse, /199\.000/);
  assert.match(newCourse, /30 ngày truy cập/);
  assert.match(newCourse, /Mục tiêu tối đa: 550 điểm/);
  assert.doesNotMatch(newCourse, /type="checkbox"/);
  assert.match(card({ accessStatus: "ACTIVE" }), /Vào học/);
  assert.match(card({ accessStatus: "EXPIRED" }), /Gia hạn khóa học/);
  assert.match(card({ accessStatus: "PAYMENT_PENDING" }), /Đang chờ thanh toán/);
});

test("confirmation shows the authoritative single-course quote and gates checkout", () => {
  const valid = dialog({ courses: [{ courseId: 7, name: course.name, price: 199000 }],
    total: 199000, validDays: 30 });
  assert.match(valid, /role="dialog"/);
  assert.match(valid, /VNPay/);
  assert.match(valid, /Tổng thanh toán/);
  assert.match(valid, /30 ngày/);
  assert.match(valid, /Tiếp tục thanh toán/);
  assert.doesNotMatch(valid, /disabled=""[^>]*>Tiếp tục thanh toán/);

  const invalid = dialog({ courses: [{ courseId: 8, price: 199000 }], total: 199000, validDays: 30 });
  assert.match(invalid, /disabled=""[^>]*>Tiếp tục thanh toán/);
});

test("payment API sends exactly one course ID for quote and checkout", async () => {
  const originalFetch = globalThis.fetch;
  const calls = [];
  globalThis.fetch = async (url, options) => {
    calls.push({ url, body: JSON.parse(options.body) });
    return new Response(JSON.stringify({ total: 199000 }), {
      status: 200, headers: { "content-type": "application/json" }
    });
  };
  try {
    await paymentApi.quoteCourse(7, "test-token");
    await paymentApi.createCourse(7, 199000, "test-token");
  } finally { globalThis.fetch = originalFetch; }
  assert.deepEqual(calls.map(call => call.body.courseIds), [[7], [7]]);
  assert.equal(calls[1].body.expectedTotal, 199000);
  assert.match(calls[1].url, /\/api\/payment\/create-courses$/);
  assert.throws(() => paymentApi.createCourse([7, 8], 398000, "test-token"), TypeError);
});
