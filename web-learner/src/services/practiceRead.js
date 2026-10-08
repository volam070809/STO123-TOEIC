import { apiRequest } from "./api";

const pending = new Map();

export function practiceRead(endpoint, token) {
  const key = `${token}:${endpoint}`;
  if (pending.has(key)) return pending.get(key);
  const request = apiRequest(endpoint, { token }).finally(() => pending.delete(key));
  pending.set(key, request);
  return request;
}
