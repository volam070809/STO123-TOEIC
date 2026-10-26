export const GUEST_PREVIEW_LIMIT = 10;

export function vocabularyTopicFromSearch(search) {
  const params = new URLSearchParams(search);
  if ([...params.keys()].some(key => key !== "topic") || params.getAll("topic").length !== 1) return null;
  const raw = params.get("topic");
  if (!/^[1-9]\d{0,9}$/.test(raw ?? "")) return null;
  const topicId = Number(raw);
  return Number.isSafeInteger(topicId) ? topicId : null;
}

export function guestPreviewHasMore(topic, words) {
  return words.length === GUEST_PREVIEW_LIMIT && Number(topic?.totalWords) > words.length;
}

export function vocabularyReturnTo(topicId) {
  return Number.isSafeInteger(topicId) && topicId > 0 ? `/vocabulary?topic=${topicId}` : "/vocabulary";
}
