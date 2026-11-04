import { useEffect, useState } from "react";
import { apiRequest } from "./api";

export const contentDomains = ["COURSES", "ACCESS", "CURRICULUM", "LECTURES",
  "VOCABULARY", "PRACTICE", "EXERCISES", "EXAMS"];
const eventName = "sto123:content-revision";
let last = null;
let pending = null;

export function checkContentRevisions({ fresh = false } = {}) {
  if (pending) return pending;
  const params = new URLSearchParams();
  if (last !== null) params.set("since", String(last));
  if (fresh) params.set("fresh", "true");
  pending = apiRequest(`/api/content/revisions${params.size ? `?${params}` : ""}`,
    { cache: "no-store", timeoutMs: 12_000 }).then(result => {
    if (!Number.isSafeInteger(result?.version) || !Array.isArray(result?.changedDomains))
      throw new Error("Invalid content revision response.");
    const changed = result.changedDomains.filter(domain => contentDomains.includes(domain));
    last = result.version;
    if (changed.length) window.dispatchEvent(new CustomEvent(eventName, { detail: { domains: changed } }));
    return changed;
  }).finally(() => { pending = null; });
  return pending;
}

export function ContentRevisionWatcher() {
  useEffect(() => {
    const check = options => { if (!document.hidden) checkContentRevisions(options).catch(() => {}); };
    check({ fresh: true });
    const timer = window.setInterval(() => check(), 25_000);
    const focus = () => {
      if (document.hidden) return;
      check({ fresh: true });
    };
    window.addEventListener("focus", focus);
    document.addEventListener("visibilitychange", focus);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", focus);
      document.removeEventListener("visibilitychange", focus); };
  }, []);
  return null;
}

export function useContentRevision(...domains) {
  const [revision, setRevision] = useState(0);
  const key = domains.join("|");
  useEffect(() => {
    const accepted = new Set(key.split("|"));
    const listener = event => {
      if (event.detail?.domains?.some(domain => accepted.has(domain))) setRevision(value => value + 1);
    };
    window.addEventListener(eventName, listener);
    return () => window.removeEventListener(eventName, listener);
  }, [key]);
  return revision;
}
