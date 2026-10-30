import { protectedReturnPath } from "./protectedReturnPath.js";

export function loginDestination(role, explicitReturnTo) {
  if (explicitReturnTo) return explicitReturnTo;
  if (role === "ADMIN_QUAN_LY") return "/admin";
  if (role === "ADMIN_NOI_DUNG") return "/admin/content";
  return "/";
}

export function requiresEmailVerification(error) {
  return error?.status === 403 && error?.data?.message === "email not verified";
}

export function preservePendingAuthReturnTo(candidate, storage) {
  const returnTo = protectedReturnPath(candidate);
  if (returnTo) storage.setItem("pendingAuthReturnTo", returnTo);
  else storage.removeItem("pendingAuthReturnTo");
  return returnTo;
}
