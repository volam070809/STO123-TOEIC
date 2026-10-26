import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { nextPaymentHistoryLearner, paymentHistoryRegionId, paymentHistoryState } from "./adminPaymentViewState.js";

const learner1 = { learnerId: 11, learnerName: "Lan" };
const learner2 = { learnerId: 22, learnerName: "Minh" };

test("history selection opens, closes, reopens and switches by stable learner ID", () => {
  assert.equal(nextPaymentHistoryLearner(null, learner1), learner1);
  assert.equal(nextPaymentHistoryLearner(learner1, learner1), null);
  assert.equal(nextPaymentHistoryLearner(null, learner1), learner1);
  assert.equal(nextPaymentHistoryLearner(learner1, learner2), learner2);
  assert.equal(paymentHistoryRegionId(22), "payment-history-22");
});

test("history request state rejects stale learners and distinguishes all render outcomes", () => {
  const result = { items: [{ paymentId: 7 }], page: 1, totalPages: 2 };
  assert.equal(paymentHistoryState({ key: "learner-11", result }, "learner-22").kind, "loading");
  assert.deepEqual(paymentHistoryState({ key: "learner-22", error: "Mất kết nối" }, "learner-22"),
    { kind: "error", error: "Mất kết nối" });
  assert.equal(paymentHistoryState({ key: "learner-22", result: { items: [] } }, "learner-22").kind, "empty");
  assert.equal(paymentHistoryState({ key: "learner-22", result }, "learner-22").kind, "success");
});

test("learner history is rendered directly below its row with retry, pagination and detail navigation", () => {
  const source = readFileSync(new URL("./AdminPaymentsPage.jsx", import.meta.url), "utf8");
  assert.match(source, /admin-payment-history-row/);
  assert.match(source, /colSpan="6"/);
  assert.match(source, /learner\.learnerId, scopedFilters/);
  assert.match(source, /Đang tải lịch sử giao dịch/);
  assert.match(source, /Không tải được lịch sử/);
  assert.match(source, /Không có giao dịch phù hợp/);
  assert.match(source, /onPage={setPage}/);
  assert.match(source, /to={`\/admin\/payments\/\$\{item\.paymentId\}`}/);
  assert.match(source, /view === "learners" \? <LearnerSummaryTable/);
});
