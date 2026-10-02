import { useEffect, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { avatarApi } from "../../services/avatarApi";
import { initialsFromName } from "./avatarInitials";

function validProviderUrl(value) {
  try { return new URL(value).protocol === "https:"; }
  catch { return false; }
}

export default function Avatar({ user, size = 36 }) {
  const { token } = useAuth();
  const [asset, setAsset] = useState(null);
  const [failed, setFailed] = useState({});
  const [loaded, setLoaded] = useState("");
  const key = user?.hasCustomAvatar && token ? `${token}:${user.avatarVersion}` : null;
  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    let objectUrl = "";
    avatarApi.image(token, controller.signal).then(blob => {
      if (!controller.signal.aborted) {
        objectUrl = URL.createObjectURL(blob);
        setAsset({ key, url: objectUrl });
      }
    }).catch(() => {});
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [key, token]);

  const custom = asset?.key === key && !failed[asset.url] ? asset.url : null;
  const provider = validProviderUrl(user?.anhDaiDien) && !failed[user.anhDaiDien] ? user.anhDaiDien : null;
  const src = custom || provider;
  return <span className="sto-avatar" style={{ "--avatar-size": `${size}px` }}
    role="img" aria-label={`Ảnh đại diện của ${user?.hoTen || "học viên"}`}>
    <span className="sto-avatar-initials" aria-hidden="true">{initialsFromName(user?.hoTen)}</span>
    {src && <img src={src} alt="" aria-hidden="true" className={loaded === src ? "is-loaded" : ""}
      onLoad={() => setLoaded(src)}
      onError={() => setFailed(old => ({ ...old, [src]: true }))} />}
  </span>;
}
