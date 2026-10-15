import { apiRequest } from "./api";

export const packageApi = {
  list: token => apiRequest("/api/packages", { token, cache: "no-store" }),
  detail: (id, token) => apiRequest(`/api/packages/${id}`, { token, cache: "no-store" }),
};
