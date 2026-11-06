import test from "node:test";
import assert from "node:assert/strict";
import { preferFreshArenaState } from "../src/pages/learning/arenaState.js";

test("late polling cannot replace a newer Arena state", () => {
  const current = { id: "match-a", revision: "000000000000000B", status: "REVEAL" };
  const oldPoll = { id: "match-a", revision: "000000000000000A", status: "ACTIVE" };
  assert.equal(preferFreshArenaState(current, oldPoll), current);
  assert.equal(preferFreshArenaState(current, { ...oldPoll, revision: "000000000000000C" }).status,
    "ACTIVE");
});

test("presence updates with the same SQL revision and new matches still arrive", () => {
  const current = { id: "match-a", revision: "000000000000000B", online: false };
  const online = { ...current, online: true };
  assert.equal(preferFreshArenaState(current, online), online);
  const anotherMatch = { id: "match-b", revision: "0000000000000001" };
  assert.equal(preferFreshArenaState(current, anotherMatch), anotherMatch);
});
