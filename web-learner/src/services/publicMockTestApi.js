import { apiRequest } from "./api";

export const publicMockTestApi = {
  list: () => apiRequest("/api/public/mock-tests", { cache: "no-store" }),
};
