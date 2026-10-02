import { apiRequest, API_BASE_URL } from "./api";

const endpoint = "/api/profile/avatar";

export const avatarApi = {
  async upload(file, token) {
    const body = new FormData();
    body.append("file", file);
    const response = await fetch(API_BASE_URL + endpoint, {
      method: "POST", headers: { Accept: "application/json", Authorization: "Bearer " + token }, body
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      if (response.status === 401)
        window.dispatchEvent(new CustomEvent("auth:session-expired", { detail: { token } }));
      throw new Error(data?.message || "Không thể lưu ảnh đại diện.");
    }
    return data;
  },
  remove: token => apiRequest(endpoint, { method: "DELETE", token }),
  async image(token, signal) {
    const response = await fetch(API_BASE_URL + endpoint, {
      headers: { Authorization: "Bearer " + token }, signal
    });
    if (!response.ok) throw new Error("Không thể tải ảnh đại diện.");
    return response.blob();
  }
};
