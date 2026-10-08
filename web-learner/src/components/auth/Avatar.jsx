import { useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { initialsFromName } from "./avatarInitials";

function validProviderUrl(value) {
  try { return new URL(value).protocol === "https:"; }
  catch { return false; }
}

export default function Avatar({ user, size = 36 }) {
  const { avatarAsset } = useAuth();
  const [failed, setFailed] = useState({});
  const [loaded, setLoaded] = useState("");
  const key = user?.hasCustomAvatar ? `${user.maNguoiDung}:${user.avatarVersion}` : null;
  const custom = avatarAsset?.key === key && avatarAsset.url && !failed[avatarAsset.url] ? avatarAsset.url : null;
  const provider = validProviderUrl(user?.anhDaiDien) && !failed[user.anhDaiDien] ? user.anhDaiDien : null;
  const src = custom || provider;
  return <span className="sto-avatar" style={{ "--avatar-size": `${size}px` }}
    role="img" aria-label={`Ảnh đại diện của ${user?.hoTen || "học viên"}`}
    title={avatarAsset?.key === key && avatarAsset.error ? "Không thể tải ảnh đại diện" : undefined}>
    <span className="sto-avatar-initials" aria-hidden="true">{initialsFromName(user?.hoTen)}</span>
    {src && <img src={src} alt="" aria-hidden="true" className={loaded === src ? "is-loaded" : ""}
      onLoad={() => setLoaded(src)}
      onError={() => setFailed(old => ({ ...old, [src]: true }))} />}
  </span>;
}
