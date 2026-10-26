import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
const { PracticeGroup, PracticeQuestion, PracticeQuestionNumbers, QuestionNavigation } =
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

test("See Answer hides Check; Check locks only that question", () => {
  const viewed = render(PracticeQuestion, { question: question(5), value: "B",
    result: { selectedOption: "B", viewed: true, correctOption: "A", explanation: "Why" },
    onNext: () => {} });
  assert.match(viewed, /Đáp án đúng/);
  assert.doesNotMatch(viewed, /Kiểm tra câu này/);
  assert.match(viewed, /Giải thích/);
  assert.match(viewed, /Why/);
  assert.match(viewed, /Làm lại câu này/);
  assert.match(viewed, /Câu tiếp →/);
  assert.doesNotMatch(viewed, /checked=""[^>]*disabled=""/);
  const completed = render(PracticeQuestion, { question: question(5), value: "B",
    result: { selectedOption: "B", checked: true, isCorrect: false, correctOption: "A" },
    onNext: () => {} });
  assert.match(completed, /Bạn trả lời chưa đúng/);
  assert.match(completed, /Xem đáp án/);
  assert.match(completed, /Câu tiếp →/);
  assert.doesNotMatch(completed, /Đáp án đúng/);
  assert.match(completed, /Làm lại câu này/);
  assert.doesNotMatch(completed, /Kiểm tra câu này/);
});

