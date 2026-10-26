import assert from "node:assert/strict";
import test from "node:test";
import { validatePackage } from "./adminPackageFormData.js";

const valid = { name: "Combo TOEIC", price: "199000", validDays: "90", description: "Ba tháng",
  status: "DANG_MO", courseIds: [1, 2] };

test("package form accepts payment-compatible data", () => assert.deepEqual(validatePackage(valid), {}));
test("package form rejects invalid price, duration, status and course set", () => {
  const errors = validatePackage({ ...valid, price: "1.5", validDays: "0", status: "UNKNOWN", courseIds: [] });
  assert.ok(errors.price); assert.ok(errors.validDays); assert.ok(errors.status); assert.ok(errors.courseIds);
});
