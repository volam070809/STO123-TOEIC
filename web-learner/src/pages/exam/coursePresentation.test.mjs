import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { CourseCover } = await vite.ssrLoadModule("/src/pages/exam/coursePresentation.jsx");
const { default: DisclosureToggle } = await vite.ssrLoadModule("/src/components/layout/DisclosureToggle.jsx");

test("course cover uses its own configured image before any fallback", () => {
  const html = renderToStaticMarkup(React.createElement(CourseCover, {
    course: { stage: 1, name: "TOEIC Foundation", coverImageUrl: "/api/public/course-images/cover.jpg" },
  }));
  assert.match(html, /src="\/api\/public\/course-images\/cover\.jpg"/);
  assert.match(html, /alt="Ảnh bìa khóa học TOEIC Foundation"/);
  assert.doesNotMatch(html, /course-cover-fallback|images\/home\/foundation\.jpg/);
});

test("course cover shows an honest placeholder when no image is configured", () => {
  const html = renderToStaticMarkup(React.createElement(CourseCover, {
    course: { stage: 2, name: "TOEIC Development", coverImageUrl: null },
  }));
  assert.match(html, /Ảnh bìa đang cập nhật/);
  assert.doesNotMatch(html, /<img|images\/home\/listening\.jpg/);
});

test("shared disclosure action includes both states and a chevron", () => {
  const html = renderToStaticMarkup(React.createElement(DisclosureToggle));
  assert.match(html, /Xem chi tiết/);
  assert.match(html, /Thu gọn/);
  assert.match(html, /<svg/);
});
