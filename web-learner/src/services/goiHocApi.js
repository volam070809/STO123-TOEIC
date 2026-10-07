import { apiRequest } from "./api";

export const goiHocApi = {
    getAll: (token) =>
        apiRequest("/api/GoiHoc", {
            token,
        }),

    getById: (id, token) =>
        apiRequest(`/api/GoiHoc/${id}`, {
            token,
        }),

    getKhoaHoc: (id, token) =>
    apiRequest(`/api/GoiHoc/${id}/khoa-hoc`, {
        token,
    }),
};