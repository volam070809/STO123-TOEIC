const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

export async function apiRequest(endpoint, { method = "GET", body = null, token = null, timeoutMs = 30_000 } = {}) {
  const headers = { Accept: "application/json" };
  if (body !== null) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = "Bearer " + token;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(API_BASE_URL + endpoint, {
      method, headers, body: body !== null ? JSON.stringify(body) : undefined, signal: controller.signal,
    });
    let data = null;
    if (response.headers.get("content-type")?.includes("json")) {
      try { data = await response.json(); }
      catch (error) {
        if (error.name !== "SyntaxError") throw error;
        if (response.ok) throw new Error("Invalid server response.");
      }
    }
    if (!response.ok) {
      const error = new Error(data?.message || "HTTP " + response.status);
      error.status = response.status;
      error.data = data;
      if (token && response.status === 401 &&
          !(endpoint === "/api/auth/change-password" && data?.message === "Incorrect current password.")) {
        window.dispatchEvent(new CustomEvent("auth:session-expired", { detail: { token } }));
      }
      throw error;
    }
    return data;
  } catch (error) {
    if (error.status || error.message === "Invalid server response.") throw error;
    const timedOut = controller.signal.aborted;
    const requestError = new Error(timedOut
      ? "Máy chủ phản hồi quá lâu. Vui lòng thử lại."
      : "Không thể kết nối đến máy chủ. Vui lòng thử lại.");
    requestError.network = true;
    requestError.timeout = timedOut;
    throw requestError;
  } finally {
    clearTimeout(timer);
  }
}

export { API_BASE_URL };
