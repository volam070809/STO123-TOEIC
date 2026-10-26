export function nextPaymentHistoryLearner(current, candidate) {
  return current?.learnerId === candidate.learnerId ? null : candidate;
}

export function paymentHistoryRegionId(learnerId) {
  return `payment-history-${learnerId}`;
}

export function paymentHistoryState(request, expectedKey) {
  if (request.key !== expectedKey) return { kind: "loading" };
  if (request.error) return { kind: "error", error: request.error };
  if (request.result?.items?.length) return { kind: "success", result: request.result };
  return { kind: "empty", result: request.result };
}
