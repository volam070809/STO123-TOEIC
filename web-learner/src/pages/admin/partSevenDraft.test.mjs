import test from "node:test";
import assert from "node:assert/strict";
import { clearPartSevenDraft, clearPartSevenGroupDraft, loadPartSevenDraft, loadPartSevenGroupDraft,
  savePartSevenDraft, savePartSevenGroupDraft } from "./partSevenDraft.js";

test("Part 7 draft reopens for the same admin, bank and exam", () => {
  const storage = new Map();
  const original = globalThis.localStorage;
  globalThis.localStorage = { getItem: key => storage.get(key) || null,
    setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) };
  try {
    const token = subject => `x.${Buffer.from(JSON.stringify({ sub: subject })).toString("base64url")}.x`;
    const value = { text: "Context", documents: [{ type: "IMAGE", imagePath: "part7/photo.png" }],
      questions: [{ text: "One" }, { text: "Two" }], part7Translation: { documents: [] } };
    assert.equal(savePartSevenDraft(token("one"), "DE_THI", 42, value), true);
    assert.equal(loadPartSevenDraft(token("one"), "DE_THI", 42).documents[0].imagePath, "part7/photo.png");
    assert.equal(loadPartSevenDraft(token("two"), "DE_THI", 42), null);
    assert.equal(loadPartSevenDraft(token("one"), "LUYEN_TAP", 42), null);
    clearPartSevenDraft(token("one"), "DE_THI", 42);
    assert.equal(loadPartSevenDraft(token("one"), "DE_THI", 42), null);
  } finally { globalThis.localStorage = original; }
});

test("Part 7 edit draft is ignored after the saved group changes", () => {
  const storage = new Map();
  const original = globalThis.localStorage;
  globalThis.localStorage = { getItem: key => storage.get(key) || null,
    setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) };
  try {
    const token = `x.${Buffer.from(JSON.stringify({ sub: "admin" })).toString("base64url")}.x`;
    const value = { documents: [{ type: "TEXT", content: "Draft" }] };
    assert.equal(savePartSevenGroupDraft(token, "DE_THI", 9, "source-v1", value, { documents: [] }), true);
    assert.equal(loadPartSevenGroupDraft(token, "DE_THI", 9, "source-v1").value.documents[0].content, "Draft");
    assert.equal(loadPartSevenGroupDraft(token, "DE_THI", 9, "source-v2"), null);
    clearPartSevenGroupDraft(token, "DE_THI", 9);
    assert.equal(loadPartSevenGroupDraft(token, "DE_THI", 9, "source-v1"), null);
  } finally { globalThis.localStorage = original; }
});
