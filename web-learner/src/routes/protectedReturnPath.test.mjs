import test from "node:test";
import assert from "node:assert/strict";
import { protectedReturnPath } from "./protectedReturnPath.js";

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
