const validStage = stage => [1, 2, 3].includes(stage);
const average = values => Math.round(values.reduce((sum, value) => sum + value, 0) / values.length * 100) / 100;

export function coursesForKnnStage(courses, stage) {
  if (!validStage(stage)) return [];
  return courses.filter(course => course.stage === stage && Number.isInteger(course.courseId) && course.courseId > 0);
}

export const shouldLoadPlacementAnalysis = (result, attemptId) =>
  result?.source === "PLACEMENT" && String(result.attemptId) === String(attemptId);

// Historical attempts expose their saved stage and exact exam result, but not the old analysis DTO.
export function historicalPlacementAnalysis(result, stage) {
  const parts = result.parts.map(({ part, stats }) => ({
    part, correct: stats.correct, total: stats.total, percentage: stats.percentage,
    level: stats.percentage < 40 ? "NEEDS_IMPROVEMENT" : stats.percentage < 65 ? "DEVELOPING" : "GOOD",
  }));
  const percentages = parts.map(part => part.percentage);
  return { result, parts, stage, targetScore: null, summary: {
    overallPercentage: average(percentages), listeningPercentage: average(percentages.slice(0, 4)),
    readingPercentage: average(percentages.slice(4)),
    strongestPart: [...parts].sort((a, b) => b.percentage - a.percentage || a.part - b.part)[0].part,
    weakestPart: [...parts].sort((a, b) => a.percentage - b.percentage || a.part - b.part)[0].part,
  } };
}

export async function placementAnalysisForAttempt(attemptId, token, api) {
  const latest = await api.result(token);
  if (String(latest.result?.attemptId) === String(attemptId)) {
    return latest.classification === "AVAILABLE" && validStage(latest.stage)
      ? { status: "available", stage: latest.stage, outcome: latest, current: true }
      : { status: "pending" };
  }

  let page = 1;
  while (true) {
    const history = await api.history(token, page, 20);
    const attempt = history.items.find(item => String(item.attemptId) === String(attemptId));
    if (attempt) return validStage(attempt.stage)
      ? { status: "available", stage: attempt.stage, current: false }
      : { status: "missing" };
    if (!history.hasMore) return { status: "missing" };
    page += 1;
  }
}

const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

export async function waitForPlacementAnalysis(attemptId, token, api, options = {}) {
  const { attempts = 5, intervalMs = 1500, sleep = pause, isActive = () => true } = options;
  for (let count = 0; count < attempts; count += 1) {
    if (!isActive()) return { status: "cancelled" };
    const analysis = await placementAnalysisForAttempt(attemptId, token, api);
    if (!isActive()) return { status: "cancelled" };
    if (analysis.status !== "pending") return analysis;
    if (count + 1 < attempts) await sleep(intervalMs);
  }
  return { status: "missing" };
}
