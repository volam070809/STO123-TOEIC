import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { buildCourseImageFormData, courseImageUpdatePayload, validateCourse } from "./adminCourseFormData.js";

const valid = {
  name: "TOEIC Giai đoạn 1", stage: "1", maxTargetScore: "450", retailPrice: "99000",
  description: "Nền tảng TOEIC", status: "DANG_MO", coverImageUrl: "/images/course-1.webp",
};

test("course form accepts mapped course fields", () => {
  assert.deepEqual(validateCourse(valid), {});
  assert.deepEqual(validateCourse({ ...valid, retailPrice: "", coverImageUrl: "https://cdn.example.test/course.webp" }), {});
});

test("course form rejects invalid schema and payment values", () => {
  const errors = validateCourse({ ...valid, name: "", stage: "4", maxTargetScore: "991",
    retailPrice: "12.5", status: "UNKNOWN", coverImageUrl: "javascript:alert(1)" });
  assert.ok(errors.name); assert.ok(errors.stage); assert.ok(errors.maxTargetScore);
  assert.ok(errors.retailPrice); assert.ok(errors.status); assert.ok(errors.coverImageUrl);
});

test("course image upload uses multipart fields without persisting a local path", () => {
  const file = new File([new Uint8Array([0xff, 0xd8, 0xff, 0xff, 0xd9])], "course.jpg", { type: "image/jpeg" });
  const form = buildCourseImageFormData({
    name: "TOEIC 1", stage: 1, maxTargetScore: 450, retailPrice: 99000,
    description: "Nền tảng TOEIC", status: "DANG_MO", coverImageUrl: "blob:local-preview",
  }, file);

  assert.equal(form.get("Name"), "TOEIC 1");
  assert.equal(form.get("Stage"), "1");
  assert.equal(form.get("RetailPrice"), "99000");
  assert.equal(form.get("File").name, "course.jpg");
  assert.equal(form.has("CoverImageUrl"), false);
});

test("cover removal changes only the course image reference", () => {
  const course = { ...valid, courseId: 7, coverImageUrl: "/api/public/course-images/old.jpg" };
  const payload = courseImageUpdatePayload(course, null);
  assert.equal(payload.coverImageUrl, null);
  assert.equal(payload.name, course.name);
  assert.equal(payload.stage, course.stage);
  assert.equal(payload.status, course.status);
  assert.equal(payload.retailPrice, course.retailPrice);
  assert.equal(payload.description, course.description);
  assert.equal(Object.hasOwn(payload, "courseId"), false);
});

test("course form exposes upload and real-library choices instead of a manual path input", () => {
  const source = readFileSync(new URL("./AdminCourseForm.jsx", import.meta.url), "utf8");
  assert.match(source, /Tải ảnh từ máy/);
  assert.match(source, /Chọn ảnh có sẵn/);
  assert.match(source, /type="file"/);
  assert.doesNotMatch(source, /placeholder="\/images\/course\.webp/);
});
