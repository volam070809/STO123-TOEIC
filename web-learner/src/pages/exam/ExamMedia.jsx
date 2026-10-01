import { useEffect, useRef, useState } from "react";
import { examMediaBlob } from "../../services/examApi";
import { advanceAudioSession, audioSession, finishAudioSession, resetUnplayedAudioSession, startAudioSession } from "./audioSession";

export function PrivateImage({ endpoint, token, alt }) {
  const [url, setUrl] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(endpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); }
    }).catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token]);
  if (error) return <p className="exam-media-error">Không thể tải hình ảnh.</p>;
  return url ? <img className="exam-image" src={url} alt={alt} /> : <p>Đang tải hình ảnh…</p>;
}

function timeLabel(seconds) {
  if (!Number.isFinite(seconds)) return "--:--";
  return `${Math.floor(seconds / 60).toString().padStart(2, "0")}:${Math.floor(seconds % 60).toString().padStart(2, "0")}`;
}

export function PrivateAudio({ endpoint, token, mode = "MOCK" }) {
  const session = audioSession(endpoint);
  const audioRef = useRef(null);
  const [playing, setPlaying] = useState(false);
  const [ended, setEnded] = useState(session.ended);
  const [position, setPosition] = useState(session.position);
  const [duration, setDuration] = useState(null);
  const [ready, setReady] = useState(false);
  const [url, setUrl] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    if (session.ended) return;
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(endpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); }
    }).catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token, session]);
  useEffect(() => {
    const audio = audioRef.current;
    return () => {
      if (audio) { advanceAudioSession(session, audio.currentTime); audio.pause(); }
    };
  }, [url, session]);

  async function toggle() {
    const audio = audioRef.current;
    if (!audio || session.ended || !ready) return;
    if (!audio.paused) { audio.pause(); setPlaying(false); return; }
    if (session.started && audio.currentTime + 0.5 < session.position) audio.currentTime = session.position;
    const firstPlay = !session.started;
    startAudioSession(session);
    try { await audio.play(); setPlaying(true); }
    catch { if (firstPlay) resetUnplayedAudioSession(session); setError(true); }
  }

  if (error) return <p className="exam-media-error">Không thể tải âm thanh.</p>;
  if (ended) return <p className="exam-audio-finished">Đã nghe xong · Không thể phát lại trong phiên này.</p>;
  return <div className="exam-audio" aria-label="Âm thanh câu hỏi">
    {url && <audio ref={audioRef} src={url} preload="auto" onError={() => setError(true)}
      onLoadedMetadata={event => {
        const audio = event.currentTarget;
        if (session.position > 0) audio.currentTime = session.position;
        setDuration(audio.duration);
        setReady(true);
      }}
      onTimeUpdate={event => setPosition(advanceAudioSession(session, event.currentTarget.currentTime))}
      onSeeking={event => {
        const audio = event.currentTarget;
        if (Math.abs(audio.currentTime - session.position) > 0.5) audio.currentTime = session.position;
      }}
      onRateChange={event => { if (event.currentTarget.playbackRate !== 1) event.currentTarget.playbackRate = 1; }}
      onPause={event => { advanceAudioSession(session, event.currentTarget.currentTime); setPlaying(false); }}
      onEnded={event => { finishAudioSession(session, event.currentTarget.duration); setPlaying(false); setEnded(true); }} />}
    <button type="button" className="outline-button" disabled={!ready} onClick={toggle}>
      {playing ? "Tạm dừng" : session.started ? "Tiếp tục" : "Phát âm thanh"}
    </button>
    <span aria-live="polite">{!url || !ready ? "Đang tải âm thanh…" : `${timeLabel(position)} / ${timeLabel(duration)}`}</span>
    <small>{mode === "PRACTICE" ? "Có thể tạm dừng và tiếp tục. Không thể tua hoặc phát lại." :
      "Chỉ phát một lượt. Không thể tua, bắt đầu lại hoặc phát lại."}</small>
  </div>;
}
