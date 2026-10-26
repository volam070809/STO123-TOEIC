import assert from "node:assert/strict";
import test from "node:test";
import { buildTrendChartModel } from "./adminChartModel.js";

test("zero values are real points on the baseline rather than missing data", () => {
  const model = buildTrendChartModel([
    { date: "2026-10-01", count: 0 },
    { date: "2026-10-02", count: 0 },
    { date: "2026-10-03", count: 4 },
  ]);

  assert.equal(model.hasData, true);
  assert.equal(model.points[0].y, model.baseline);
  assert.equal(model.points[1].y, model.baseline);
  assert.notEqual(model.points[2].y, model.baseline);
  assert.equal(model.total, 4);
});

test("missing values create a gap and are not silently converted to zero", () => {
  const model = buildTrendChartModel([
    { date: "2026-10-01", amount: 1000 },
    { date: "2026-10-02", amount: null },
    { date: "2026-10-03", amount: 2000 },
  ], "amount", true);

  assert.equal(model.points[1].value, null);
  assert.equal((model.linePath.match(/M /g) || []).length, 2);
  assert.equal(model.total, 3000);
});

test("an all-zero period remains visible with an honest zero total", () => {
  const model = buildTrendChartModel([{ date: "2026-10-01", count: 0 }]);
  assert.equal(model.hasData, true);
  assert.equal(model.total, 0);
  assert.equal(model.points[0].y, model.baseline);
});
