// SQL rowversion is a fixed-width, big-endian hexadecimal string. Network polling
// can finish after a newer SignalR notification, so keep the newer match snapshot.
export function preferFreshArenaState(current, incoming) {
  if (current?.id === incoming?.id && current.revision && incoming.revision &&
      incoming.revision < current.revision) return current;
  return incoming;
}
