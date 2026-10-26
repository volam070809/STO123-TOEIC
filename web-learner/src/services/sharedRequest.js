const readStores = new Set();

function withCancellation(request, signal) {
  if (!signal) return request;
  return new Promise((resolve, reject) => {
    const canceled = () => reject(signal.reason ?? Object.assign(new Error("Request canceled."), { name: "AbortError" }));
    if (signal.aborted) { canceled(); return; }
    signal.addEventListener("abort", canceled, { once: true });
    request.then(value => {
      signal.removeEventListener("abort", canceled);
      resolve(value);
    }, error => {
      signal.removeEventListener("abort", canceled);
      reject(error);
    });
  });
}

export function createSharedRequest({ ttlMs = 0, now = Date.now } = {}) {
  const pending = new Map();
  const successful = new Map();
  let generation = 0;
  const store = { clear: () => { generation += 1; successful.clear(); } };
  readStores.add(store);

  function share(key, load, signal) {
    const cached = successful.get(key);
    if (cached && cached.expiresAt > now()) {
      return withCancellation(Promise.resolve(cached.value), signal);
    }
    if (cached) successful.delete(key);

    let request = pending.get(key);
    if (!request) {
      const requestGeneration = generation;
      request = Promise.resolve().then(load).then(value => {
        if (ttlMs > 0 && requestGeneration === generation) successful.set(key, { value, expiresAt: now() + ttlMs });
        return value;
      }).finally(() => {
        if (pending.get(key) === request) pending.delete(key);
      });
      pending.set(key, request);
    }
    return withCancellation(request, signal);
  }

  share.clear = store.clear;
  share.invalidate = predicate => {
    for (const key of successful.keys()) if (predicate(key)) successful.delete(key);
  };
  return share;
}

export function clearSharedReadCache() {
  for (const store of readStores) store.clear();
}
