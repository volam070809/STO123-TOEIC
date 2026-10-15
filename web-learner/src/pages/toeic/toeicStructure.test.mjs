import test from "node:test";
import assert from "node:assert/strict";
import { toeicParts, sectionParts, sectionQuestionCount } from "./toeicStructure.js";

test("public TOEIC guide matches the seven-Part exam contract", () => {
  assert.deepEqual(toeicParts.map(part => part.number), [1, 2, 3, 4, 5, 6, 7]);
  assert.deepEqual(toeicParts.map(part => part.questions), [6, 25, 39, 30, 30, 16, 54]);
  assert.equal(sectionParts("listening").length, 4);
  assert.equal(sectionParts("reading").length, 3);
  assert.equal(sectionQuestionCount("listening"), 100);
  assert.equal(sectionQuestionCount("reading"), 100);
  assert.equal(toeicParts.reduce((total, part) => total + part.questions, 0), 200);
  assert.ok(toeicParts.every(part => part.english && part.vietnamese && part.task && part.material));
});
