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
