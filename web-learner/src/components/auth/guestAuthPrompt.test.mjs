import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { GuestAuthDialog } = await vite.ssrLoadModule("/src/components/auth/GuestAuthPrompt.jsx");
const { authorizeProtectedAction, guestAuthPromptReducer, isGuestAuthDismissKey } =
  await vite.ssrLoadModule("/src/components/auth/guestAuthPromptPolicy.js");

test("guest protected action opens one prompt and never executes the operation", () => {
  let executions = 0;
  const decision = authorizeProtectedAction(false, "/placement", () => { executions += 1; });
  assert.deepEqual(decision, { allowed: false, returnTo: "/placement" });
  assert.equal(executions, 0);
  assert.deepEqual(guestAuthPromptReducer(null, { type: "open", returnTo: decision.returnTo }),
    { returnTo: "/placement" });
});

test("authenticated learner bypasses the prompt and keeps the protected operation", () => {
  let executions = 0;
  const decision = authorizeProtectedAction(true, "/practice", () => { executions += 1; });
  assert.deepEqual(decision, { allowed: true, returnTo: null });
  assert.equal(executions, 1);
});

test("unsafe return destination cannot open the prompt", () => {
  assert.deepEqual(authorizeProtectedAction(false, "https://attacker.example", () => {}),
    { allowed: false, returnTo: null });
});

test("close button and Escape both close the shared prompt state", () => {
  const open = { returnTo: "/mock-test" };
  assert.equal(guestAuthPromptReducer(open, { type: "close" }), null);
  assert.equal(isGuestAuthDismissKey("Escape"), true);
  assert.equal(isGuestAuthDismissKey("Enter"), false);
});

test("dialog exposes accessible semantics and deliberate Login/Register navigation", () => {
  const html = renderToStaticMarkup(React.createElement(MemoryRouter, null,
    React.createElement(GuestAuthDialog, { returnTo: "/placement", onClose: () => {} })));
  assert.match(html, /role="dialog"/);
  assert.match(html, /aria-modal="true"/);
  assert.match(html, /Đăng nhập để tiếp tục/);
  assert.match(html, /Vui lòng đăng nhập hoặc đăng ký tài khoản STO123/);
  assert.match(html, /aria-label="Đóng hộp thoại"/);
  assert.match(html, /href="\/login"/);
  assert.match(html, /href="\/register"/);
});
