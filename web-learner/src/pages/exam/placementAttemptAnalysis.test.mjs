import test from "node:test";
import assert from "node:assert/strict";
import { coursesForKnnStage, historicalPlacementAnalysis, placementAnalysisForAttempt, shouldLoadPlacementAnalysis, waitForPlacementAnalysis } from "./placementAttemptAnalysis.js";

const latest = (attemptId, stage, classification = "AVAILABLE") =>
  ({ result: { attemptId }, classification, stage, targetScore: null });

test("an immediate KNN result belongs to the requested attempt", async () => {
  const outcome = latest(42, 2);
  const api = { result: async () => outcome,
    history: async () => { throw new Error("History should not be requested"); } };
  assert.deepEqual(await waitForPlacementAnalysis("42", "token", api),
    { status: "available", stage: 2, outcome, current: true });
});

test("a delayed KNN result is polled without starting classification again", async () => {
  let calls = 0;
  const api = { result: async () => ++calls < 3 ? latest(42, null, "UNAVAILABLE") : latest(42, 2) };
  const waits = [];
  const answer = await waitForPlacementAnalysis("42", "token", api,
    { attempts: 5, intervalMs: 1500, sleep: ms => { waits.push(ms); } });
  assert.equal(answer.stage, 2);
  assert.equal(calls, 3);
  assert.deepEqual(waits, [1500, 1500]);
});

test("missing classification stops after a bounded number of reads", async () => {
  let calls = 0;
  const api = { result: async () => { calls += 1; return latest(42, null, "UNAVAILABLE"); } };
  assert.deepEqual(await waitForPlacementAnalysis("42", "token", api,
    { attempts: 3, sleep: () => {} }), { status: "missing" });
  assert.equal(calls, 3);
});

test("a request failure remains an error for the UI retry action", async () => {
  const api = { result: async () => { throw new Error("network"); } };
  await assert.rejects(waitForPlacementAnalysis("42", "token", api), /network/);
});

test("an older attempt uses its saved stage, never the latest KNN stage", async () => {
  const calls = [];
  const api = { result: async () => latest(42, 3),
    history: async (_token, page, size) => {
      calls.push([page, size]);
      return page === 1 ? { items: [{ attemptId: 42, stage: 3 }], hasMore: true }
        : { items: [{ attemptId: 19, stage: 1 }], hasMore: false };
    } };
  assert.deepEqual(await placementAnalysisForAttempt("19", "token", api),
    { status: "available", stage: 1, current: false });
  assert.deepEqual(calls, [[1, 20], [2, 20]]);
});

test("an older attempt without a saved stage never borrows another stage", async () => {
  const api = { result: async () => latest(42, 3),
    history: async () => ({ items: [{ attemptId: 19, stage: null }], hasMore: false }) };
  assert.deepEqual(await placementAnalysisForAttempt("19", "token", api), { status: "missing" });
});

test("historical Part analysis uses only that attempt's seven Part stats", () => {
  const result = { attemptId: 19, parts: [10, 20, 30, 40, 50, 60, 70].map((percentage, index) =>
    ({ part: index + 1, stats: { correct: percentage / 10, total: 10, percentage } })) };
  const analysis = historicalPlacementAnalysis(result, 1);
  assert.equal(analysis.result.attemptId, 19);
  assert.equal(analysis.stage, 1);
  assert.equal(analysis.summary.strongestPart, 7);
  assert.equal(analysis.summary.weakestPart, 1);
  assert.equal(analysis.summary.listeningPercentage, 25);
  assert.equal(analysis.summary.readingPercentage, 60);
});

test("MOCK and a mismatched route do not start placement analysis", () => {
  assert.equal(shouldLoadPlacementAnalysis({ source: "MOCK", attemptId: 42 }, "42"), false);
  assert.equal(shouldLoadPlacementAnalysis({ source: "PLACEMENT", attemptId: 42 }, "19"), false);
  assert.equal(shouldLoadPlacementAnalysis({ source: "PLACEMENT", attemptId: 42 }, "42"), true);
});

test("each saved KNN stage exposes its matching real course, including a locked course", () => {
  const courses = [
    { courseId: 11, stage: 1, accessStatus: "LOCKED", recommended: false },
    { courseId: 22, stage: 2, accessStatus: "LOCKED", recommended: true },
    { courseId: 33, stage: 3, accessStatus: "LOCKED", recommended: true },
  ];
  for (const [stage, courseId] of [[1, 11], [2, 22], [3, 33]]) {
    assert.deepEqual(coursesForKnnStage(courses, stage).map(course => course.courseId), [courseId]);
  }
});

test("an old attempt selects its own stage even when the latest course recommendation differs", () => {
  const courses = [{ courseId: 11, stage: 1, recommended: false },
    { courseId: 33, stage: 3, recommended: true }];
  assert.deepEqual(coursesForKnnStage(courses, 1).map(course => course.courseId), [11]);
});

test("multiple courses in one KNN stage remain separate choices", () => {
  const courses = [
    { courseId: 21, stage: 2, accessStatus: "LOCKED" },
    { courseId: 22, stage: 2, accessStatus: "ACTIVE" },
    { courseId: 31, stage: 3, accessStatus: "LOCKED" },
  ];
  assert.deepEqual(coursesForKnnStage(courses, 2).map(course => course.courseId), [21, 22]);
});

test("missing KNN or missing stage course cannot create roadmap choices", () => {
  const courses = [{ courseId: 22, stage: 2, contentCount: 0 }];
  assert.deepEqual(coursesForKnnStage(courses, null), []);
  assert.deepEqual(coursesForKnnStage(courses, 1), []);
  assert.deepEqual(coursesForKnnStage(courses, 2).map(course => course.courseId), [22]);
});
