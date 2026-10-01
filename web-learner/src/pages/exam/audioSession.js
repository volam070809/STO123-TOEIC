// Playback state lasts while this browser application remains open. It is not
// an authorization mechanism; the server does not persist audio consumption.
const sessions = new Map();

export function audioSession(key) {
  if (!sessions.has(key)) sessions.set(key, { started: false, ended: false, position: 0 });
  return sessions.get(key);
}

export function advanceAudioSession(session, position) {
  if (Number.isFinite(position) && position >= 0) {
    session.position = Math.max(session.position, position);
  }
  return session.position;
}

export function startAudioSession(session) {
  session.started = true;
}

export function resetUnplayedAudioSession(session) {
  if (session.position === 0 && !session.ended) session.started = false;
}

export function finishAudioSession(session, duration) {
  advanceAudioSession(session, duration);
  session.started = true;
  session.ended = true;
}
