import test from "node:test";
import assert from "node:assert/strict";
import { answerViewed, canNavigateTo, checked, restoredFeedback } from "./practiceFlow.mjs";

test("one checked question unlocks only the next question, even in a shared group", () => {
  const questions = [1, 2, 3, 4].map(id => ({ questionOccurrenceId: id, groupId: id < 4 ? 10 : 20 }));
  const feedback = { 1: { checked: true, selectedOption: "A" } };
  assert.equal(canNavigateTo(questions, feedback, 1), true);
  assert.equal(canNavigateTo(questions, feedback, 2), false);
  feedback[2] = { checked: true, selectedOption: "B" };
  assert.equal(canNavigateTo(questions, feedback, 3), false);
  feedback[3] = { checked: true, selectedOption: "C" };
  assert.equal(canNavigateTo(questions, feedback, 3), true);
  feedback[2] = { checked: false, selectedOption: null }; // Redo a previously checked question.
  assert.equal(canNavigateTo(questions, feedback, 3), false);
});

test("See Answer preserves selection and never marks a question checked", () => {
  const state = answerViewed({ selectedOption: "B" }, { correctOption: "A", explanation: "Why" });
  assert.equal(state.selectedOption, "B");
  assert.equal(state.viewed, true);
  assert.equal(state.checked, undefined);
  const questions = [1, 2].map(questionOccurrenceId => ({ questionOccurrenceId }));
  assert.equal(canNavigateTo(questions, { 1: state }, 1), true);
  assert.equal(canNavigateTo(questions, { 1: { checked: false, selectedOption: null } }, 1), false);
});

test("resume restores independent checked state by occurrence ID", () => {
  const questions = [{ questionOccurrenceId: 10, isChecked: true, selectedOption: "C",
    correctOption: "C", isCorrect: true }, { questionOccurrenceId: 11, isChecked: false }];
  const state = restoredFeedback(questions);
  assert.equal(checked(questions[0], state), true);
  assert.equal(checked(questions[1], state), false);
  assert.equal(state[10].selectedOption, "C");
  assert.equal(state[11], undefined);
});
