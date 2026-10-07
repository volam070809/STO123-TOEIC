import { apiRequest } from "./api";

export const paymentApi = {
    create: (maGoiHoc, token) =>
        apiRequest("/api/payment/create", {
            method: "POST",
            body: {
                maGoiHoc: maGoiHoc,
            },
            token,
        }),

    getStatus: (maGiaoDich, token) =>
        apiRequest(`/api/payment/status/${maGiaoDich}`, {
            method: "GET",
            token,
        }),
};