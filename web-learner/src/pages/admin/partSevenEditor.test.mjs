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
const { default: DocumentRenderer } = await vite.ssrLoadModule("/src/pages/exam/DocumentRenderer.jsx");
const { default: PartSevenTranslationView } = await vite.ssrLoadModule("/src/pages/exam/PartSevenTranslationView.jsx");
const { studioTemplates, newStudioDocument, importStudioDocument, serializeStudioDocument,
  studioIssues, parseStudioTranslation, serializeStudioTranslation } =
  await vite.ssrLoadModule("/src/pages/admin/partSevenStudioData.js");
const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));

test("creation opens visual studio with 25 unique templates and three steps", () => {
  const html = render(ExamQuestionForm, { token: "invalid", fixedBank: "DE_THI", initialPart: 7,
    onClose() {}, onSaved() {} });
  assert.equal(studioTemplates.length, 25);
  assert.equal(new Set(studioTemplates.map(item => item.type)).size, 25);
  for (const label of ["Visual Document Studio", "01 Tài liệu", "02 Câu hỏi",
    "03 Xem như học viên", "Tiếng Việt", "Thêm nội dung", "Thêm tài liệu",
    "Thêm câu hỏi", "Bản nháp tự lưu"]) assert.ok(html.includes(label), label);
});

test("legacy table with image becomes editable blocks without losing data", () => {
  const source = { type: "TABLE", content: JSON.stringify({ version: 1, title: "Prices",
    headers: ["Item", "Cost"], rows: [["Tea", "$2"]], legacy: "keep" }), image: "part7/table.png" };
  const imported = importStudioDocument(source);
  assert.equal(imported.blocks.find(block => block.kind === "IMAGE")?.path, "part7/table.png");
  assert.equal(imported.blocks.find(block => block.kind === "TABLE")?.rows[0][0], "Tea");
  assert.equal(JSON.parse(serializeStudioDocument(imported).content).version, 2);
  assert.equal(JSON.parse(JSON.parse(serializeStudioDocument(imported).content).legacyV1).legacy, "keep");
  const html = render(ExamGroupEdit, { token: "invalid", onCancel() {}, onSaved() {},
    unit: { part: 7, bank: "DE_THI", groupId: 91, resource: { text: "", translation: "" },
      documents: [source], questions: [{ id: 1, text: "How much?", a: "$2", b: "$3",
        c: "$4", d: "$5" }, { id: 2, text: "What?", a: "A", b: "B", c: "C", d: "D" }] } });
  assert.match(html, /Tea/);
  assert.match(html, /Thay ảnh/);
  assert.match(html, /Gỡ ảnh/);
  assert.match(html, /Nhân bản nhóm/);
});

test("blank image requires upload before publication", () => {
  const image = newStudioDocument("IMAGE");
  const question = { text: "Question?", a: "A", b: "B", c: "C", d: "D", explanation: "Because" };
  assert.ok(studioIssues([image], [question, question]).some(issue => issue.includes("ảnh")));
  image.blocks[0].path = "part7/photo.png";
  assert.deepEqual(studioIssues([image], [question, question]), []);
});

test("version 2 translation stays aligned with document block IDs", () => {
  const doc = newStudioDocument("EMAIL");
  doc.blocks[4].text = "Hello";
  const translated = parseStudioTranslation(null, [doc]);
  translated.documents[0].blocks[4].text = "Xin chào";
  const encoded = serializeStudioTranslation(translated, [doc]);
  assert.match(encoded, /^P7DOCV2:/);
  assert.equal(parseStudioTranslation(encoded, [doc]).documents[0].blocks[4].id, doc.blocks[4].id);
  assert.equal(parseStudioTranslation(encoded, [doc]).documents[0].blocks[4].text, "Xin chào");
});

test("version 1 email translation migrates by field even when metadata is empty", () => {
  const original = importStudioDocument({ type: "EMAIL", content: JSON.stringify({ version: 1,
    from: "Manager", to: "Team", date: "Monday", subject: "News", body: "Hello" }) });
  const old = "P7DOCV1:" + JSON.stringify({ version: 1, kind: "PART7_TRANSLATIONS",
    documents: [{ type: "EMAIL", content: { version: 1, from: "", to: "", date: "",
      subject: "Tin", body: "Xin chào" } }] });
  const translated = parseStudioTranslation(old, [original]);
  const body = original.blocks.findIndex(block => block._legacy === "body");
  const subject = original.blocks.findIndex(block => block._legacy === "subject");
  assert.equal(translated.documents[0].blocks[body].text, "Xin chào");
  assert.equal(translated.documents[0].blocks[subject].value, "Tin");
  assert.ok(!serializeStudioDocument(original).content.includes("_legacy"));
});

test("learner renderer shows ordered blocks, table, multiple images and zoom", () => {
  const content = { version: 2, blocks: [
    { id: "h", kind: "HEADING", text: "Summer schedule" },
    { id: "t", kind: "TABLE", headers: ["Time", "Activity"], rows: [["09:00", "Meeting"]] },
    { id: "a", kind: "IMAGE", imageUrl: "/api/attempts/1/groups/2/documents/1/images/a", width: 80, align: "center" },
    { id: "b", kind: "IMAGE", imageUrl: "/api/attempts/1/groups/2/documents/1/images/b", width: 60, align: "right" }
  ] };
  const html = render(DocumentRenderer, { document: { order: 1, type: "SCHEDULE", content }, token: "test" });
  assert.match(html, /Summer schedule/);
  assert.match(html, /09:00/);
  assert.equal((html.match(/aria-label="Phóng to hình tài liệu"/g) || []).length, 2);
  assert.match(html, /width:80%/);
  const learner = render(ExamContent, { token: "test", attemptId: 1,
    group: { part: 7, groupId: 2, documents: [{ order: 1, type: "SCHEDULE", content }],
      questions: [{ attemptQuestionId: 9, part: 7, order: 1, text: "Question?", a: "A", b: "B", c: "C", d: "D" }] },
    onFlag() {}, onAnswer() {} });
  assert.match(learner, /exam-part7-content/);
  assert.match(learner, /Summer schedule/);
  assert.match(learner, /Question\?/);
});

test("numbered list blocks keep numbering across document rows", () => {
  const html = render(DocumentRenderer, { document: { order: 1, type: "POLICY", content: {
    version: 2, blocks: [{ id: "one", kind: "NUMBERED", text: "First rule" },
      { id: "two", kind: "NUMBERED", text: "Second rule" }] } } });
  assert.match(html, /<ol start="1"/);
  assert.match(html, /<ol start="2"/);
});

test("review displays version 2 Vietnamese translation", () => {
  const source = { version: 2, blocks: [{ id: "h", kind: "HEADING", text: "News" }] };
  const value = "P7DOCV2:" + JSON.stringify({ version: 2, kind: "PART7_TRANSLATIONS",
    documents: [{ type: "ARTICLE", blocks: [{ id: "h", kind: "HEADING", text: "Tin tức" }] }] });
  const html = render(PartSevenTranslationView, { translation: value,
    documents: [{ order: 1, type: "ARTICLE", content: source }] });
  assert.match(html, /Tin tức/);
});
