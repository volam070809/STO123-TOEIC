export function examNavigation(result, attemptSummary) {
  if (result?.source === "PLACEMENT")
    return { root: "/placement", back: "/placement", rootLabel: "Về Lộ trình học", mode: "PLACEMENT" };
  const mode = result?.mode || attemptSummary?.mode || (result?.source === "PART" ? "PART" : null);
  const part = result?.part || attemptSummary?.part || result?.parts?.find(row => row.stats.total > 0)?.part;
  const back = "/mock-test";
  return { root: "/mock-test", back, rootLabel: "Về trang Thi thử", mode,
    examId: result?.examId ?? attemptSummary?.examId, part };
}
