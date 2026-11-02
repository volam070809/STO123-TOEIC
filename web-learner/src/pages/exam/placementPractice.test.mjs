import test from "node:test";
import assert from "node:assert/strict";
import { placementPartNames, practicePriorities, strengthWeakness } from "./placementPractice.js";

test("practice priorities take the three lowest Part percentages in order", () => {
  const parts = [10, 70, 30, 80, 20, 60, 50].map((percentage, index) => ({
    part: index + 1, percentage,
  }));
  assert.deepEqual(practicePriorities(parts).map(item => item.part), [1, 5, 3]);
  assert.deepEqual(practicePriorities(parts).map(item => item.priority), [1, 2, 3]);
});

test("ties keep the smaller Part number first", () => {
  const parts = [50, 50, 50, 90, 90, 90, 90].map((percentage, index) => ({
    part: index + 1, percentage,
  }));
  assert.deepEqual(practicePriorities(parts).map(item => item.part), [1, 2, 3]);
});

test("strength is the highest Part and weakness is the lowest Part", () => {
  const parts = [10, 20, 30, 40, 50, 60, 70].map((percentage, index) => ({
    part: index + 1, percentage,
  }));
  const analysis = { parts, summary: { strongestPart: 7, weakestPart: 1 } };
  const pair = strengthWeakness(analysis);
  assert.equal(pair.strongest.part, 7);
  assert.equal(pair.weakest.part, 1);
  assert.equal(placementPartNames[7], "Reading Comprehension");
});
