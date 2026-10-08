// Keep one playback record per attempt/group across component remounts and page reloads.
// The server does not persist audio consumption.
const sessions = new Map();
const storagePrefix = "sto123:audio:";

function remember(session) {
  try {
    window.sessionStorage.setItem(storagePrefix + session.key, JSON.stringify({
      started: session.started, ended: session.ended,
      position: session.position, duration: session.duration
    }));
    session.savedSecond = Math.floor(session.position);
  } catch { /* Playback still works when session storage is unavailable. */ }
}

export function audioSession(key) {
  if (!sessions.has(key)) {
    let saved = null;
    try { saved = JSON.parse(window.sessionStorage.getItem(storagePrefix + key)); }
    catch { /* Use an in-memory session when storage is unavailable. */ }
    const position = Number.isFinite(saved?.position) && saved.position >= 0 ? saved.position : 0;
    sessions.set(key, { key, started: saved?.started === true, ended: saved?.ended === true,
      position, duration: Number.isFinite(saved?.duration) ? saved.duration : null,
      savedSecond: Math.floor(position) });
  }
  return sessions.get(key);
}

export function advanceAudioSession(session, position) {
  if (Number.isFinite(position) && position >= 0) {
    session.position = Math.max(session.position, position);
    if (Math.floor(session.position) !== session.savedSecond) remember(session);
  }
  return session.position;
}

export function startAudioSession(session) {
  session.started = true;
  remember(session);
}

export function resetUnplayedAudioSession(session) {
  if (session.position === 0 && !session.ended) { session.started = false; remember(session); }
}

export function finishAudioSession(session, duration) {
  advanceAudioSession(session, duration);
  if (Number.isFinite(duration)) session.duration = duration;
  session.started = true;
  session.ended = true;
  remember(session);
}

export function setAudioSessionDuration(session, duration) {
  if (Number.isFinite(duration) && duration > 0) { session.duration = duration; remember(session); }
}
