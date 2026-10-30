import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { withCreationKey } from "./adminContentIdempotency.js";

test("creation key survives an uncertain response and changes after success", async () => {
  const values = new Map();
  const storage = { getItem: key => values.get(key) || null,
    setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
  let firstKey;
  await assert.rejects(withCreationKey("/units", { name: "Unit A" }, key => {
    firstKey = key; throw new Error("network timeout");
  }, storage, webcrypto), /network timeout/);
  const replay = await withCreationKey("/units", { name: "Unit A" },
    key => { assert.equal(key, firstKey); return { id: 7 }; }, storage, webcrypto);
  assert.deepEqual(replay, { id: 7 });
  await withCreationKey("/units", { name: "Unit A" },
    key => { assert.notEqual(key, firstKey); return { id: 8 }; }, storage, webcrypto);
});

test("different payloads keep separate pending keys", async () => {
  const values = new Map();
  const storage = { getItem: key => values.get(key) || null,
    setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
  let firstKey;
  await assert.rejects(withCreationKey("/lectures", { title: "A" }, key => {
    firstKey = key; throw new Error("timeout");
  }, storage, webcrypto));
  await withCreationKey("/lectures", { title: "B" },
    key => { assert.notEqual(key, firstKey); return true; }, storage, webcrypto);
  assert.equal(values.size, 1);
});
