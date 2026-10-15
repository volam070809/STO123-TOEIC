import test from "node:test";
import assert from "node:assert/strict";
import { checkoutErrorMessage, paymentError, paymentIdFromSearch, paymentState, startPaymentPolling,
  validatedCheckoutUrl } from "./paymentFlow.js";

const checkout = `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=abc123&vnp_SecureHash=${"a".repeat(128)}`;

test("result ID survives refresh but invalid and duplicate IDs are rejected", () => {
  assert.equal(paymentIdFromSearch("?paymentId=42"), 42);
  assert.equal(paymentIdFromSearch("?paymentId=0"), null);
  assert.equal(paymentIdFromSearch("?paymentId=42&paymentId=43"), null);
  assert.equal(paymentIdFromSearch("?paymentId=hello"), null);
});

test("checkout accepts only the exact Sandbox target and signed reference", () => {
  assert.equal(validatedCheckoutUrl(checkout), checkout);
  assert.equal(validatedCheckoutUrl(checkout.replace("sandbox.vnpayment.vn", "sandbox.vnpayment.vn.evil.test")), null);
  assert.equal(validatedCheckoutUrl(checkout.replace("https:", "http:")), null);
  assert.equal(validatedCheckoutUrl(checkout + "&vnp_TxnRef=second"), null);
  assert.equal(validatedCheckoutUrl(checkout + "#fragment"), null);
});

test("only confirmed backend states are terminal", () => {
  assert.equal(paymentState("CHO_XU_LY"), "pending");
  assert.equal(paymentState("THANH_CONG", "DANG_SU_DUNG"), "success");
  assert.equal(paymentState("THANH_CONG", "CHO_THANH_TOAN"), "reconciliation");
  assert.equal(paymentState("THAT_BAI"), "failed");
  assert.equal(paymentState("07"), "error");
  assert.equal(paymentError({ status: 401 }), "unauthorized");
  assert.equal(paymentError({ status: 404 }), "not-found");
  assert.equal(paymentError({ network: true }), "network-error");
  assert.match(checkoutErrorMessage({ status: 503, data: { code: "CHECKOUT_DISABLED" } }),
    /chưa được mở/);
});

test("bounded polling keeps pending after timeout and stops after confirmation", async () => {
  const timers = [];
  const views = [];
  let calls = 0;
  const stop = startPaymentPolling({
    paymentId: 42,
    load: async () => ({ maGiaoDich: 42, trangThai: ++calls === 3 ? "THANH_CONG" : "CHO_XU_LY",
      trangThaiDangKy: "DANG_SU_DUNG" }),
    onView: view => views.push(view),
    schedule: callback => { timers.push(callback); return callback; },
    cancel: () => {}, maxRequests: 3,
  });
  await Promise.resolve();
  assert.equal(views.at(-1).kind, "pending");
  timers.shift()(); await Promise.resolve();
  timers.shift()(); await Promise.resolve();
  assert.equal(views.at(-1).kind, "success");
  assert.equal(timers.length, 0);
  stop();

  const pendingViews = [];
  let pendingCalls = 0;
  startPaymentPolling({ paymentId: 42,
    load: async () => { pendingCalls++; return { maGiaoDich: 42, trangThai: "CHO_XU_LY" }; },
    onView: view => pendingViews.push(view),
    schedule: callback => { timers.push(callback); return callback; }, cancel: () => {}, maxRequests: 2 });
  await Promise.resolve();
  timers.shift()(); await Promise.resolve();
  assert.equal(pendingCalls, 2);
  assert.deepEqual(pendingViews.at(-1), { kind: "pending", polling: false });
  assert.equal(timers.length, 0);
});

test("unmount suppresses late responses and timers", async () => {
  let resolve;
  const views = [];
  const stop = startPaymentPolling({ paymentId: 42,
    load: () => new Promise(done => { resolve = done; }),
    onView: view => views.push(view), schedule: () => { throw new Error("unexpected timer"); } });
  stop();
  resolve({ maGiaoDich: 42, trangThai: "THANH_CONG" });
  await Promise.resolve();
  assert.deepEqual(views, [{ kind: "loading", polling: true }]);
});
