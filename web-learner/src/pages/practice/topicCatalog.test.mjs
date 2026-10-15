import test from "node:test";
import assert from "node:assert/strict";
import { filterTopics, INITIAL_TOPIC_COUNT, topicCatalogView, topicGridStyle, visibleTopics } from "./topicCatalog.js";

test("desktop and tablet show every API topic while mobile reveals six at a time", () => {
  for (const size of [1, 2, 3, 4, 8, 20]) {
    const topics = Array.from({ length: size }, (_, index) => ({ maChuDe: index + 1, tenChuDe: `Chủ đề ${index + 1}` }));
    assert.equal(filterTopics(topics, "").length, size);
    const wide = topicCatalogView(topics, "", INITIAL_TOPIC_COUNT, false);
    assert.deepEqual(wide.shownTopics.map(topic => topic.maChuDe), topics.map(topic => topic.maChuDe));
    assert.equal(wide.hasMore, false);
    for (let count = INITIAL_TOPIC_COUNT; count < size + INITIAL_TOPIC_COUNT; count += INITIAL_TOPIC_COUNT) {
      const mobile = topicCatalogView(topics, "", count, true);
      assert.equal(mobile.shownTopics.length, Math.min(size, count));
      assert.deepEqual(mobile.shownTopics.map(topic => topic.maChuDe), topics.slice(0, count).map(topic => topic.maChuDe));
      assert.equal(mobile.hasMore, size > count);
    }
    const grid = topicGridStyle(size);
    assert.equal(grid["--topic-columns"], Math.min(size, 4));
    assert.equal(grid["--topic-tablet-columns"], Math.min(size, 2));
    assert.equal(grid["--topic-max-width"], `${Math.min(size, 4) * 280 + (Math.min(size, 4) - 1) * 18}px`);
    if (size === 8 || size === 20) assert.equal(Math.ceil(size / grid["--topic-columns"]), size / 4);
  }
});

test("resizing preserves the catalog order without duplicate or missing cards", () => {
  const topics = Array.from({ length: 20 }, (_, index) => ({ maChuDe: index + 1, tenChuDe: `Chủ đề ${index + 1}` }));
  const mobile = topicCatalogView(topics, "", INITIAL_TOPIC_COUNT, true);
  const desktop = topicCatalogView(topics, "", INITIAL_TOPIC_COUNT, false);
  const mobileAgain = topicCatalogView(topics, "", INITIAL_TOPIC_COUNT, true);
  assert.deepEqual(mobile.shownTopics, topics.slice(0, 6));
  assert.deepEqual(desktop.shownTopics, topics);
  assert.deepEqual(mobileAgain.shownTopics, mobile.shownTopics);
  assert.deepEqual(topicCatalogView(topics, "", 12, true).shownTopics, topics.slice(0, 12));
});

test("search is case-insensitive and does not invent a topic", () => {
  const topics = [{ tenChuDe: "Office Essentials" }, { tenChuDe: "Travel Vocabulary" },
    { tenChuDe: "Du lịch" }, { tenChuDe: "Công việc" }];
  assert.deepEqual(filterTopics(topics, "  office  "), [topics[0]]);
  assert.deepEqual(filterTopics(topics, "du lich"), [topics[2]]);
  assert.deepEqual(filterTopics(topics, "Du LỊCH"), [topics[2]]);
  assert.deepEqual(filterTopics(topics, "cong viec"), [topics[3]]);
  assert.deepEqual(filterTopics(topics, "unknown"), []);
  assert.deepEqual(visibleTopics(topics, "du lich", INITIAL_TOPIC_COUNT), [topics[2]]);
});

test("search reaches a topic after the first six on both viewports", () => {
  const topics = Array.from({ length: 20 }, (_, index) => ({ maChuDe: index + 1, tenChuDe: `Chủ đề ${index + 1}` }));
  for (const mobile of [false, true]) {
    const view = topicCatalogView(topics, "chu de 20", INITIAL_TOPIC_COUNT, mobile);
    assert.deepEqual(view.filteredTopics.map(topic => topic.maChuDe), [20]);
    assert.deepEqual(view.shownTopics.map(topic => topic.maChuDe), [20]);
    assert.equal(view.hasMore, false);
  }
});
