import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
const { default: PracticeHistoryDashboard } = await vite.ssrLoadModule("/src/pages/practice/PracticeHistoryDashboard.jsx");
const render = overrides => renderToStaticMarkup(React.createElement(PracticeHistoryDashboard, {
  history: { summary: { totalCompleted: 21, totalQuestions: 140, totalAnswered: 100,
    totalCorrect: 62, overallAccuracy: 44.3 }, total: 11, pageSize: 8,
    items: [{ attemptId: 79, part: 7, correct: 2, attempted: 9, total: 14,
      accuracy: 14.3, status: "DA_NOP", finishedAt: "2026-10-08T03:00:00Z" }] },
  loading: false, error: "", partFilter: 0, sort: "NEWEST", page: 1,
  onPartChange() {}, onSortChange() {}, onPageChange() {}, onReview() {}, onRetry() {}, ...overrides
}));

test("summary represents full authorized history and row shows separate correct and answered counts", () => {
  const html = render();
  assert.match(html, /Bài đã hoàn thành/);
  assert.match(html, />21<\/strong>/);
  assert.match(html, /44,3%/);
  assert.match(html, />100<\/strong>/);
  assert.match(html, /Số câu đúng \/ tổng số câu/);
  assert.match(html, /2\/14/);
  assert.match(html, /9\/14/);
  assert.match(html, /14,3%/);
  assert.match(html, /Trang 1\/2/);
  assert.match(html, /aria-pressed="true"[^>]*>Tất cả/);
  assert.match(html, /value="OLDEST"/);
  assert.equal((html.match(/Xem lại/g) || []).length, 2); // desktop and mobile views
  assert.doesNotMatch(html, /questionOccurrenceId|phuongAnDung|explanation/);
});

test("loading, filtered empty, and failure each show a clear state", () => {
  assert.match(render({ history: null, loading: true }), /Đang tải lịch sử luyện tập/);
  assert.match(render({ history: { summary: { totalCompleted: 21, totalAnswered: 100,
    overallAccuracy: 44.3 }, total: 0, pageSize: 8, items: [] }, partFilter: 6 }),
    /Chưa có bài Part 6 đã hoàn thành/);
  assert.match(render({ error: "HISTORY_LOAD_FAILED" }), /Không thể tải lịch sử luyện tập/);
  assert.match(render({ error: "HISTORY_LOAD_FAILED" }), /Thử lại/);
});

test.after(async () => { await vite.close(); });
