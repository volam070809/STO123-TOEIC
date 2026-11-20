import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { default: ExamQuestionForm } = await vite.ssrLoadModule("/src/pages/admin/ExamQuestionForm.jsx");
const { default: ExamGroupEdit } = await vite.ssrLoadModule("/src/pages/admin/ExamGroupEdit.jsx");
const { default: ExamContent } = await vite.ssrLoadModule("/src/pages/exam/ExamContent.jsx");
const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));

test("Part 3 manual exam creation offers one shared image alongside audio", () => {
  const html = render(ExamQuestionForm, {
    token: "test", examId: 42, fixedBank: "DE_THI", fixedPart: true, initialPart: 3,
    onClose() {}, onSaved() {}
  });
  assert.match(html, /Audio MP3/);
  assert.match(html, /Hình ảnh/);
  assert.equal((html.match(/type="file"/g) || []).length, 2);
  assert.equal((html.match(/class="exam-author-question"/g) || []).length, 3);
});

test("Part 3 group edit retains the stored image and offers replacement and removal", () => {
  const html = render(ExamGroupEdit, {
    token: "test", onCancel() {}, onSaved() {},
    unit: { part: 3, bank: "DE_THI", groupId: 14, documents: [],
      resource: { text: "Conversation", audio: "question-media/audio.mp3",
        image: "question-media/dialogue.png" } }
  });
  assert.match(html, /question-media\/dialogue\.png/);
  assert.match(html, /Gỡ ảnh/);
  assert.match(html, /Audio MP3/);
  assert.equal((html.match(/type="file"/g) || []).length, 2);
});

test("learner Part 3 attempt displays its group image and audio", () => {
  const html = render(ExamContent, {
    attemptId: 9, token: "test", group: { groupId: 14, part: 3, hasImage: true,
      hasAudio: true, documents: [], questions: [] }
  });
  assert.match(html, /Đang tải hình ảnh/);
  assert.match(html, /Âm thanh câu hỏi/);
});
