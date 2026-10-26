import { protectedReturnPath } from "../../routes/protectedReturnPath";

export function authorizeProtectedAction(isAuthenticated, destination, operation) {
  if (isAuthenticated) {
    operation?.();
    return { allowed: true, returnTo: null };
  }
  return { allowed: false, returnTo: protectedReturnPath(destination) };
}

export function guestAuthPromptReducer(state, action) {
  if (action.type === "open" && action.returnTo) return { returnTo: action.returnTo };
  if (action.type === "close") return null;
  return state;
}

export function isGuestAuthDismissKey(key) {
  return key === "Escape";
}
