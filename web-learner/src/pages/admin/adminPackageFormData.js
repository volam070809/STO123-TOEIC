import { courseStatuses } from "./adminFormat.js";

export function validatePackage(values) {
  const errors = {};
  const price = Number(values.price);
  const days = Number(values.validDays);
  if (!values.name.trim() || values.name.trim().length > 64) errors.name = "Tên gói học phải có từ 1 đến 64 ký tự.";
  if (!Number.isSafeInteger(price) || price <= 0 || price > 9_999_999_999) errors.price = "Giá phải là số nguyên dương và không vượt quá 9.999.999.999 đồng.";
  if (!Number.isSafeInteger(days) || days <= 0) errors.validDays = "Thời hạn phải là số nguyên dương.";
  if (values.description.trim().length > 512) errors.description = "Mô tả tối đa 512 ký tự.";
  if (!Object.hasOwn(courseStatuses, values.status)) errors.status = "Trạng thái không hợp lệ.";
  if (!values.courseIds.length) errors.courseIds = "Hãy chọn ít nhất một khóa học.";
  if (new Set(values.courseIds).size !== values.courseIds.length) errors.courseIds = "Khóa học không được chọn trùng.";
  return errors;
}
