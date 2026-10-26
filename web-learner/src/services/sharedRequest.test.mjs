import assert from "node:assert/strict";
import test from "node:test";
import { clearSharedReadCache, createSharedRequest } from "./sharedRequest.js";

test("shares a request with the same key while it is pending", async () => {
  const share = createSharedRequest();
  let calls = 0;
  let finish;
  const load = () => {
    calls += 1;
    return new Promise(resolve => { finish = resolve; });
  };

  const first = share("learners", load);
  const second = share("learners", load);
  await Promise.resolve();

  assert.equal(calls, 1);
  assert.strictEqual(second, first);
  finish({ items: [] });
  assert.deepEqual(await first, { items: [] });
});

test("removes completed and failed requests from the shared cache", async () => {
  const share = createSharedRequest();
  let calls = 0;

  await share("detail", async () => { calls += 1; return 1; });
  await assert.rejects(share("detail", async () => { calls += 1; throw new Error("failed"); }));
  assert.equal(await share("detail", async () => { calls += 1; return 2; }), 2);
  assert.equal(calls, 3);
});

test("canceling one consumer does not cancel the shared server request", async () => {
  const share = createSharedRequest();
  const controller = new AbortController();
  let calls = 0;
  let finish;
  const load = () => {
    calls += 1;
    return new Promise(resolve => { finish = resolve; });
  };

  const canceledConsumer = share("overview", load, controller.signal);
  const activeConsumer = share("overview", load);
  await Promise.resolve();
  controller.abort();

  await assert.rejects(canceledConsumer, error => error.name === "AbortError");
  finish({ total: 12 });
  assert.deepEqual(await activeConsumer, { total: 12 });
  assert.equal(calls, 1);
});

test("keeps successful reads only for the configured short lifetime", async () => {
  let clock = 1_000;
  let calls = 0;
  const share = createSharedRequest({ ttlMs: 10_000, now: () => clock });
  const load = async () => ({ version: ++calls });

  assert.deepEqual(await share("token-a|learners", load), { version: 1 });
  assert.deepEqual(await share("token-a|learners", load), { version: 1 });
  clock += 10_001;
  assert.deepEqual(await share("token-a|learners", load), { version: 2 });
});

test("isolates cached reads by identity key and clears them on session changes", async () => {
  let calls = 0;
  const share = createSharedRequest({ ttlMs: 10_000 });
  const load = async () => ++calls;

  assert.equal(await share("token-a|payments", load), 1);
  assert.equal(await share("token-b|payments", load), 2);
  assert.equal(await share("token-a|payments", load), 1);
  clearSharedReadCache();
  assert.equal(await share("token-a|payments", load), 3);
});

test("does not cache failed reads", async () => {
  let calls = 0;
  const share = createSharedRequest({ ttlMs: 10_000 });
  await assert.rejects(share("reports", async () => { calls += 1; throw new Error("failed"); }));
  assert.equal(await share("reports", async () => { calls += 1; return "ok"; }), "ok");
  assert.equal(calls, 2);
});

test("a read started before invalidation cannot repopulate stale data", async () => {
  let finish;
  let calls = 0;
  const share = createSharedRequest({ ttlMs: 10_000 });
  const stale = share("token-a|courses", () => new Promise(resolve => { calls += 1; finish = resolve; }));
  await Promise.resolve();
  clearSharedReadCache();
  finish("old");
  assert.equal(await stale, "old");
  assert.equal(await share("token-a|courses", async () => { calls += 1; return "new"; }), "new");
  assert.equal(calls, 2);
});
