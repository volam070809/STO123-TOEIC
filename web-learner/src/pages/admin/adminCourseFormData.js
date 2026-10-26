import { courseStatuses } from "./adminFormat.js";

export function buildCourseImageFormData(data, file) {
  const form = new FormData();
  form.set("Name", data.name);
  form.set("Stage", String(data.stage));
  form.set("MaxTargetScore", String(data.maxTargetScore));
  if (data.retailPrice !== null) form.set("RetailPrice", String(data.retailPrice));
  if (data.description) form.set("Description", data.description);
  form.set("Status", data.status);
  form.set("File", file);
  return form;
}

export function validateCourse(values) {
  const errors = {};
  const name = values.name.trim();
  const stage = Number(values.stage);
  const score = Number(values.maxTargetScore);
  const price = values.retailPrice === "" ? null : Number(values.retailPrice);
  const image = values.coverImageUrl.trim();
  if (!name || name.length > 128) errors.name = "Tên khóa học phải có từ 1 đến 128 ký tự.";
  if (!Number.isInteger(stage) || stage < 1 || stage > 3) errors.stage = "Giai đoạn phải từ 1 đến 3.";
  if (!Number.isInteger(score) || score < 10 || score > 990) errors.maxTargetScore = "Điểm mục tiêu phải từ 10 đến 990.";
  if (price !== null && (!Number.isSafeInteger(price) || price <= 0 || price > 9_999_999_999))
    errors.retailPrice = "Giá phải là số nguyên dương và không vượt quá 9.999.999.999 đồng.";
  if (values.description.trim().length > 512) errors.description = "Mô tả tối đa 512 ký tự.";
  if (!Object.hasOwn(courseStatuses, values.status)) errors.status = "Trạng thái không hợp lệ.";
  if (image.length > 512 || (image && !safeImagePath(image)))
    errors.coverImageUrl = "Đường dẫn ảnh phải là đường dẫn tương đối hoặc URL HTTP(S).";
  return errors;
}

function safeImagePath(value) {
  if (value.startsWith("/")) return !value.startsWith("//");
  try { return ["http:", "https:"].includes(new URL(value).protocol); }
  catch { return false; }
}
