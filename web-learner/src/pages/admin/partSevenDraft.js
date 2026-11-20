function draftKey(token, bank, examId) {
  try {
    const payload = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    const userId = payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] || payload.sub;
    return userId ? `sto123:part7:draft:${userId}:${bank}:${examId || "bank"}` : null;
  } catch { return null; }
}

export function loadPartSevenDraft(token, bank, examId) {
  const key = draftKey(token, bank, examId);
  if (!key || typeof localStorage === "undefined") return null;
  try {
    const value = JSON.parse(localStorage.getItem(key));
    return value?.version === 1 && Array.isArray(value.documents) &&
      value.documents.length >= 1 && value.documents.length <= 3 &&
      Array.isArray(value.questions) && value.questions.length >= 2 && value.questions.length <= 5 ? value : null;
  } catch { return null; }
}

export function savePartSevenDraft(token, bank, examId, value) {
  const key = draftKey(token, bank, examId);
  if (!key || typeof localStorage === "undefined") return false;
  try { localStorage.setItem(key, JSON.stringify({ version: 1, ...value })); return true; }
  catch { return false; }
}

export function clearPartSevenDraft(token, bank, examId) {
  const key = draftKey(token, bank, examId);
  if (key && typeof localStorage !== "undefined") localStorage.removeItem(key);
}

export function loadPartSevenGroupDraft(token, bank, groupId, source) {
  const key = draftKey(token, bank, `group-${groupId}`);
  if (!key || typeof localStorage === "undefined") return null;
  try {
    const value = JSON.parse(localStorage.getItem(key));
    return value?.version === 1 && value.source === source && value.value &&
      Array.isArray(value.value.documents) && value.part7Translation ? value : null;
  } catch { return null; }
}

export function savePartSevenGroupDraft(token, bank, groupId, source, value, part7Translation) {
  const key = draftKey(token, bank, `group-${groupId}`);
  if (!key || typeof localStorage === "undefined") return false;
  try { localStorage.setItem(key, JSON.stringify({ version: 1, source, value, part7Translation })); return true; }
  catch { return false; }
}

export function clearPartSevenGroupDraft(token, bank, groupId) {
  clearPartSevenDraft(token, bank, `group-${groupId}`);
}
