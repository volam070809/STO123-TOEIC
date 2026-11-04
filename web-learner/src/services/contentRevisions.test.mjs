import test from "node:test";
import assert from "node:assert/strict";
import { createServer } from "vite";

const vite = await createServer({ server: { middlewareMode: true, hmr: false }, appType: "custom" });
const { checkContentRevisions, contentDomains } = await vite.ssrLoadModule("/src/services/contentRevisions.js");
const events = [];
const requests = [];
globalThis.window = new EventTarget();
window.addEventListener("sto123:content-revision", event => events.push(event.detail.domains));
const responses = [
  { version: 0, changedDomains: contentDomains },
  { version: 0, changedDomains: [] },
  { version: 1, changedDomains: ["VOCABULARY"] },
];
globalThis.fetch = async url => {
  requests.push(String(url));
  return new Response(JSON.stringify(responses.shift()), {
    status: 200, headers: { "content-type": "application/json" }
  });
};

test("revision cursor refreshes only the changed content domain", async () => {
  assert.deepEqual(await checkContentRevisions(), contentDomains);
  assert.deepEqual(await checkContentRevisions(), []);
  assert.deepEqual(await checkContentRevisions(), ["VOCABULARY"]);
  assert.equal(events.length, 2);
  assert.deepEqual(events[1], ["VOCABULARY"]);
  assert.match(requests[1], /since=0/);
});

test.after(async () => { await vite.close(); });
