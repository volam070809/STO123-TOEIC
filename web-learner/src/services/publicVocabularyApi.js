import { apiRequest } from "./api";

const ROOT = "/api/public/vocabulary";

export const publicVocabularyApi = {
  topics: () => apiRequest(ROOT + "/topics", { cache: "no-store" }),
  preview: topicId => apiRequest(`${ROOT}/topics/${topicId}/preview`, { cache: "no-store" }),
  audioEndpoint: (topicId, wordId, example = false) =>
    `${ROOT}/topics/${topicId}/preview/${wordId}/${example ? "audio-example" : "audio"}`,
};
