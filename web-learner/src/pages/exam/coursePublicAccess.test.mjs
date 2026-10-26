import test from "node:test";
import assert from "node:assert/strict";
import { loadCourseCatalog, requestGuestCoursePurchase } from "./coursePublicAccess.js";

test("guest catalog uses only the anonymous read endpoint and contains no personal state", async () => {
  const calls = [];
  const [courses, placement, history] = await loadCourseCatalog(null, {
    loadPublicCourses: async () => {
      calls.push("public");
      return [{ courseId: 7, name: "Foundation", retailPrice: 199000 }];
    },
    loadLearnerCourses: async () => { calls.push("learner"); },
    loadPlacement: async () => { calls.push("placement"); },
    loadHistory: async () => { calls.push("history"); },
  });

  assert.deepEqual(calls, ["public"]);
  assert.equal(placement, null);
  assert.equal(history, null);
  assert.deepEqual(courses[0], {
    courseId: 7, name: "Foundation", retailPrice: 199000,
    accessStatus: "NOT_PURCHASED", accessExpiresAt: null,
    recommended: false, recommendationOrder: null,
  });
});

test("authenticated catalog preserves learner, placement and payment-history reads", async () => {
  const calls = [];
  const result = await loadCourseCatalog("token", {
    loadPublicCourses: async () => { calls.push("public"); },
    loadLearnerCourses: async token => { calls.push(`learner:${token}`); return [1]; },
    loadPlacement: async token => { calls.push(`placement:${token}`); return { currentStage: 1 }; },
    loadHistory: async token => { calls.push(`history:${token}`); return [2]; },
  });

  assert.deepEqual(calls, ["learner:token", "placement:token", "history:token"]);
  assert.deepEqual(result, [[1], { currentStage: 1 }, [2]]);
});

test("guest purchase asks for authentication without executing checkout", () => {
  const destinations = [];
  let checkoutCalls = 0;
  const allowed = requestGuestCoursePurchase(destination => {
    destinations.push(destination);
    return false;
  });

  assert.equal(allowed, false);
  assert.deepEqual(destinations, ["/courses"]);
  assert.equal(checkoutCalls, 0);
});
