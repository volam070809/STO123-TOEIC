import { useSyncExternalStore } from "react";

export const INITIAL_TOPIC_COUNT = 6;
const MOBILE_TOPIC_QUERY = "(max-width: 600px)";

function subscribeMobile(onChange) {
  if (typeof window === "undefined") return () => {};
  const media = window.matchMedia(MOBILE_TOPIC_QUERY);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
}

function isMobile() {
  return typeof window !== "undefined" && window.matchMedia(MOBILE_TOPIC_QUERY).matches;
}

export function useMobileTopicCatalog() {
  return useSyncExternalStore(subscribeMobile, isMobile, () => false);
}

const searchKey = value => String(value ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "")
  .replace(/[đĐ]/g, "d").toLocaleLowerCase("vi-VN");

export function filterTopics(topics, query) {
  const needle = searchKey(query.trim());
  if (!needle) return topics;
  return topics.filter(topic => searchKey(topic.tenChuDe).includes(needle));
}

export function visibleTopics(topics, query, count) {
  return filterTopics(topics, query).slice(0, count);
}

export function topicCatalogView(topics, query, count, mobile) {
  const filteredTopics = filterTopics(topics, query);
  return {
    filteredTopics,
    shownTopics: mobile ? filteredTopics.slice(0, count) : filteredTopics,
    hasMore: mobile && filteredTopics.length > count,
  };
}

export function topicGridStyle(topicCount) {
  const columns = Math.min(Math.max(topicCount, 1), 4);
  const tabletColumns = Math.min(columns, 2);
  const width = count => `${count * 280 + (count - 1) * 18}px`;
  return {
    "--topic-columns": columns,
    "--topic-max-width": width(columns),
    "--topic-tablet-columns": tabletColumns,
    "--topic-tablet-max-width": width(tabletColumns),
  };
}
