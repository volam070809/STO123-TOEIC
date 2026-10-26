import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { default: PracticeChoosePage } = await vite.ssrLoadModule("/src/pages/practice/PracticeChoosePage.jsx");
const { default: PracticePartsOverviewPage } = await vite.ssrLoadModule("/src/pages/practice/PracticePartsOverviewPage.jsx");
const { default: PlacementPage } = await vite.ssrLoadModule("/src/pages/exam/PlacementPage.jsx");
const { default: MockHomePage } = await vite.ssrLoadModule("/src/pages/exam/MockHomePage.jsx");
const { AuthContext } = await vite.ssrLoadModule("/src/contexts/AuthState.js");
const { GuestAuthPromptContext } = await vite.ssrLoadModule("/src/contexts/GuestAuthPromptState.js");

const guestAuth = {
  token: null, user: null, isAuthenticated: false, loading: false, sessionError: "",
  loadCurrentUser: () => {}, logout: () => {},
};

function renderGuest(Component) {
  return renderToStaticMarkup(React.createElement(AuthContext.Provider, { value: guestAuth },
    React.createElement(GuestAuthPromptContext.Provider,
      { value: { requireAuthentication: () => false, close: () => {} } },
      React.createElement(MemoryRouter, null, React.createElement(Component)))));
}

test("guest can render Practice categories and the Part selector without protected content", () => {
  const landing = renderGuest(PracticeChoosePage);
  const parts = renderGuest(PracticePartsOverviewPage);
  assert.match(landing, /Luyện tập từ vựng/);
  assert.match(landing, /Luyện tập TOEIC theo Part/);
  assert.match(parts, /Part 7 · Reading Comprehension/);
  assert.match(parts, /aria-haspopup="dialog"/);
  assert.doesNotMatch(parts, /Đáp án đúng|Giải thích|transcript|attemptId/);
});

test("guest can render general Placement roadmap without personal KNN data", () => {
  const html = renderGuest(PlacementPage);
  assert.match(html, /Lộ trình TOEIC ba giai đoạn/);
  assert.match(html, /Foundation/);
  assert.match(html, /Development/);
  assert.match(html, /Advanced/);
  assert.match(html, /aria-haspopup="dialog"/);
  assert.doesNotMatch(html, /Kết quả gần nhất|Lịch sử kiểm tra đầu vào|Tiếp tục kiểm tra/);
});

test("guest can render Mock Test overview and no personal history action", () => {
  const html = renderGuest(MockHomePage);
  assert.match(html, /Thi thử TOEIC/);
  assert.match(html, /Đang tải danh sách đề/);
  assert.doesNotMatch(html, /Lịch sử thi|Tiếp tục làm bài|Xem kết quả/);
});

test("overview routes are public while attempts and private history stay protected", () => {
  const routes = readFileSync(new URL("../routes/AppRoutes.jsx", import.meta.url), "utf8");
  assert.match(routes, /path="\/practice" element={<PracticeRoute \/>}/);
  assert.match(routes, /path="\/mock-test" element={<MockHomePage \/>}/);
  assert.match(routes, /path="\/placement" element={<PlacementPage \/>}/);
  assert.match(routes, /path="\/exam\/:attemptId" element={<ProtectedRoute>/);
  assert.match(routes, /path="\/mock-test\/history" element={<ProtectedRoute>/);
  assert.match(routes, /params\.has\("attempt"\).*params\.has\("review"\)/s);
});
