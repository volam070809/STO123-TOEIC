import { apiRequest, API_BASE_URL } from "./api";
import { withCreationKey } from "./adminContentIdempotency.js";

const root = "/api/admin/content";
const course = id => `${root}/courses/${encodeURIComponent(id)}`;
const unit = (courseId, unitId) => `${course(courseId)}/units/${encodeURIComponent(unitId)}`;
const lesson = (courseId, unitId, lessonId) => `${unit(courseId, unitId)}/lessons/${encodeURIComponent(lessonId)}`;
const step = (courseId, unitId, lessonId, stepId) => `${lesson(courseId, unitId, lessonId)}/steps/${encodeURIComponent(stepId)}`;
const write = (path, method, body, token) => apiRequest(path, { method, body, token });
const deletionQuery = impact => impact ? `?${new URLSearchParams({ deleteHistory: "true",
  expectedProgress: String(impact.progressRecords), expectedAttempts: String(impact.attempts),
  expectedChecks: String(impact.checks) })}` : "";
const create = (path, body, token) => withCreationKey(path, body,
  key => apiRequest(path, { method: "POST", body, token, idempotencyKey: key }));
const multipartWithProgress = (path, method, body, token, onProgress, idempotencyKey) => new Promise((resolve, reject) => {
  const request = new XMLHttpRequest();
  request.open(method, `${API_BASE_URL}${path}`);
  request.setRequestHeader("Authorization", `Bearer ${token}`);
  if (idempotencyKey) request.setRequestHeader("Idempotency-Key", idempotencyKey);
  request.timeout = 600_000;
  request.upload.onprogress = event => {
    if (event.lengthComputable && onProgress) onProgress(Math.round(100 * event.loaded / event.total));
  };
  request.onload = () => {
    let data = null;
    try { data = JSON.parse(request.responseText); } catch { data = null; }
    if (request.status >= 200 && request.status < 300) resolve(data);
    else { const error = new Error(data?.message || `HTTP ${request.status}`);
      error.status = request.status; error.data = data; reject(error); }
  };
  request.onerror = () => reject(new Error("Không kết nối được máy chủ."));
  request.ontimeout = () => reject(new Error("Tải lên quá thời gian. Kiểm tra danh sách trước khi thử lại."));
  request.send(body);
});

