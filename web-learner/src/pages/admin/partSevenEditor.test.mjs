import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
test.after(async () => { await vite.close(); });
const { default: ExamQuestionForm } = await vite.ssrLoadModule("/src/pages/admin/ExamQuestionForm.jsx");
const { default: ExamGroupEdit } = await vite.ssrLoadModule("/src/pages/admin/ExamGroupEdit.jsx");
const { default: PartSevenLivePreview } = await vite.ssrLoadModule("/src/pages/admin/PartSevenLivePreview.jsx");
const { default: ExamContent } = await vite.ssrLoadModule("/src/pages/exam/ExamContent.jsx");
const { default: DocumentRenderer } = await vite.ssrLoadModule("/src/pages/exam/DocumentRenderer.jsx");
const { default: PartSevenTranslationView } = await vite.ssrLoadModule("/src/pages/exam/PartSevenTranslationView.jsx");
const { documentTypes, newDocument } = await vite.ssrLoadModule("/src/pages/admin/partSevenDocumentData.js");
const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));

test("Part 7 creation offers templates, translation, questions, draft and live learner preview", () => {
  const html = render(ExamQuestionForm, { token: "invalid", fixedBank: "DE_THI", initialPart: 7,
    onClose() {}, onSaved() {} });
  for (const label of Object.values(documentTypes)) assert.ok(html.includes(label), label);
  for (const text of ["Bản dịch tiếng Việt", "Xem trước giao diện học viên", "Lưu nháp",
    "Thêm câu Part 7", "Thêm tài liệu", "Giải thích", "Cần hoàn thiện trước khi lưu"])
    assert.ok(html.includes(text), text);
  assert.equal((html.match(/class="exam-author-question"/g) || []).length, 2);
});

test("Part 7 editing preserves a saved image and exposes removal and preview", () => {
  const html = render(ExamGroupEdit, { token: "invalid", onCancel() {}, onSaved() {},
    unit: { part: 7, bank: "DE_THI", groupId: 91, resource: { text: "", translation: "" },
      documents: [{ type: "TABLE", content: JSON.stringify({ version: 1, title: "Prices",
        headers: ["Item", "Cost"], rows: [["Tea", "$2"]] }), image: "part7/table.png" }],
      questions: [{ id: 1, text: "How much?", a: "$2", b: "$3", c: "$4", d: "$5" }] } });
  assert.match(html, /part7\/table\.png/);
  assert.match(html, /Gỡ ảnh/);
  assert.match(html, /Xem trước giao diện học viên/);
  assert.match(html, /Tea/);
});

test("all ETS style layouts render without HTML input and keep document image zoom", () => {
  for (const type of ["LETTER", "ARTICLE", "NOTICE", "POLICY", "INVOICE", "WEB", "AD", "SCHEDULE", "SURVEY"]) {
    const source = { ...newDocument(type), title: `${type} heading`, paragraphs: ["First paragraph"],
      items: ["First item"], rows: [["09:00", "Meeting", "$2"]] };
    const html = render(DocumentRenderer, { document: { order: 1, type, content: source,
      imageUrl: "/api/attempts/1/groups/2/documents/1/image" }, token: "test" });
    assert.match(html, new RegExp(`${type} heading`));
    assert.match(html, /Phóng to hình tài liệu/);
    assert.match(html, /Đang tải hình ảnh/);
  }
  const preview = render(PartSevenLivePreview, { documents: [{ ...newDocument("IMAGE"),
    imagePath: "part7/photo.png" }], questions: [{ text: "Question?", a: "One", b: "Two", c: "Three", d: "Four" }],
    token: "test", bank: "DE_THI" });
  assert.match(preview, /Question\?/);
  assert.match(preview, /Phóng to hình tài liệu/);
});

test("learner Part 7 attempt opens documents next to questions", () => {
  const html = render(ExamContent, { token: "test", attemptId: 1,
    group: { part: 7, groupId: 2, documents: [{ order: 1, type: "TEXT", content: "Passage" }],
      questions: [{ attemptQuestionId: 9, part: 7, order: 1, text: "Question?", a: "A", b: "B", c: "C", d: "D" }] },
    onFlag() {}, onAnswer() {} });
  assert.match(html, /exam-part7-content/);
  assert.match(html, /class="exam-context is-open"/);
  assert.match(html, /Passage/);
  assert.match(html, /Question\?/);
});

test("Part 7 review renders a translated article with aligned paragraphs", () => {
  const original = { version: 1, title: "Company news", subtitle: "", footer: "",
    metadata: [], paragraphs: ["First paragraph"], items: [], headers: [], rows: [] };
  const translated = { ...original, title: "Tin công ty", paragraphs: ["Đoạn thứ nhất"] };
  const value = "P7DOCV1:" + JSON.stringify({ version: 1, kind: "PART7_TRANSLATIONS",
    documents: [{ type: "ARTICLE", content: translated }] });
  const html = render(PartSevenTranslationView, { translation: value,
    documents: [{ order: 1, type: "ARTICLE", content: original }] });
  assert.match(html, /Tin công ty/);
  assert.match(html, /Đoạn thứ nhất/);
});
