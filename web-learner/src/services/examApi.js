import { apiRequest, API_BASE_URL } from "./api";

const root = "/api/attempts/";
export const examApi = {
  fixed: token => apiRequest("/api/mock-tests", { token }),
  summary: token => apiRequest("/api/mock-tests/summary", { token }),
  parts: token => apiRequest("/api/mock-tests/parts", { token }),
  fixedSummary: token => apiRequest("/api/mock-tests/fixed-summary", { token }),
  history: (token, { mode = "ALL", part = null, examId = null, page = 1, pageSize = 10 } = {}) => {
    const params = new URLSearchParams({ mode, page: String(page), pageSize: String(pageSize) });
    if (part != null) params.set("part", String(part));
    if (examId != null) params.set("examId", String(examId));
    return apiRequest("/api/mock-tests/history?" + params, { token });
  },
  active: token => apiRequest("/api/mock-tests/active", { token }),
  start: (source, examId, token, part = null) => apiRequest("/api/mock-tests/start",
    { method: "POST", body: { source, examId, part }, token }),
  attempt: (id, token) => apiRequest(root + id, { token }),
  answer: (id, questionId, selectedOption, token) => apiRequest(root + id + "/answers/" + questionId,
    { method: "PUT", body: { selectedOption }, token }),
  flag: (id, questionId, flagged, token) => apiRequest(root + id + "/flags/" + questionId,
    { method: "PUT", body: { flagged }, token }),
  submit: (id, token) => apiRequest(root + id + "/submit", { method: "POST", token }),
  pause: (id, token) => apiRequest(root + id + "/pause", { method: "POST", token }),
  resume: (id, token) => apiRequest(root + id + "/resume", { method: "POST", token }),
  heartbeat: (id, token) => apiRequest(root + id + "/heartbeat", { method: "POST", token }),
  result: (id, token) => apiRequest(root + id + "/result", { token }),
  review: (id, token) => apiRequest(root + id + "/review", { token }),
};

export const placementApi = {
  state: token => apiRequest("/api/placement", { token }),
  summary: token => apiRequest("/api/placement/summary", { token }),
  history: (token, page = 1, pageSize = 10) => apiRequest(
    `/api/placement/history?page=${page}&pageSize=${pageSize}`, { token }),
  start: token => apiRequest("/api/placement/start", { method: "POST", token }),
  result: token => apiRequest("/api/placement/result", { token }),
  target: (targetScore, token) => apiRequest("/api/placement/target",
    { method: "PUT", body: { targetScore }, token }),
  courses: token => apiRequest("/api/placement/courses", { token }),
  course: (id, token) => apiRequest("/api/placement/courses/" + id, { token }),
};

export async function examMediaBlob(endpoint, token, signal) {
  const response = await fetch(API_BASE_URL + endpoint, { headers: { Authorization: "Bearer " + token }, signal });
  if (!response.ok) throw new Error("Không thể tải media.");
  return response.blob();
}
