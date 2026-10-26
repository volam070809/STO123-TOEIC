import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { adminLearnerListPath, adminEnrollmentListPath, adminPaymentLearnerHistoryPath,
  adminPaymentLearnersPath, adminPaymentListPath } from "./adminListPaths.js";

const period = { dateFrom: "2026-10-10", dateTo: "2026-10-17", page: 3, pageSize: 20 };

test("destination APIs preserve Dashboard dates during pagination", () => {
  assert.equal(adminLearnerListPath({ ...period, status: "HOAT_DONG" }),
    "/api/admin/learners?status=HOAT_DONG&dateFrom=2026-10-10&dateTo=2026-10-17&page=3&pageSize=20");
  assert.equal(adminEnrollmentListPath({ ...period, courseId: 9 }),
    "/api/admin/enrollments?courseId=9&dateFrom=2026-10-10&dateTo=2026-10-17&page=3&pageSize=20");
  assert.equal(adminPaymentListPath({ ...period, status: "THANH_CONG" }),
    "/api/admin/payments?status=THANH_CONG&dateFrom=2026-10-10&dateTo=2026-10-17&page=3&pageSize=20");
});

test("payment learner summaries and histories preserve the same filter scope", () => {
  const filters = { ...period, search: "an@example.test", gateway: "VNPAY", status: "THANH_CONG" };
  assert.equal(adminPaymentLearnersPath(filters),
    "/api/admin/payments/learners?search=an%40example.test&status=THANH_CONG&gateway=VNPAY&dateFrom=2026-10-10&dateTo=2026-10-17&page=3&pageSize=20");
  assert.equal(adminPaymentLearnerHistoryPath(42, filters),
    "/api/admin/payments/learners/42?search=an%40example.test&status=THANH_CONG&gateway=VNPAY&dateFrom=2026-10-10&dateTo=2026-10-17&page=3&pageSize=20");
});

test("payments defaults to learner mode and keeps transaction/detail actions", () => {
  const source = readFileSync(new URL("../pages/admin/AdminPaymentsPage.jsx", import.meta.url), "utf8");
  assert.match(source, /params\.get\("view"\) === "transactions" \? "transactions" : "learners"/);
  assert.match(source, /adminPaymentApi\.learners/);
  assert.match(source, /adminPaymentApi\.learnerHistory/);
  assert.match(source, /Theo học viên/);
  assert.match(source, /Theo giao dịch/);
  assert.match(source, /to={`\/admin\/payments\/\$\{item\.paymentId\}`}/);
  assert.match(source, /expanded && <tr className="admin-payment-history-row"/);
  assert.match(source, /Không tìm thấy học viên có giao dịch/);
  assert.match(source, /Không có giao dịch phù hợp/);
});
