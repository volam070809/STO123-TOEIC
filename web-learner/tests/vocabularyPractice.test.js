import test from "node:test";
import assert from "node:assert/strict";
import { buildPracticeQuestions, practiceReducer, practiceResult } from "../src/pages/practice/vocabularyPractice.js";

const words = [
  { maTuVung: 11, word: "appointment", meaning: "cuộc hẹn" },
  { maTuVung: 23, word: "meeting", meaning: "cuộc họp" },
  { maTuVung: 37, word: "document", meaning: "tài liệu" },
  { maTuVung: 41, word: "colleague", meaning: "đồng nghiệp" },
  { maTuVung: 59, word: "schedule", meaning: "lịch trình" }
];
const normalize = value => value.trim().normalize("NFKC").replace(/\s+/g, " ").toLowerCase();

test("each target occurs once, both question types have four unique same-topic options", () => {
  const questions = buildPracticeQuestions([...words, words[0]], words);
  assert.equal(questions.length, words.length);
  assert.equal(new Set(questions.map(q => q.word.maTuVung)).size, words.length);
  assert.equal(new Set(questions.map(q => q.type)).size, 2);
  for (const question of questions) {
    const key = question.type === "word-to-meaning" ? "meaning" : "word";
    assert.equal(question.options.length, 4);
    assert.equal(new Set(question.options.map(normalize)).size, 4);
    assert.equal(question.options[question.correctIndex], question.word[key]);
    assert.ok(question.options.every(option => words.some(word => word[key] === option)));
  }
});

test("review/retry subsets only create target questions but retain full-topic distractors", () => {
  const questions = buildPracticeQuestions([words[2]], words);
  assert.deepEqual(questions.map(q => q.word.maTuVung), [37]);
  assert.equal(questions[0].options.length, 4);
  assert.equal(buildPracticeQuestions([], words).length, 0);
});

test("synonyms, homographs, and whitespace variants do not produce two valid answers", () => {
  const pool = [...words,
    { maTuVung: 70, word: "coworker", meaning: "đồng nghiệp" },
    { maTuVung: 71, word: "  COLLEAGUE ", meaning: "Bạn cùng làm việc" },
    { maTuVung: 72, word: "peer", meaning: "Bạn cùng làm việc" }
  ];
  for (const random of [() => 0, () => 0.999]) {
    for (const question of buildPracticeQuestions(pool, pool, random)) {
      const promptKey = question.type === "word-to-meaning" ? "word" : "meaning";
      const answerKey = question.type === "word-to-meaning" ? "meaning" : "word";
      const valid = new Set(pool.filter(w => normalize(w[promptKey]) === normalize(question.prompt))
        .map(w => normalize(w[answerKey])));
      assert.equal(question.options.filter(option => valid.has(normalize(option))).length, 1);
      assert.equal(new Set(question.options.map(normalize)).size, question.options.length);
    }
  }
});

test("small or unusable topics are safe, using two/three options when available", () => {
  for (const size of [2, 3]) {
    const pool = words.slice(0, size);
    const questions = buildPracticeQuestions(pool, pool);
    assert.equal(questions.length, size);
    assert.ok(questions.every(q => q.options.length === size));
  }
  assert.deepEqual(buildPracticeQuestions([words[0]], [words[0]]), []);
  assert.deepEqual(buildPracticeQuestions([], []), []);
  assert.deepEqual(buildPracticeQuestions([{ maTuVung: 1, word: " ", meaning: "" }], words), []);
});

test("answer order varies with the random source without mutating source data", () => {
  const before = structuredClone(words);
  const first = buildPracticeQuestions([words[0]], words, () => 0);
  const second = buildPracticeQuestions([words[0]], words, () => 0.999);
  assert.notDeepEqual(first[0].options, second[0].options);
  assert.deepEqual(words, before);
});

test("answers lock on the first selection and score/wrong-word retry are correct", () => {
  const questions = buildPracticeQuestions(words, words, () => 0.999);
  let session = practiceReducer(null, { type: "start", questions });
  assert.equal(practiceReducer(session, { type: "next" }), session);
  assert.equal(practiceReducer(session, { type: "answer", optionIndex: -1 }), session);
  for (let i = 0; i < questions.length; i += 1) {
    const optionIndex = i < 3 ? questions[i].correctIndex : (questions[i].correctIndex + 1) % 4;
    session = practiceReducer(session, { type: "answer", optionIndex });
    assert.equal(practiceReducer(session, { type: "answer", optionIndex: (optionIndex + 1) % 4 }), session);
    session = practiceReducer(session, { type: "next" });
  }
  assert.equal(session.finished, true);
  const result = practiceResult(session);
  assert.equal(result.correct, 3);
  assert.equal(result.incorrect, 2);
  assert.equal(result.percent, 60);
  assert.deepEqual(result.wrongWords.map(w => w.maTuVung), [41, 59]);
  const retry = buildPracticeQuestions(result.wrongWords, words);
  assert.deepEqual(new Set(retry.map(q => q.word.maTuVung)), new Set([41, 59]));
  assert.equal(practiceReducer(null, { type: "start", questions: retry }).answers.length, 0);
});