async function uploadVideo(lectureId, resourceId, body, token, onProgress) {
  const file = body.get("file");
  const base = `${root}/lectures/${lectureId}/video-upload`;
  const key = `sto123-video:${lectureId}:${resourceId || 0}:${file.name}:${file.size}:${file.lastModified}`;
  let session;
  let alreadyUploaded = false;
  try { session = JSON.parse(sessionStorage.getItem(key) || "null"); } catch { session = null; }
  if (session) {
    try {
      const status = await apiRequest(`${base}/status`, { token, headers: { "X-Upload-Token": session.token } });
      if (status.state === "saved") { sessionStorage.removeItem(key); onProgress?.(100, "saved"); return status.resource; }
      alreadyUploaded = status.state === "uploaded";
    } catch (error) {
      if (error.status === 401 || error.status === 404) { sessionStorage.removeItem(key); session = null; }
      else throw error;
    }
  }
  if (!session) {
    session = await apiRequest(`${base}/start`, { method: "POST", token,
      body: { fileName: file.name, size: file.size, resourceId: resourceId || null } });
    sessionStorage.setItem(key, JSON.stringify(session));
  }
  const loaded = new Array(session.blocks).fill(0);
  const progress = () => onProgress?.(Math.min(99, Math.round(loaded.reduce((a, b) => a + b, 0) / file.size * 100)), "upload");
  const send = index => new Promise((resolve, reject) => {
    const start = index * session.blockBytes;
    const chunk = file.slice(start, Math.min(file.size, start + session.blockBytes));
    const request = new XMLHttpRequest();
    request.open("PUT", `${API_BASE_URL}${base}/blocks/${index}`);
    request.setRequestHeader("Authorization", `Bearer ${token}`);
    request.setRequestHeader("X-Upload-Token", session.token);
    request.timeout = 180_000;
    request.upload.onprogress = event => { loaded[index] = event.loaded; progress(); };
    request.onload = () => {
      if (request.status === 204) { loaded[index] = chunk.size; progress(); resolve(); return; }
      let message;
      try { message = JSON.parse(request.responseText)?.message; } catch { /* Máy chủ có thể trả lỗi không phải JSON. */ }
      const error = new Error(message || `Khối ${index + 1} tải lỗi (HTTP ${request.status}).`);
      error.status = request.status;
      reject(error);
    };
    request.onerror = () => reject(new Error(`Mất kết nối khi tải khối ${index + 1}.`));
    request.ontimeout = () => reject(new Error(`Khối ${index + 1} tải quá lâu.`));
    request.send(chunk);
  });
  let next = 0;
  let stopped = false;
  async function worker() {
    while (!stopped && next < session.blocks) {
      const index = next++;
      let lastError;
      for (let attempt = 0; attempt < 3; attempt++) {
        try { await send(index); lastError = null; break; }
        catch (error) { lastError = error; loaded[index] = 0; progress(); if (attempt < 2) await new Promise(r => setTimeout(r, 1000 * (attempt + 1))); }
      }
      if (lastError) { stopped = true; throw lastError; }
    }
  }
  if (!alreadyUploaded) {
    const results = await Promise.allSettled(Array.from({ length: Math.min(3, session.blocks) }, worker));
    const failed = results.find(result => result.status === "rejected");
    if (failed) throw failed.reason;
  }
  onProgress?.(99, "verify");
  let lastError;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const result = await apiRequest(`${base}/finish`, { method: "POST", token,
        body: {}, timeoutMs: 180_000, headers: { "X-Upload-Token": session.token } });
      sessionStorage.removeItem(key); onProgress?.(100, "saved"); return result;
    } catch (error) {
      lastError = error;
      if (error.status && error.status < 500 && error.status !== 409) break;
      if (attempt < 2) await new Promise(r => setTimeout(r, 1000 * (attempt + 1)));
    }
  }
  let status;
  try { status = await apiRequest(`${base}/status`, { token, headers: { "X-Upload-Token": session.token } }); }
  catch (statusError) { if (!lastError.status) throw statusError; }
  if (status?.state === "saved") { sessionStorage.removeItem(key); onProgress?.(100, "saved"); return status.resource; }
  if (status?.state === "uploaded") throw new Error("Video đã lên Blob nhưng chưa được lưu vào bài giảng. Nhấn Tải tệp để thử xác nhận lại.");
  throw lastError;
}

