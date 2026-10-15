import test from "node:test";
import assert from "node:assert/strict";
import { hasRoadmapContent } from "./courseOutline.js";

test("a real course without Units, Lessons or Steps has no roadmap content", () => {
  assert.equal(hasRoadmapContent({ units: [] }), false);
  assert.equal(hasRoadmapContent({ units: [{ lessons: [] }] }), false);
  assert.equal(hasRoadmapContent({ units: [{ lessons: [{ contents: [] }] }] }), false);
});

test("a stored Step makes the course outline available", () => {
  assert.equal(hasRoadmapContent({ units: [{ lessons: [{ contents: [{ title: "Bài 1" }] }] }] }), true);
});
