export const placementPartNames = {
  1: "Photographs",
  2: "Question - Response",
  3: "Conversations",
  4: "Talks",
  5: "Incomplete Sentences",
  6: "Text Completion",
  7: "Reading Comprehension",
};

export function practicePriorities(parts = []) {
  const rows = parts
    .filter(row => row && Number.isInteger(row.part) && row.part >= 1 && row.part <= 7)
    .map(row => ({ part: row.part, percentage: Number(row.percentage ?? 0) }));
  return rows
    .sort((a, b) => a.percentage - b.percentage || a.part - b.part)
    .slice(0, 3)
    .map((row, index) => ({ ...row, priority: index + 1, name: placementPartNames[row.part] }));
}

export function strengthWeakness(analysis) {
  if (!analysis?.parts?.length || !analysis?.summary) return null;
  const byPart = new Map(analysis.parts.map(row => [row.part, row]));
  const strongest = byPart.get(analysis.summary.strongestPart);
  const weakest = byPart.get(analysis.summary.weakestPart);
  if (!strongest || !weakest) return null;
  return { strongest, weakest };
}
