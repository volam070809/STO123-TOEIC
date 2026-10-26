import { apiRequest } from "./api";

export const publicCourseApi = {
  list: () => apiRequest("/api/public/courses", { cache: "no-store" }),
  detail: courseId => apiRequest(`/api/public/courses/${courseId}`, { cache: "no-store" }),
};
