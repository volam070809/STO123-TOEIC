export const learnerDetailTabs = ["overview", "vocabulary", "practice", "exams", "courses"];

export function learnerDetailTab(value) {
  return learnerDetailTabs.includes(value) ? value : "overview";
}

export function positivePage(value) {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
}

export function learnerDetailParams(current, changes) {
  const params = new URLSearchParams(current);
  Object.entries(changes).forEach(([key, value]) => {
    if (value === null || value === undefined || value === "" ||
        (value === 1 && key.toLowerCase().endsWith("page"))) params.delete(key);
    else params.set(key, String(value));
  });
  return params;
}

export function practiceInsights(parts = []) {
  const measured = parts.filter(part =>
    Number(part.totalAttempts) > 0 && Number.isFinite(Number(part.accuracy)));

  if (!measured.length) return { strongest: null, weakest: null };

  return measured.reduce((result, part) => ({
    strongest: Number(part.accuracy) > Number(result.strongest.accuracy) ? part : result.strongest,
    weakest: Number(part.accuracy) < Number(result.weakest.accuracy) ? part : result.weakest,
  }), { strongest: measured[0], weakest: measured[0] });
}

export function mockScoreTrend(items = []) {
  return items
    .filter(item => item.completedAt && Number.isFinite(Number(item.totalScore)))
    .slice()
    .reverse()
    .map(item => ({
      attemptId: item.attemptId,
      completedAt: item.completedAt,
      score: Number(item.totalScore),
    }));
}

export function groupVocabularyTopics(topics = []) {
  return topics.reduce((groups, topic) => {
    if (topic.learningStatus === "NOT_STARTED") groups.inactive.push(topic);
    else groups.started.push(topic);
    return groups;
  }, { started: [], inactive: [] });
}

export function hasVocabularyPracticeAccuracy(topic) {
  const answered = Number(topic?.practiceCorrect || 0) + Number(topic?.practiceIncorrect || 0);
  return Number(topic?.practiceAttempts) > 0 && answered > 0 &&
    Number.isFinite(Number(topic?.practiceAccuracy));
}
