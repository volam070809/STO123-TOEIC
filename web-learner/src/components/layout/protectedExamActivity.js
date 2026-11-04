const examSources = new Set(["PLACEMENT", "MOCK", "FIXED", "RANDOM", "PART"]);

export function isProtectedExamActive(attempt) {
  return !!attempt && examSources.has(attempt.source) &&
    (attempt.status === "DANG_LAM" || attempt.status === "BO_DO");
}
