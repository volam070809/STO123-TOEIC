import { HubConnectionBuilder, HttpTransportType } from "@microsoft/signalr";
import { apiRequest, API_BASE_URL } from "./api";

const SIGNALR_BASE_URL = import.meta.env.VITE_SIGNALR_BASE_URL?.trim().replace(/\/$/, "") ?? API_BASE_URL;

export const arenaApi = {
  quick: (part, token) => apiRequest("/api/arena/quick", { method: "POST", body: { part }, token }),
  privateRoom: (part, token) => apiRequest("/api/arena/private", { method: "POST", body: { part }, token }),
  join: (code, token) => apiRequest("/api/arena/join", { method: "POST", body: { code }, token }),
  state: (id, token) => apiRequest(`/api/arena/matches/${id}`, { token }),
  leave: (id, token) => apiRequest(`/api/arena/matches/${id}/leave`, { method: "POST", token }),
  surrender: (id, token) => apiRequest(`/api/arena/matches/${id}/surrender`, { method: "POST", token }),
  ready: (id, token) => apiRequest(`/api/arena/matches/${id}/ready`, { method: "POST", token }),
  answer: (id, questionIndex, choice, token) => apiRequest(`/api/arena/matches/${id}/answer`, { method: "POST", body: { questionIndex, choice }, token }),
  invite: (id, token) => apiRequest(`/api/arena/matches/${id}/invite`, { method: "POST", token }),
  active: token => apiRequest("/api/arena/active", { token }),
};

export function arenaConnection(token) {
  return new HubConnectionBuilder()
    .withUrl(`${SIGNALR_BASE_URL}/hubs/arena`, {
      accessTokenFactory: () => token,
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect([0, 1500, 4000, 8000, 15000])
    .build();
}
