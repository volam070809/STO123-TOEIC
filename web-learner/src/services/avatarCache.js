import { avatarApi } from "./avatarApi";

let cached = null;

export function clearAvatarCache() {
  if (cached?.url) URL.revokeObjectURL(cached.url);
  cached = null;
}

export function loadAvatarOnce(key, token) {
  if (cached?.key === key) return cached.promise;
  clearAvatarCache();
  const entry = { key, url: null, promise: null };
  cached = entry;
  entry.promise = avatarApi.image(token).then(blob => {
    if (cached !== entry) return null;
    entry.url = URL.createObjectURL(blob);
    return entry.url;
  }).catch(error => {
    if (cached === entry) cached = null;
    throw error;
  });
  return entry.promise;
}
