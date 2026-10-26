import test from "node:test";
import assert from "node:assert/strict";
import {
  GUEST_PREVIEW_LIMIT,
  guestPreviewHasMore,
  vocabularyReturnTo,
  vocabularyTopicFromSearch,
} from "./guestVocabularyPreview.js";

test("guest preview limit is independent and recognizes a real eleventh word", () => {
  const tenWords = Array.from({ length: GUEST_PREVIEW_LIMIT }, (_, index) => ({ id: index + 1 }));
  assert.equal(guestPreviewHasMore({ totalWords: 12 }, tenWords), true);
  assert.equal(guestPreviewHasMore({ totalWords: 10 }, tenWords), false);
  assert.equal(guestPreviewHasMore({ totalWords: 4 }, tenWords.slice(0, 4)), false);
  assert.equal(guestPreviewHasMore({ totalWords: 25 }, tenWords), true);
});

test("topic return URLs accept only one safe positive integer identifier", () => {
  assert.equal(vocabularyTopicFromSearch("?topic=42"), 42);
  assert.equal(vocabularyTopicFromSearch("?topic=42&topic=43"), null);
  assert.equal(vocabularyTopicFromSearch("?topic=42&offset=10"), null);
  assert.equal(vocabularyTopicFromSearch("?topic=0"), null);
  assert.equal(vocabularyTopicFromSearch("?topic=1%2F..%2Fadmin"), null);
  assert.equal(vocabularyReturnTo(42), "/vocabulary?topic=42");
  assert.equal(vocabularyReturnTo(null), "/vocabulary");
});
