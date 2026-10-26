import test from "node:test";
import assert from "node:assert/strict";
import { protectedReturnPath } from "./protectedReturnPath.js";

test("learner action links survive login without accepting unrelated paths", () => {
  assert.equal(protectedReturnPath("/placement"), "/placement");
  assert.equal(protectedReturnPath("/mock-test"), "/mock-test");
  assert.equal(protectedReturnPath("/practice"), "/practice");
  assert.equal(protectedReturnPath("/practice/parts"), "/practice/parts");
  assert.equal(protectedReturnPath("/practice/vocabulary"), "/practice/vocabulary");
  assert.equal(protectedReturnPath("/practice/parts?attempt=12"), null);
  assert.equal(protectedReturnPath("/practice?next=https://attacker.example"), null);
  assert.equal(protectedReturnPath("//attacker.example/practice"), null);
});

test("vocabulary topic survives authentication without accepting preview bypass parameters", () => {
  assert.equal(protectedReturnPath("/vocabulary"), "/vocabulary");
  assert.equal(protectedReturnPath("/vocabulary?topic=12"), "/vocabulary?topic=12");
  assert.equal(protectedReturnPath("/vocabulary?topic=12&offset=10"), null);
  assert.equal(protectedReturnPath("/vocabulary?topic=12&topic=13"), null);
  assert.equal(protectedReturnPath("/vocabulary?topic=0"), null);
});

test("course links survive login without accepting external redirects", () => {
  assert.equal(protectedReturnPath("/courses"), "/courses");
  assert.equal(protectedReturnPath("/courses/3"), "/courses/3");
  assert.equal(protectedReturnPath("https://attacker.example/courses/3"), null);
  assert.equal(protectedReturnPath("//attacker.example/courses/3"), null);
  assert.equal(protectedReturnPath("/courses/3?next=evil"), null);
  assert.equal(protectedReturnPath("/courses/0"), null);
  assert.equal(protectedReturnPath("/packages"), "/courses");
  assert.equal(protectedReturnPath("/packages/12"), "/courses");
  assert.equal(protectedReturnPath("/payment/result?paymentId=12"), "/courses?paymentId=12");
  assert.equal(protectedReturnPath("/courses?paymentId=12"), "/courses?paymentId=12");
  assert.equal(protectedReturnPath("/payment/result?paymentId=12&next=https://attacker.example"), null);
  assert.equal(protectedReturnPath("/payment/result?paymentId=0"), null);
});

test("admin links survive login but external and unknown admin links do not", () => {
  assert.equal(protectedReturnPath("/admin"), "/admin");
  assert.equal(protectedReturnPath("/admin/learners"), "/admin/learners");
  assert.equal(protectedReturnPath("/admin/learners/42"), "/admin/learners/42");
  assert.equal(protectedReturnPath("/admin/learners/new"), "/admin/learners/new");
  assert.equal(protectedReturnPath("/admin/learners/42/edit"), "/admin/learners/42/edit");
  assert.equal(protectedReturnPath("/admin/courses?stage=2&status=DANG_MO"),
    "/admin/courses?stage=2&status=DANG_MO");
  assert.equal(protectedReturnPath("/admin/courses/3/edit"), "/admin/courses/3/edit");
  assert.equal(protectedReturnPath("/admin/packages?status=DANG_MO"), "/admin/packages?status=DANG_MO");
  assert.equal(protectedReturnPath("/admin/packages/2/edit"), "/admin/packages/2/edit");
  assert.equal(protectedReturnPath("/admin/enrollments?packageId=2&dateFrom=2026-01-01"),
    "/admin/enrollments?packageId=2&dateFrom=2026-01-01");
  assert.equal(protectedReturnPath("/admin/enrollments/25"), "/admin/enrollments/25");
  assert.equal(protectedReturnPath("/admin/enrollments/0"), null);
  assert.equal(protectedReturnPath("/admin/payments?status=THANH_CONG&gateway=VNPAY"),
    "/admin/payments?status=THANH_CONG&gateway=VNPAY");
  assert.equal(protectedReturnPath("/admin/payments?view=transactions&status=THANH_CONG"),
    "/admin/payments?view=transactions&status=THANH_CONG");
  assert.equal(protectedReturnPath("/admin/payments?view=unsafe"), null);
  assert.equal(protectedReturnPath("/admin/payments/20"), "/admin/payments/20");
  assert.equal(protectedReturnPath("/admin/payments/0"), null);
  assert.equal(protectedReturnPath("/admin?dateFrom=2026-10-01&dateTo=2026-10-21"),
    "/admin?dateFrom=2026-10-01&dateTo=2026-10-21");
  assert.equal(protectedReturnPath("/admin/reports?type=payments&dateFrom=2026-10-01"),
    "/admin/reports?type=payments&dateFrom=2026-10-01");
  assert.equal(protectedReturnPath("/admin/reports?type=payments&period=month&dateFrom=2026-10-01&dateTo=2026-10-31"),
    "/admin/reports?type=payments&period=month&dateFrom=2026-10-01&dateTo=2026-10-31");
  assert.equal(protectedReturnPath("/admin/reports?period=week"), null);
  assert.equal(protectedReturnPath("/admin/learners?status=HOAT_DONG&page=2"),
    "/admin/learners?status=HOAT_DONG&page=2");
  assert.equal(protectedReturnPath("/admin/learners/0"), null);
  assert.equal(protectedReturnPath("/admin/learners/0/edit"), null);
  assert.equal(protectedReturnPath("/admin/settings"), null);
  assert.equal(protectedReturnPath("/admin/learners?next=https://attacker.example"), null);
  assert.equal(protectedReturnPath("/admin/reports?type=unknown"), null);
  assert.equal(protectedReturnPath("/admin/reports?type=payments&type=learners"), null);
  assert.equal(protectedReturnPath("https://attacker.example/admin/learners"), null);
});
