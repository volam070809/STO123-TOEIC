import { createContext, useContext } from "react";

export const GuestAuthPromptContext = createContext(null);

export function useGuestAuthPrompt() {
  const context = useContext(GuestAuthPromptContext);
  if (!context) throw new Error("useGuestAuthPrompt must be used inside GuestAuthPromptProvider");
  return context;
}
