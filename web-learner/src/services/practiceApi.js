import { apiRequest } from "./api";

export const practiceApi = {
  start: (data, token) =>
    apiRequest("/api/practice/start", {
      method: "POST",
      body: data,
      token,
    }),

  answer: (maKetQua, data, token) =>
    apiRequest(`/api/practice/${maKetQua}/answer`, {
      method: "POST",
      body: data,
      token,
    }),

  submit: (maKetQua, token) =>
    apiRequest(`/api/practice/${maKetQua}/submit`, {
      method: "POST",
      token,
    }),

  result: (maKetQua, token) =>
    apiRequest(`/api/practice/${maKetQua}/result`, {
      token,
    }),

  history: token =>
    apiRequest("/api/practice/history", {
      token,
    }),

  bookmark: (maKetQua, maCauHoiLuotLam, danhDau, token) =>
    apiRequest(`/api/practice/${maKetQua}/bookmark`, {
      method: "POST",
      body: {
        maCauHoiLuotLam,
        danhDau,
      },
      token,
    }),
};