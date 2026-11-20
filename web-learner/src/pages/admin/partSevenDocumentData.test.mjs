import test from "node:test";
import assert from "node:assert/strict";
import { documentIssues, documentPayload, documentTypes, fromDocument, moveItem,
  newDocument, partSevenIssues } from "./partSevenDocumentData.js";

test("Part 7 structured legacy documents round trip without changing untouched fields", () => {
  const cases = [
    ["TABLE", { version: 1, title: "Prices", headers: ["A", "B"],
      rows: [["1", "2"], ["3", "4"]], legacy: { keep: true } }],
    ["EMAIL", { version: 1, from: "a@example.com", to: "b@example.com",
      date: "today", subject: "Hello", body: "Message", legacy: "keep" }],
    ["CHAT", { version: 1, messages: [
      { sender: "A", time: "09:00", text: "One", extra: 1 },
      { sender: "B", time: "09:01", text: "Two" }], legacy: "keep" }],
    ["FORM", { version: 1, title: "Signup",
      fields: [{ label: "Name", value: "Lan", extra: "keep" }], legacy: "keep" }]
  ];
  for (const [type, value] of cases) {
    const content = JSON.stringify(value, null, 2);
    const loaded = fromDocument({ type, content, image: null });
    assert.equal(documentPayload(loaded).content, content);
    const edited = { ...loaded, title: type === "TABLE" || type === "FORM" ? "Edited" : loaded.title };
    const parsed = JSON.parse(documentPayload(edited).content);
    assert.deepEqual(parsed.legacy, value.legacy);
    if (type === "CHAT") assert.deepEqual(parsed.messages, value.messages);
    if (type === "TABLE") assert.deepEqual(parsed.rows, value.rows);
  }
});

test("Part 7 TEXT and IMAGE retain their native values", () => {
  assert.deepEqual(documentPayload(fromDocument({ type: "TEXT", content: "Line 1\nLine 2" })),
    { type: "TEXT", content: "Line 1\nLine 2", imagePath: null });
  assert.deepEqual(documentPayload(fromDocument({ type: "IMAGE", image: "part7/image.png" })),
    { type: "IMAGE", content: null, imagePath: "part7/image.png" });
});

test("Unparseable legacy document is marked for blocking instead of converting", () => {
  assert.equal(fromDocument({ type: "TABLE", content: "legacy-table" })._parseError, true);
});

test("all Part 7 templates keep structure and attached image through create and edit", () => {
  assert.equal(Object.keys(documentTypes).length, 15);
  for (const type of ["LETTER", "ARTICLE", "NOTICE", "POLICY", "INVOICE", "WEB", "AD", "SCHEDULE", "SURVEY"]) {
    const item = { ...newDocument(type), title: "Source", imagePath: "part7/photo.png" };
    const payload = documentPayload(item);
    const loaded = fromDocument({ type, content: payload.content, image: payload.imagePath });
    assert.equal(loaded.title, "Source");
    assert.equal(loaded.imagePath, "part7/photo.png");
    assert.deepEqual(JSON.parse(documentPayload(loaded).content), JSON.parse(payload.content));
  }
  assert.equal(documentPayload({ ...newDocument("TEXT"), content: "Text", imagePath: "part7/photo.png" }).imagePath,
    "part7/photo.png");
});

test("Part 7 editors detect incomplete documents and question explanations", () => {
  const question = { text: "Question?", a: "A", b: "B", c: "C", d: "D", explanation: "Why" };
  const issues = partSevenIssues([newDocument("IMAGE")], [question, { ...question, explanation: "" }]);
  assert.match(issues.join(" "), /cần tải hình ảnh/);
  assert.match(issues.join(" "), /cần nhập giải thích/);
  assert.deepEqual(documentIssues({ ...newDocument("INVOICE"), title: "Invoice", rows: [["Tea", "1", "$2"]] }, 0), []);
  assert.deepEqual(moveItem(["one", "two", "three"], 0, 1), ["two", "one", "three"]);
});
