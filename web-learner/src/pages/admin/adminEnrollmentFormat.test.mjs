import test from "node:test";
import assert from "node:assert/strict";
import { accessStateLabel, paymentStateLabel, purchaseTypeLabel } from "./adminEnrollmentFormat.js";

test("phân biệt mua gói và mua khóa học trực tiếp", () => {
  assert.equal(purchaseTypeLabel("PACKAGE"), "Gói học");
  assert.equal(purchaseTypeLabel("DIRECT_COURSES"), "Mua khóa học trực tiếp");
});

test("hiển thị độc lập trạng thái thanh toán và quyền truy cập", () => {
  assert.equal(paymentStateLabel("SUCCESS"), "Đã thanh toán");
  assert.equal(accessStateLabel("EXPIRED"), "Quyền truy cập đã hết hạn");
  assert.equal(accessStateLabel("PAYMENT_FAILED"), "Thanh toán không thành công");
});