export const adminContentApi = {
  dashboard: token => apiRequest(`${root}/dashboard`, { token }),
  outline: (courseId, token) => apiRequest(`${course(courseId)}/outline`, { token }),
  progressSummary: (courseId, token) => apiRequest(`${course(courseId)}/progress-summary`, { token }),
  previewStep: (courseId, stepId, token) => apiRequest(`${course(courseId)}/steps/${stepId}/preview`, { token }),
  topics: token => apiRequest(`${root}/topics`, { token }),
  parts: token => apiRequest("/api/parts", { token }),
  addUnit: (courseId, body, token) => create(`${course(courseId)}/units`, body, token),
  duplicateUnit: (courseId, unitId, token) => create(`${unit(courseId, unitId)}/duplicate`, {}, token),
  editUnit: (courseId, unitId, body, token) => write(unit(courseId, unitId), "PUT", body, token),
  archiveUnit: (courseId, unitId, isArchived, token) => write(`${unit(courseId, unitId)}/archive`, "PUT", { isArchived }, token),
  unitDeleteImpact: (courseId, unitId, token) => apiRequest(`${unit(courseId, unitId)}/delete-impact`, { token }),
  deleteUnit: (courseId, unitId, token, impact) => write(`${unit(courseId, unitId)}${deletionQuery(impact)}`, "DELETE", null, token),
  orderUnits: (courseId, ids, token) => write(`${course(courseId)}/units/order`, "POST", { ids }, token),
  addLesson: (courseId, unitId, body, token) => create(`${unit(courseId, unitId)}/lessons`, body, token),
  duplicateLesson: (courseId, unitId, lessonId, token) => create(`${lesson(courseId, unitId, lessonId)}/duplicate`, {}, token),
  editLesson: (courseId, unitId, lessonId, body, token) => write(lesson(courseId, unitId, lessonId), "PUT", body, token),
  archiveLesson: (courseId, unitId, lessonId, isArchived, token) => write(`${lesson(courseId, unitId, lessonId)}/archive`, "PUT", { isArchived }, token),
  lessonDeleteImpact: (courseId, unitId, lessonId, token) => apiRequest(`${lesson(courseId, unitId, lessonId)}/delete-impact`, { token }),
  deleteLesson: (courseId, unitId, lessonId, token, impact) => write(`${lesson(courseId, unitId, lessonId)}${deletionQuery(impact)}`, "DELETE", null, token),
  orderLessons: (courseId, unitId, ids, token) => write(`${unit(courseId, unitId)}/lessons/order`, "POST", { ids }, token),
  addStep: (courseId, unitId, lessonId, body, token) => create(`${lesson(courseId, unitId, lessonId)}/steps`, body, token),
  duplicateStep: (courseId, unitId, lessonId, stepId, token) => create(`${step(courseId, unitId, lessonId, stepId)}/duplicate`, {}, token),
  addCourseLecture: (courseId, unitId, lessonId, body, token) => create(`${lesson(courseId, unitId, lessonId)}/lecture`, body, token),
  addExercise: (courseId, unitId, lessonId, body, token) => create(`${lesson(courseId, unitId, lessonId)}/exercises`, body, token),
  reviseExercise: (courseId, unitId, lessonId, stepId, body, token) => write(`${step(courseId, unitId, lessonId, stepId)}/exercise`, "PUT", body, token),
  exerciseDetail: (courseId, unitId, lessonId, stepId, token) => apiRequest(`${step(courseId, unitId, lessonId, stepId)}/exercise`, { token }),
  editCourseLecture: (courseId, unitId, lessonId, stepId, body, token) => write(`${step(courseId, unitId, lessonId, stepId)}/lecture`, "PUT", body, token),
  editStep: (courseId, unitId, lessonId, stepId, body, token) => write(step(courseId, unitId, lessonId, stepId), "PUT", body, token),
  archiveStep: (courseId, unitId, lessonId, stepId, isArchived, token) => write(`${step(courseId, unitId, lessonId, stepId)}/archive`, "PUT", { isArchived }, token),
  stepDeleteImpact: (courseId, unitId, lessonId, stepId, token) => apiRequest(`${step(courseId, unitId, lessonId, stepId)}/delete-impact`, { token }),
  deleteStep: (courseId, unitId, lessonId, stepId, token, impact) => write(`${step(courseId, unitId, lessonId, stepId)}${deletionQuery(impact)}`, "DELETE", null, token),
  orderSteps: (courseId, unitId, lessonId, ids, token) => write(`${lesson(courseId, unitId, lessonId)}/steps/order`, "POST", { ids }, token),
  categories: token => apiRequest(`${root}/lectures/categories`, { token }),
  addCategory: (body, token) => create(`${root}/lectures/categories`, body, token),
  editCategory: (id, body, token) => write(`${root}/lectures/categories/${id}`, "PUT", body, token),
  deleteCategory: (id, token) => write(`${root}/lectures/categories/${id}`, "DELETE", null, token),
  lectures: (search, page, token) => apiRequest(`${root}/lectures?${new URLSearchParams({ search, page: String(page) })}`, { token }),
  availableLectures: (search, token) => apiRequest(`${root}/lectures/available?${new URLSearchParams({ search })}`, { token }),
  lecture: (id, token) => apiRequest(`${root}/lectures/${id}`, { token }),
  resourceCapabilities: token => apiRequest(`${root}/lectures/resource-capabilities`, { token }),
  addLecture: (body, token) => create(`${root}/lectures`, body, token),
  editLecture: (id, body, token) => write(`${root}/lectures/${id}`, "PUT", body, token),
  deleteLecture: (id, token) => write(`${root}/lectures/${id}`, "DELETE", null, token),
  uploadResource: (id, resourceId, body, token, onProgress) => {
    if (body.get("kind") === "VIDEO") return uploadVideo(id, resourceId, body, token, onProgress).catch(error => {
      if (error.data?.errors?.["X-Upload-Token"])
        throw new Error("Thiếu mã xác nhận phiên tải video. Vui lòng chọn lại tệp và thử lại.");
      if (error.message?.startsWith("HTTP "))
        throw new Error(`Không thể hoàn tất tải video (${error.message}). Vui lòng thử lại.`);
      throw error;
    });
    const path = `${root}/lectures/${id}/resources${resourceId ? `/${resourceId}` : ""}`;
    if (resourceId) return multipartWithProgress(path, "PUT", body, token, onProgress);
    const file = body.get("file");
    return withCreationKey(path, { name: body.get("name"), kind: body.get("kind"),
      fileName: file?.name, size: file?.size, lastModified: file?.lastModified },
    key => multipartWithProgress(path, "POST", body, token, onProgress, key));
  },
  renameResource: (id, resourceId, name, token) => write(`${root}/lectures/${id}/resources/${resourceId}/name`,
    "PUT", { name }, token),
  orderResources: (id, ids, token) => write(`${root}/lectures/${id}/resources/order`, "POST", { ids }, token),
  resourceDeleteImpact: (id, resourceId, token) => apiRequest(`${root}/lectures/${id}/resources/${resourceId}/delete-impact`, { token }),
  deleteResource: (id, resourceId, token, impact) => write(`${root}/lectures/${id}/resources/${resourceId}${impact?.progressRecords ? `?${new URLSearchParams({ confirmWithProgress: "true", expectedProgress: String(impact.progressRecords) })}` : ""}`, "DELETE", null, token),
  archiveResource: (id, resourceId, isArchived, token) => write(`${root}/lectures/${id}/resources/${resourceId}/archive`, "PUT", { isArchived }, token),
  resourceBlob: async (lectureId, resourceId, token) => {
    const response = await fetch(`${API_BASE_URL}${root}/lectures/${lectureId}/resources/${resourceId}/file`,
      { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error("Không tải được tài nguyên xem trước.");
    return response.blob();
  },
};

export const learningApi = {
  step: (courseId, stepId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}`, { token }),
  complete: (courseId, stepId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/completion`, { method: "PUT", token }),
  exercise: (courseId, stepId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise`, { token }),
  exerciseHistory: (courseId, stepId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise/attempts`, { token }),
  startExercise: (courseId, stepId, restartFromAttemptId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise/attempts`, { method: "POST", body: { restartFromAttemptId }, token }),
  checkExercise: (courseId, stepId, attemptId, body, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise/attempts/${attemptId}/check`, { method: "POST", body, token }),
  exerciseAnswer: (courseId, stepId, attemptId, questionId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise/attempts/${attemptId}/questions/${questionId}/answer`, { token }),
  finishExercise: (courseId, stepId, attemptId, token) => apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/exercise/attempts/${attemptId}/finish`, { method: "POST", token }),
  resourceTicket: (courseId, stepId, resourceId, token) =>
    apiRequest(`/api/learning/courses/${courseId}/steps/${stepId}/resources/${resourceId}/ticket`,
      { method: "POST", token }),
  resourceStreamUrl: (relativeUrl, download = false) => `${API_BASE_URL}${relativeUrl}${download ? "&download=true" : ""}`,
  resource: async (courseId, stepId, resourceId, token) => {
    const response = await fetch(`${API_BASE_URL}/api/learning/courses/${courseId}/steps/${stepId}/resources/${resourceId}`,
      { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error("Không tải được tài nguyên.");
    return response.blob();
  },
};
