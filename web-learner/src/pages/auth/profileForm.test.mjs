import test, { after } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";
import { applyProfileUpdate, prepareProfileUpdate, profileDraft } from "../../services/profileData.js";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
after(() => vite.close());
const { default: ProfilePage } = await vite.ssrLoadModule("/src/pages/auth/ProfilePage.jsx");
const { AuthContext } = await vite.ssrLoadModule("/src/contexts/AuthState.js");
const { authApi } = await vite.ssrLoadModule("/src/services/authApi.js");

const user = {
  maNguoiDung: 1, hoTen: "Người học", email: "learner@example.test", soDienThoai: "0912345678",
  vaiTro: "HOC_VIEN", trangThai: "HOAT_DONG", hasPassword: true, hasCustomAvatar: false,
};

function renderProfile(currentUser) {
  return renderToStaticMarkup(React.createElement(MemoryRouter, { initialEntries: ["/profile"] },
    React.createElement(AuthContext.Provider, { value: { user: currentUser, token: "test-token", avatarAsset: null } },
      React.createElement(ProfilePage))));
}

test("valid edit normalizes the two permitted fields", () => {
  assert.deepEqual(prepareProfileUpdate(user, { hoTen: "  Nguyễn An  ", soDienThoai: " +84912345678 " }), {
    payload: { hoTen: "Nguyễn An", soDienThoai: "+84912345678" }, error: "", changed: true,
  });
});

test("invalid name and phone are rejected before sending", () => {
  assert.ok(prepareProfileUpdate(user, { hoTen: "   ", soDienThoai: "0912345678" }).error);
  assert.ok(prepareProfileUpdate(user, { hoTen: "Tên hợp lệ", soDienThoai: "abc" }).error);
  assert.ok(prepareProfileUpdate(user, { hoTen: "x".repeat(65), soDienThoai: "" }).error);
});

test("cancel restores the server values; an unchanged draft needs no request", () => {
  const edited = { hoTen: "Tên khác", soDienThoai: "" };
  assert.equal(prepareProfileUpdate(user, edited).changed, true);
  const cancelled = profileDraft(user);
  assert.equal(prepareProfileUpdate(user, cancelled).changed, false);
  assert.equal(prepareProfileUpdate(user, { hoTen: " Người học ", soDienThoai: " 0912345678 " }).changed, false);
});

test("Google account has no password action and profile has no second logout", () => {
  const html = renderProfile({ ...user, hasPassword: false });
  assert.match(html, /Phương thức đăng nhập/);
  assert.match(html, /Google/);
  assert.doesNotMatch(html, /href="\/change-password"/);
  assert.equal((html.match(/>Đăng xuất</g) || []).length, 1);
  assert.match(html, /Chỉnh sửa thông tin/);
});

test("header and profile show the same updated name without another login", () => {
  const updated = applyProfileUpdate(user, { maNguoiDung: 1, hoTen: "Nguyễn An", soDienThoai: null });
  const html = renderProfile(updated);
  assert.ok((html.match(/Nguyễn An/g) || []).length >= 3);
  assert.doesNotMatch(html, /Người học/);
  assert.equal(updated.soDienThoai, null);
  assert.equal(applyProfileUpdate(user, { maNguoiDung: 2, hoTen: "Sai tài khoản", soDienThoai: null }), user);
});

test("profile API failure is reported and does not manufacture an updated user", async () => {
  const previousFetch = globalThis.fetch;
  let calls = 0;
  globalThis.fetch = async () => {
    calls += 1;
    return new Response(JSON.stringify({ message: "Không thể cập nhật hồ sơ." }), {
      status: 503, headers: { "content-type": "application/json" },
    });
  };
  try {
    await assert.rejects(authApi.updateProfile({ hoTen: "Tên khác", soDienThoai: null }, "test-token"),
      /Không thể cập nhật hồ sơ/);
    assert.equal(calls, 1);
    assert.equal(user.hoTen, "Người học");
  } finally { globalThis.fetch = previousFetch; }
});
