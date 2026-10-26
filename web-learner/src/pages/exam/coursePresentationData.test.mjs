import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { courseAudienceLabel, courseEntryReference, courseTargetLabel } from "./coursePresentationData.js";

test("entry stage and course target are presented as separate concepts", () => {
  assert.equal(courseAudienceLabel(1), "Dành cho học viên Giai đoạn 1");
  assert.equal(courseEntryReference(1), "Mức đầu vào tham khảo: Dưới 450");
  assert.equal(courseTargetLabel(555), "Mục tiêu tối đa: 555 điểm");
});

test("courses in the same stage keep independent target scores", () => {
  const courses = [
    { courseId: 11, stage: 1, maxTargetScore: 555 },
    { courseId: 12, stage: 1, maxTargetScore: 650 },
  ];

  assert.deepEqual(courses.map(course => ({
    courseId: course.courseId,
    audience: courseAudienceLabel(course.stage),
    entry: courseEntryReference(course.stage),
    target: courseTargetLabel(course.maxTargetScore),
  })), [
    { courseId: 11, audience: "Dành cho học viên Giai đoạn 1",
      entry: "Mức đầu vào tham khảo: Dưới 450", target: "Mục tiêu tối đa: 555 điểm" },
    { courseId: 12, audience: "Dành cho học viên Giai đoạn 1",
      entry: "Mức đầu vào tham khảo: Dưới 450", target: "Mục tiêu tối đa: 650 điểm" },
  ]);
});

test("presentation does not enforce a target inside the entry reference band", () => {
  assert.equal(courseEntryReference(1), "Mức đầu vào tham khảo: Dưới 450");
  assert.equal(courseTargetLabel(990), "Mục tiêu tối đa: 990 điểm");
});

test("course list, detail and Placement choices render entry and target independently", async () => {
  const sources = await Promise.all([
    readFile(new URL("./CourseListPage.jsx", import.meta.url), "utf8"),
    readFile(new URL("./CoursePage.jsx", import.meta.url), "utf8"),
    readFile(new URL("./PlacementAnalysisView.jsx", import.meta.url), "utf8"),
  ]);

  for (const source of sources) {
    assert.match(source, /courseAudienceLabel\(course\.stage\)/);
    assert.match(source, /courseEntryReference\(course\.stage\)/);
    assert.match(source, /courseTargetLabel\(course\.maxTargetScore\)/);
  }
});
