import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { loginDestination, preservePendingAuthReturnTo, requiresEmailVerification } from "./loginDestination.js";

test("normal login sends management admin to Dashboard", () => {
  assert.equal(loginDestination("ADMIN_QUAN_LY", null), "/admin");
  assert.equal(loginDestination("ADMIN_NOI_DUNG", null), "/admin/content");
  assert.equal(loginDestination("HOC_VIEN", null), "/");
});

test("an intentional valid deep link takes precedence over the role default", () => {
  assert.equal(loginDestination("ADMIN_QUAN_LY", "/admin/payments/21"), "/admin/payments/21");
  assert.equal(loginDestination("ADMIN_QUAN_LY", "/admin/reports?type=payments"),
    "/admin/reports?type=payments");
  assert.equal(loginDestination("HOC_VIEN", "/courses/3"), "/courses/3");
});

test("only the backend email-verification response enters the verification flow", () => {
  assert.equal(requiresEmailVerification({ status: 403, data: { message: "email not verified" } }), true);
  assert.equal(requiresEmailVerification({ status: 403, data: { message: "Account is disabled." } }), false);
  assert.equal(requiresEmailVerification({ status: 401, data: { message: "email not verified" } }), false);
});

test("email verification preserves only an allowlisted return destination", () => {
  const values = new Map();
  const storage = {
    setItem: (key, value) => values.set(key, value),
    removeItem: key => values.delete(key),
  };

  assert.equal(preservePendingAuthReturnTo("/courses", storage), "/courses");
  assert.equal(values.get("pendingAuthReturnTo"), "/courses");
  assert.equal(loginDestination("HOC_VIEN", values.get("pendingAuthReturnTo")), "/courses");

  assert.equal(preservePendingAuthReturnTo("https://attacker.example/courses", storage), null);
  assert.equal(values.has("pendingAuthReturnTo"), false);
});

test("successful email verification cannot fall back to registration before login navigation", () => {
  const verifyPage = readFileSync(new URL("../pages/auth/VerifyEmailPage.jsx", import.meta.url), "utf8");
  const loginPage = readFileSync(new URL("../pages/auth/LoginPage.jsx", import.meta.url), "utf8");

  assert.match(verifyPage, /useState\(\(\) => sessionStorage\.getItem\("pendingVerificationEmail"\)\)/);
  assert.match(verifyPage, /navigate\("\/login", \{ replace: true, state:/);
  assert.match(verifyPage, /notice: "Email đã được xác thực\. Bạn có thể đăng nhập\.", email,/);
  assert.match(loginPage, /typeof location\.state\?\.email === "string" \? location\.state\.email : ""/);
});