test("each question state renders exactly one consistent action row", () => {
  const baseProps = { question: question(5), onNext: () => {} };
  const initialEmpty = render(PracticeQuestion, baseProps);
  assert.equal((initialEmpty.match(/class="practice-part-actions"/g) || []).length, 1);
  assert.match(initialEmpty, /Xem đáp án/);
  assert.match(initialEmpty, /practice-check-button" disabled/);
  assert.doesNotMatch(initialEmpty, /Làm lại câu này|Câu tiếp →/);

  const initialSelected = render(PracticeQuestion, { ...baseProps, value: "B",
    result: { checked: false, selectedOption: "B" } });
  assert.equal((initialSelected.match(/class="practice-part-actions"/g) || []).length, 1);
  assert.match(initialSelected, /Xem đáp án/);
  assert.doesNotMatch(initialSelected, /practice-check-button" disabled/);

  const checked = render(PracticeQuestion, { ...baseProps, value: "B",
    result: { checked: true, selectedOption: "B", isCorrect: false, correctOption: "A" } });
  assert.equal((checked.match(/class="practice-part-actions"/g) || []).length, 1);
  assert.match(checked, /Xem đáp án/);
  assert.match(checked, /Làm lại câu này/);
  assert.match(checked, /Câu tiếp →/);
  assert.doesNotMatch(checked, /Kiểm tra câu này/);

  const viewed = render(PracticeQuestion, { ...baseProps, value: "B",
    result: { viewed: true, selectedOption: "B", correctOption: "A", explanation: "Why" } });
  assert.equal((viewed.match(/class="practice-part-actions"/g) || []).length, 1);
  assert.match(viewed, /Làm lại câu này/);
  assert.match(viewed, /Câu tiếp →/);
  assert.doesNotMatch(viewed, /Xem đáp án|Kiểm tra câu này/);

  const retried = render(PracticeQuestion, { ...baseProps,
    result: { checked: false, selectedOption: null } });
  assert.equal((retried.match(/class="practice-part-actions"/g) || []).length, 1);
  assert.match(retried, /Xem đáp án/);
  assert.match(retried, /practice-check-button" disabled/);
  assert.doesNotMatch(retried, /Làm lại câu này|Câu tiếp →/);
});

test("Check reports only correctness until View answer reveals answer and explanation", () => {
  const source = { ...question(1), noiDung: "Hidden spoken question", phuongAnA: "Hidden choice A" };
  const base = { ...question(1), noiDung: null, phuongAnA: null, phuongAnB: null,
    phuongAnC: null, phuongAnD: null };
  const checkedResult = { selectedOption: "B", checked: true, isCorrect: false,
    correctOption: "A", explanation: "Hidden explanation", reveal: source };

  const checkedHtml = render(PracticeQuestion, { question: base, value: "B", result: checkedResult,
    onNext: () => {} });
  assert.match(checkedHtml, /Bạn trả lời chưa đúng/);
  assert.match(checkedHtml, /Xem đáp án/);
  assert.match(checkedHtml, /Câu tiếp →/);
  assert.equal((checkedHtml.match(/data-incorrect="true"/g) || []).length, 1);
  assert.doesNotMatch(checkedHtml, /Đáp án đúng|Hidden explanation|Hidden spoken question|data-correct/);

  const revealedHtml = render(PracticeQuestion, { question: base, value: "B",
    result: { ...checkedResult, viewed: true } });
  assert.match(revealedHtml, /Đáp án đúng/);
  assert.match(revealedHtml, /Hidden explanation/);
  assert.match(revealedHtml, /Hidden spoken question/);
  assert.equal((revealedHtml.match(/data-correct="true"/g) || []).length, 1);
  assert.equal((revealedHtml.match(/data-incorrect="true"/g) || []).length, 1);
});

test("A correct checked answer turns green without revealing explanation or transcript", () => {
  const source = { ...question(1), noiDung: "Hidden correct transcript" };
  const base = { ...question(1), noiDung: null };
  const checkedHtml = render(PracticeQuestion, { question: base, value: "A",
    result: { selectedOption: "A", checked: true, isCorrect: true,
      correctOption: "A", explanation: "Hidden correct explanation", reveal: source } });

  assert.match(checkedHtml, /Bạn trả lời đúng/);
  assert.equal((checkedHtml.match(/data-correct="true"/g) || []).length, 1);
  assert.doesNotMatch(checkedHtml, /data-incorrect|Đáp án đúng|Hidden correct explanation|Hidden correct transcript/);
});

for (const part of [1, 2, 3, 4, 5, 6, 7]) {
  test(`Part ${part}: sidebar jumps 1 → 6 → 2 without unlocking and preserves group data and answers`, () => {
    const groupSize = part === 3 || part === 4 ? 3 : part === 6 ? 4 : part === 7 ? 3 : 1;
    const groups = [10, 20, 30, 40, 50, 60].map((groupId, index) => ({ groupId,
      audioUrl: part <= 4 ? `/group-${groupId}/audio` : null,
      imageUrl: part === 1 ? `/group-${groupId}/image` : null,
      context: `Context for group ${groupId}`,
      documents: part === 7 ? Array.from({ length: index === 1 ? 3 : 1 }, (_, i) =>
        ({ order: i + 1, type: "TEXT", content: `Group ${groupId} passage ${i + 1}` })) : []
    }));
    const questions = Array.from({ length: 6 }, (_, i) => ({ ...question(part, i + 1),
      groupId: part === 5 ? null : groups[Math.floor(i / groupSize)].groupId }));
    const feedback = { 2: { selectedOption: "B", checked: false } };
    const original = structuredClone({ questions, groups, feedback });
    let current = 0;
    const props = () => ({ questions, feedback, current, onChange: index => { current = index; } });
    const buttons = () => PracticeQuestionNumbers(props()).props.children.map(li => li.props.children);
    assert.ok(buttons().every(button => !button.props.disabled));
    assert.ok(buttons().every(button => button.props.type === "button"));
    buttons()[5].props.onClick();
    assert.equal(current, 5);
    assert.equal(buttons()[5].props["aria-current"], "step");
    assert.equal(buttons()[0].props["aria-current"], undefined);
    const target = questions[current];
    const group = groups.find(g => g.groupId === target.groupId);
    assert.equal(group?.groupId, part === 5 ? undefined : groups[Math.floor(5 / groupSize)].groupId);
    assert.match(render(PracticeQuestion, { question: target }), /Câu 6/);
    if (group) {
      const source = render(PracticeGroup, { group, part });
      assert.equal((source.match(/Đang tải âm thanh/g) || []).length, part <= 4 ? 1 : 0);
      if (part > 2) assert.match(source, new RegExp(group.context));
      if (part === 7) {
        assert.equal((source.match(/class="exam-document"/g) || []).length, 3);
        assert.match(source, /Group 20 passage 3/);
        assert.doesNotMatch(source, /Group 10 passage/);
      }
    }
    buttons()[1].props.onClick();
    assert.equal(current, 1);
    assert.equal(buttons()[1].props["aria-current"], "step");
    const selected = render(PracticeQuestion, { question: questions[current], value: feedback[2].selectedOption });
    assert.match(selected, /Câu 2/);
    assert.match(selected, /checked=""/);
    assert.deepEqual({ questions, groups, feedback }, original);
    const html = render(PracticeQuestionNumbers, props());
    assert.equal((html.match(/type="button"/g) || []).length, 6);
    assert.doesNotMatch(html, /disabled|chưa mở/);
  });
}

test("sidebar keeps all status types and Redo states navigable", () => {
  const questions = [1, 2, 3, 4].map(id => question(5, id));
  const feedback = { 1: { checked: true, isCorrect: true },
    2: { checked: true, isCorrect: false }, 3: { viewed: true } };
  let current = 0;
  const props = () => ({ questions, feedback, current, onChange: index => { current = index; } });
  const buttons = () => PracticeQuestionNumbers(props()).props.children.map(li => li.props.children);
  assert.deepEqual(buttons().map(b => b.props["data-state"]), ["correct", "incorrect", "viewed", "unchecked"]);
  feedback[1] = { checked: false, selectedOption: null };
  buttons()[3].props.onClick();
  assert.equal(current, 3);
  assert.ok(buttons().every(button => !button.props.disabled));
  assert.equal(buttons()[0].props["data-state"], "unchecked");
});

test.after(async () => { await vite.close(); });
