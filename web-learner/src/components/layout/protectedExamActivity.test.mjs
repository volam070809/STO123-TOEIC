import test from "node:test";
import assert from "node:assert/strict";
import { isProtectedExamActive } from "./protectedExamActivity.js";

test("only current active placement and mock variants hide help", () => {
  for (const source of ["PLACEMENT", "MOCK", "FIXED", "RANDOM", "PART"])
    assert.equal(isProtectedExamActive({ source, status: "DANG_LAM" }), true);
  assert.equal(isProtectedExamActive({ source: "PLACEMENT", status: "BO_DO" }), true);
  for (const status of ["DA_NOP", "HET_GIO", "HOAN_THANH"])
    assert.equal(isProtectedExamActive({ source: "MOCK", status }), false);
  assert.equal(isProtectedExamActive({ source: "PRACTICE", status: "DANG_LAM" }), false);
  assert.equal(isProtectedExamActive(null), false);
});
