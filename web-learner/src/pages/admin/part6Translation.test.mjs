import assert from "node:assert/strict";
import test from "node:test";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { default: ExamQuestionForm } = await vite.ssrLoadModule("/src/pages/admin/ExamQuestionForm.jsx");
const { default: ExamGroupEdit } = await vite.ssrLoadModule("/src/pages/admin/ExamGroupEdit.jsx");
const { default: QuestionContentPreview } = await vite.ssrLoadModule("/src/pages/admin/QuestionContentPreview.jsx");
const { PracticeGroup } = await vite.ssrLoadModule("/src/pages/practice/PracticePartsPage.jsx");
const { default: ExamContent } = await vite.ssrLoadModule("/src/pages/exam/ExamContent.jsx");
const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));

test("Part 6 creation has one full passage translation field", () => {
  const html = render(ExamQuestionForm, { token: "test", fixedPart: true, initialPart: 6,
    onClose() {}, onSaved() {} });
  assert.equal((html.match(/Bản dịch toàn bộ đoạn văn/g) || []).length, 1);
  assert.equal((html.match(/Bản dịch câu hỏi/g) || []).length, 4);
});

test("Part 6 edit reload and admin preview retain passage translation and blank markers", () => {
  const unit = { part: 6, bank: "DE_THI", groupId: 7, documents: [],
    resource: { text: "Line one [1]\nLine two [2] [3] [4]",
      translation: "[1] Câu 1\n[2] Câu 2\n[3] Câu 3\n[4] Câu 4",
      passageTranslation: "Dòng một [1]\nDòng hai [2] [3] [4]" },
    questions: Array.from({ length: 4 }, (_, index) => ({ id: index + 1 })) };
  const edit = render(ExamGroupEdit, { token: "test", unit, onCancel() {}, onSaved() {} });
  const preview = render(QuestionContentPreview, { token: "test", unit });
  assert.match(edit, /Dòng một \[1\]/);
  assert.match(edit, /Dòng hai \[2\] \[3\] \[4\]/);
  assert.match(preview, /Dòng một \[1\]/);
  assert.match(preview, /Bản dịch câu hỏi/);
});

test("Part 6 translation renders only when supplied by permitted feedback or review", () => {
  const original = { groupId: 7, part: 6, context: "Original [1]", documents: [], questions: [] };
  const translated = { ...original, passageTranslation: "Dịch [1]\nDịch [2]" };
  assert.doesNotMatch(render(PracticeGroup, { group: original, part: 6 }), /Dịch \[1\]/);
  assert.match(render(PracticeGroup, { group: translated, part: 6 }), /Dịch \[1\]/);
  assert.doesNotMatch(render(ExamContent, { group: translated, attemptId: 9, token: "test" }), /Dịch \[1\]/);
  const review = render(ExamContent, { group: translated, attemptId: 9, token: "test", review: true });
  assert.match(review, /Dịch \[1\]/);
  assert.match(review, /exam-preserve-lines/);
});
