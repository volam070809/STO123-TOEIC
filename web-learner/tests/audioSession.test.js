import test from "node:test";
import assert from "node:assert/strict";
import { advanceAudioSession, audioSession, finishAudioSession } from "../src/pages/exam/audioSession.js";

test("one audio occurrence keeps its forward position across mounts", () => {
  const first = audioSession("/api/attempts/11/groups/21/audio");
  first.started = true;
  advanceAudioSession(first, 18.5);
  advanceAudioSession(first, 4);
  assert.equal(audioSession("/api/attempts/11/groups/21/audio").position, 18.5);
  assert.equal(audioSession("/api/attempts/12/groups/21/audio").position, 0);
});

test("completed audio remains consumed in the current application session", () => {
  const session = audioSession("/api/attempts/11/groups/22/audio");
  finishAudioSession(session, 42);
  assert.equal(audioSession("/api/attempts/11/groups/22/audio").ended, true);
  assert.equal(session.position, 42);
  advanceAudioSession(session, 0);
  assert.equal(session.position, 42);
});
