import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
const { PracticeGroup, PracticeQuestion, QuestionNavigation } =
  await vite.ssrLoadModule("/src/pages/practice/PracticePartsPage.jsx");

const render = (Component, props) => renderToStaticMarkup(React.createElement(Component, props));
const question = (part, id = 1) => ({ part, questionOccurrenceId: id, thuTu: id,
  noiDung: `Question ${id}`, phuongAnA: "Choice A", phuongAnB: "Choice B",
  phuongAnC: "Choice C", phuongAnD: part === 2 ? null : "Choice D" });

test("Parts 1 and 2 hide spoken choices and keep Check disabled", () => {
  for (const part of [1, 2]) {
    const html = render(PracticeQuestion, { question: { ...question(part), noiDung: null,
      phuongAnA: null, phuongAnB: null, phuongAnC: null, phuongAnD: null } });
    assert.match(html, /Kiểm tra câu này/);
    assert.match(html, /Xem đáp án/);
    assert.match(html, /practice-check-button" disabled/);
    assert.equal((html.match(/type="radio"/g) || []).length, part === 1 ? 4 : 3);
  }
});

test("Parts 1 and 2 show one transcript after reveal and in Review", () => {
  for (const part of [1, 2]) {
    const source = { ...question(part), noiDung: "Spoken question" };
    const group = { groupId: 3, context: "Spoken question; Choice A; Choice B", documents: [] };
    const active = render(PracticeGroup, { group, part }) + render(PracticeQuestion, {
      question: { ...source, noiDung: null, phuongAnA: null, phuongAnB: null },
      result: { viewed: true, correctOption: "A", reveal: source, explanation: "Why" },
      transcriptContext: group.context
    });
    assert.equal((active.match(/Spoken question/g) || []).length, 1);
    assert.equal((active.match(/Choice A/g) || []).length, 1);
    const review = render(PracticeGroup, { group, part }) + render(PracticeQuestion, {
      question: { ...source, correctOption: "A" }, transcriptContext: group.context, review: true
    });
    assert.equal((review.match(/Spoken question/g) || []).length, 1);
    assert.equal((review.match(/Choice A/g) || []).length, 1);
  }
});

test("Parts 3 and 4 render one shared audio source for multiple questions", () => {
  for (const part of [3, 4]) {
    const group = render(PracticeGroup, { group: { groupId: 9, audioUrl: "/audio", documents: [] }, part });
    const html = group + render(PracticeQuestion, { question: question(part, 1) }) +
      render(PracticeQuestion, { question: question(part, 2) });
    assert.equal((html.match(/Đang tải âm thanh/g) || []).length, 1);
    assert.equal((html.match(/practice-part-question/g) || []).length, 2);
  }
});

test("Part 5 is independent; Part 6 passage is shared", () => {
  assert.match(render(PracticeQuestion, { question: question(5) }), /Question 1/);
  const group = render(PracticeGroup, { group: { groupId: 7, context: "First line\n[1] blank", documents: [] }, part: 6 });
  assert.equal((group.match(/First line/g) || []).length, 1);
  assert.match(group, /practice-part-context/);
  assert.doesNotMatch(render(PracticeQuestion, { question: question(6) }), /Chỗ trống/);
});

test("Part 7 renders one, two, and three documents in saved order", () => {
  for (const count of [1, 2, 3]) {
    const documents = Array.from({ length: count }, (_, index) =>
      ({ order: index + 1, type: "TEXT", content: `Passage ${index + 1}` }));
    const html = render(PracticeGroup, { group: { groupId: 8, documents }, part: 7 });
    assert.equal((html.match(/class="exam-document"/g) || []).length, count);
    for (let i = 1; i <= count; i++) assert.ok(html.indexOf(`Passage ${i}`) <
      (i < count ? html.indexOf(`Passage ${i + 1}`) : Infinity));
  }
});

test("Part 7 preserves structured EMAIL, TABLE, CHAT, FORM and IMAGE rendering", () => {
  const documents = [
    { order: 1, type: "EMAIL", content: { subject: "Meeting", body: "Email body" } },
    { order: 2, type: "TABLE", content: { title: "Prices", headers: ["Item"], rows: [["Ticket"]] } },
    { order: 3, type: "CHAT", content: { messages: [{ sender: "Alex", text: "Hello" }] } },
    { order: 4, type: "FORM", content: { title: "Request", fields: [{ label: "Name", value: "Sam" }] } },
    { order: 5, type: "IMAGE", imageUrl: "/image" }
  ];
  const html = render(PracticeGroup, { group: { groupId: 8, documents }, part: 7 });
  for (const text of ["Meeting", "Email body", "Prices", "Ticket", "Alex", "Hello", "Request", "Sam", "Đang tải hình ảnh"])
    assert.match(html, new RegExp(text));
  assert.equal((html.match(/class="exam-document"/g) || []).length, 5);
});

test("Next is disabled until Check or See Answer", () => {
  assert.match(render(QuestionNavigation, { current: 0, total: 2, canPrevious: false, canNext: false }),
    /disabled=""[^>]*>Câu tiếp/);
  assert.doesNotMatch(render(QuestionNavigation, { current: 0, total: 2, canPrevious: false, canNext: true }),
    /disabled=""[^>]*>Câu tiếp/);
});

test("See Answer still permits selection; Check locks only that question", () => {
  const viewed = render(PracticeQuestion, { question: question(5), value: "B",
    result: { selectedOption: "B", viewed: true, correctOption: "A", explanation: "Why" },
    onNext: () => {} });
  assert.match(viewed, /Đã xem đáp án/);
  assert.match(viewed, /Kiểm tra câu này/);
  assert.match(viewed, /Giải thích: Why/);
  assert.match(viewed, /Làm lại câu này/);
  assert.match(viewed, /Câu tiếp →/);
  assert.doesNotMatch(viewed, /checked=""[^>]*disabled=""/);
  const completed = render(PracticeQuestion, { question: question(5), value: "B",
    result: { selectedOption: "B", checked: true, isCorrect: false, correctOption: "A" } });
  assert.match(completed, /Làm lại câu này/);
  assert.doesNotMatch(completed, /Kiểm tra câu này/);
});

test.after(async () => { await vite.close(); });
