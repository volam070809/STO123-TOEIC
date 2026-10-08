import { apiRequest, API_BASE_URL } from "./api";

const root = "/api/attempts/";
let catalogInFlight = null;
let catalogToken = null;
function mockCatalog(token) {
  if (catalogInFlight && catalogToken === token) return catalogInFlight;
  catalogToken = token;
  const request = apiRequest("/api/mock-tests", { token });
  catalogInFlight = request;
  request.finally(() => { if (catalogInFlight === request) catalogInFlight = null; }).catch(() => {});
  return request;
}
const resumeInFlight = new Map();
function resumeAttempt(id, token) {
  const key = `${id}:${token}`;
  if (resumeInFlight.has(key)) return resumeInFlight.get(key);
  const request = apiRequest(root + id + "/resume", { method: "POST", token });
  resumeInFlight.set(key, request);
  request.finally(() => { if (resumeInFlight.get(key) === request) resumeInFlight.delete(key); }).catch(() => {});
  return request;
}
export const examApi = {
  catalog: mockCatalog,
  history: (token, { page = 1, pageSize = 10 } = {}) => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    return apiRequest("/api/mock-tests/history?" + params, { token });
  },
  start: (examId, token) => apiRequest("/api/mock-tests/start",
    { method: "POST", body: { examId }, token }),
  attempt: (id, token) => apiRequest(root + id, { token }),
  answer: (id, questionId, selectedOption, token) => apiRequest(root + id + "/answers/" + questionId,
    { method: "PUT", body: { selectedOption }, token }),
  flag: (id, questionId, flagged, token) => apiRequest(root + id + "/flags/" + questionId,
    { method: "PUT", body: { flagged }, token }),
  submit: (id, token) => apiRequest(root + id + "/submit", { method: "POST", token }),
  pause: (id, token) => apiRequest(root + id + "/pause", { method: "POST", token }),
  resume: resumeAttempt,
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

let cachedMediaToken = null;
const mediaCache = new Map();
export function examMediaBlob(endpoint, token) {
  if (cachedMediaToken !== token) { mediaCache.clear(); cachedMediaToken = token; }
  if (mediaCache.has(endpoint)) {
    const cached = mediaCache.get(endpoint);
    mediaCache.delete(endpoint);
    mediaCache.set(endpoint, cached);
    return cached;
  }
  const pending = fetch(API_BASE_URL + endpoint, { headers: { Authorization: "Bearer " + token } })
    .then(response => {
      if (!response.ok) throw new Error("Không thể tải media.");
      return response.blob();
    }).catch(error => {
      if (mediaCache.get(endpoint) === pending) mediaCache.delete(endpoint);
      throw error;
    });
  mediaCache.set(endpoint, pending);
  if (mediaCache.size > 16) mediaCache.delete(mediaCache.keys().next().value);
  return pending;
}
