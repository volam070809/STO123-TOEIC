export async function withCreationKey(path, payload, send, storage = sessionStorage, cryptoApi = crypto) {
  const bytes = new TextEncoder().encode(JSON.stringify({ path, payload }));
  const digest = await cryptoApi.subtle.digest("SHA-256", bytes);
  const storageKey = `sto123.content.create.${Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("")}`;
  let key = storage.getItem(storageKey);
  if (!key) { key = cryptoApi.randomUUID(); storage.setItem(storageKey, key); }
  const result = await send(key);
  storage.removeItem(storageKey);
  return result;
}
