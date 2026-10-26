import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { groupVocabularyTopics, hasVocabularyPracticeAccuracy, learnerDetailParams, learnerDetailTab,
  mockScoreTrend, positivePage, practiceInsights } from "./adminLearnerDetailState.js";

test("learner analytics accepts only five known deep-linked tabs", () => {
  assert.equal(learnerDetailTab("vocabulary"), "vocabulary");
  assert.equal(learnerDetailTab("practice"), "practice");
  assert.equal(learnerDetailTab("exams"), "exams");
  assert.equal(learnerDetailTab("courses"), "courses");
  assert.equal(learnerDetailTab("unsafe"), "overview");
});

test("learner analytics preserves tab filters and normalizes pagination", () => {
  const params = learnerDetailParams("tab=practice&part=1", { practicePage: 2 });
  assert.equal(params.get("tab"), "practice");
  assert.equal(params.get("part"), "1");
  assert.equal(params.get("practicePage"), "2");
  assert.equal(positivePage("-3"), 1);
  assert.equal(positivePage("4"), 4);
});

test("learner detail loads analytics by selected tab and keeps management actions", () => {
  const source = readFileSync(new URL("./AdminLearnerDetailPage.jsx", import.meta.url), "utf8");
  assert.match(source, /activeTab === "vocabulary"/);
  assert.match(source, /adminLearnerApi\.practice/);
  assert.match(source, /adminLearnerApi\.exams/);
  assert.match(source, /adminLearnerApi\.courses/);
  assert.match(source, /onAction\("lock"\)/);
  assert.match(source, /onAction\("delete"\)/);
  assert.match(source, /<details className="[^"]*admin-analytics-disclosure/);
  assert.match(source, /role="progressbar"/);
  assert.match(source, /ScoreTrend/);
  assert.match(source, /value={`\$\{data\.topicsStarted\}\/\$\{data\.topics\.length\}`}/);
  assert.match(source, /admin-vocabulary-inactive/);
  assert.match(source, /hasVocabularyPracticeAccuracy\(topic\)/);
});

test("practice insights use only real attempted-part accuracy", () => {
  const insights = practiceInsights([
    { part: 1, totalAttempts: 0, accuracy: 100 },
    { part: 2, totalAttempts: 3, accuracy: 82 },
    { part: 3, totalAttempts: 2, accuracy: 47 },
  ]);
  assert.equal(insights.strongest.part, 2);
  assert.equal(insights.weakest.part, 3);
  assert.deepEqual(practiceInsights([]), { strongest: null, weakest: null });
});

test("mock score trend keeps completed real scores in chronological order", () => {
  const trend = mockScoreTrend([
    { attemptId: 3, completedAt: "2026-03-03", totalScore: 700 },
    { attemptId: 2, completedAt: null, totalScore: 650 },
    { attemptId: 1, completedAt: "2026-01-01", totalScore: 500 },
  ]);
  assert.deepEqual(trend.map(point => point.score), [500, 700]);
});

test("vocabulary topics are grouped by the existing learning status", () => {
  const topics = [
    { topicId: 1, learningStatus: "IN_PROGRESS" },
    { topicId: 2, learningStatus: "NOT_STARTED" },
    { topicId: 3, learningStatus: "REMEMBERED" },
  ];
  const groups = groupVocabularyTopics(topics);
  assert.deepEqual(groups.started.map(topic => topic.topicId), [1, 3]);
  assert.deepEqual(groups.inactive.map(topic => topic.topicId), [2]);
});

test("vocabulary accuracy appears only with an actual answered denominator", () => {
  assert.equal(hasVocabularyPracticeAccuracy({ practiceAttempts: 1, practiceCorrect: 0,
    practiceIncorrect: 0, practiceAccuracy: null }), false);
  assert.equal(hasVocabularyPracticeAccuracy({ practiceAttempts: 1, practiceCorrect: 0,
    practiceIncorrect: 2, practiceAccuracy: 0 }), true);
});
