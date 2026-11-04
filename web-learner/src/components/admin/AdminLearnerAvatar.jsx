import { useEffect, useState } from "react";
import { API_BASE_URL } from "../../services/api";

export default function AdminLearnerAvatar({ learner, token, className = "admin-person-avatar" }) {
  const reference = learner.avatarReference;
  const custom = reference?.startsWith(`avatars/${learner.learnerId}/`);
  const [avatar, setAvatar] = useState({ reference: null, url: "", failed: false });
  useEffect(() => {
    if (!custom || !token) return undefined;
    let live = true;
    let localUrl = "";
    fetch(`${API_BASE_URL}/api/admin/learners/${learner.learnerId}/avatar?v=${encodeURIComponent(reference)}`,
      { headers: { Authorization: `Bearer ${token}` }, cache: "no-store" })
      .then(response => { if (!response.ok) throw new Error("avatar"); return response.blob(); })
      .then(blob => { localUrl = URL.createObjectURL(blob); if (live) setAvatar({ reference, url: localUrl, failed: false });
        else URL.revokeObjectURL(localUrl); })
      .catch(() => { if (live) setAvatar({ reference, url: "", failed: true }); });
    return () => { live = false; if (localUrl) URL.revokeObjectURL(localUrl); };
  }, [custom, learner.learnerId, reference, token]);
  let providerUrl = null;
  try { const parsed = new URL(reference); if (parsed.protocol === "https:") providerUrl = reference; } catch { /* no provider URL */ }
  const failed = avatar.reference === reference && avatar.failed;
  const imageUrl = !failed && (custom ? avatar.reference === reference ? avatar.url : "" : providerUrl);
  const initials = learner.fullName?.split(/\s+/).filter(Boolean).slice(-2)
    .map(part => part[0]).join("").toUpperCase() || "HV";
  return <span className={className} aria-hidden="true">{imageUrl ?
    <img src={imageUrl} alt="" onError={() => setAvatar({ reference, url: "", failed: true })} /> : initials}</span>;
}
