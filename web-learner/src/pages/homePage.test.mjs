import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { default: HomePage, HomeFeatureGrid } = await vite.ssrLoadModule("/src/pages/HomePage.jsx");
const { AuthContext } = await vite.ssrLoadModule("/src/contexts/AuthState.js");
const { GuestAuthPromptContext } = await vite.ssrLoadModule("/src/contexts/GuestAuthPromptState.js");
const expectedFeatures = [
  ["Kiểm tra đầu vào", "/placement"], ["Thi thử TOEIC", "/mock-test"],
  ["Luyện tập", "/practice"], ["Học từ vựng", "/vocabulary"],
  ["Khóa học", "/courses"], ["Cấu trúc đề thi TOEIC", "/toeic/structure"],
];

function renderHome(user) {
  const auth = { user, token: user ? "test-token" : null, loading: false, sessionError: "",
    loadCurrentUser: () => {}, logout: () => {} };
  return renderToStaticMarkup(React.createElement(AuthContext.Provider, { value: auth },
    React.createElement(GuestAuthPromptContext.Provider, { value: { requireAuthentication: () => false } },
      React.createElement(MemoryRouter, null, React.createElement(HomePage)))));
}

function renderFeatures(props) {
  return renderToStaticMarkup(React.createElement(GuestAuthPromptContext.Provider,
    { value: { requireAuthentication: () => false } }, React.createElement(MemoryRouter, null,
      React.createElement(HomeFeatureGrid, props))));
}

test("guest and learner use the correct role-specific homepage sections", () => {
  const guest = renderHome(null);
  const learner = renderHome({ hoTen: "Nguyễn An" });
  assert.match(guest, /Học TOEIC cùng STO123/);
  assert.match(guest, /Xây dựng hành trình học phù hợp/);
  assert.doesNotMatch(guest, /Chào mừng,|Giai đoạn \d+|lần đã hoàn thành|đề đã hoàn thành/);
  assert.match(learner, /Chào mừng, Nguyễn An/);
  assert.doesNotMatch(learner, /Đăng nhập để bắt đầu/);
});

test("shared feature cards keep the required order and valid existing routes", () => {
  const html = renderFeatures();
  let previousIndex = -1;
  for (const [title, route] of expectedFeatures) {
    const titleIndex = html.indexOf(`<h3>${title}</h3>`);
    assert.ok(titleIndex > previousIndex, `${title} phải đúng thứ tự`);
    assert.match(html, new RegExp(`href="${route.replaceAll("/", "\\/")}"`));
    previousIndex = titleIndex;
  }
  assert.equal(expectedFeatures.length, 6);
});

test("TOEIC structure is available from both shared card variants", () => {
  const guest = renderFeatures();
  const learner = renderFeatures({ learner: true, placement: { completedCount: 2 },
    mock: [{ state: "COMPLETED" }] });
  for (const html of [guest, learner]) {
    assert.match(html, /Cấu trúc đề thi TOEIC/);
    assert.match(html, /href="\/toeic\/structure"/);
  }
  assert.match(learner, /2 lần đã hoàn thành/);
  assert.match(learner, /1 đề đã hoàn thành/);
});

test("guest overview cards navigate without opening the protected-action dialog", () => {
  const guest = renderFeatures();
  const learner = renderFeatures({ learner: true });
  assert.doesNotMatch(guest, /aria-haspopup="dialog"/);
  assert.doesNotMatch(learner, /aria-haspopup="dialog"/);
});
