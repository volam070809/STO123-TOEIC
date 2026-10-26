import { useCallback, useEffect, useMemo, useReducer, useRef } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { GuestAuthPromptContext } from "../../contexts/GuestAuthPromptState";
import { authorizeProtectedAction, guestAuthPromptReducer, isGuestAuthDismissKey } from "./guestAuthPromptPolicy";

const FOCUSABLE = "a[href], button:not([disabled]), [tabindex]:not([tabindex='-1'])";

export function GuestAuthDialog({ returnTo, onClose }) {
  const dialogRef = useRef(null);
  const closeRef = useRef(null);
  const previousFocusRef = useRef(null);

  useEffect(() => {
    previousFocusRef.current = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      document.body.style.overflow = previousOverflow;
      previousFocusRef.current?.focus?.();
    };
  }, []);

  function handleKeyDown(event) {
    if (isGuestAuthDismissKey(event.key)) {
      event.preventDefault();
      onClose();
      return;
    }
    if (event.key !== "Tab") return;
    const focusable = [...dialogRef.current.querySelectorAll(FOCUSABLE)];
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  const authState = { returnTo };
  return <div className="guest-auth-backdrop" role="presentation" onMouseDown={event => {
    if (event.target === event.currentTarget) onClose();
  }}>
    <section className="guest-auth-dialog" ref={dialogRef} role="dialog" aria-modal="true"
      aria-labelledby="guest-auth-title" aria-describedby="guest-auth-description" onKeyDown={handleKeyDown}>
      <button ref={closeRef} className="guest-auth-close" type="button" aria-label="Đóng hộp thoại" onClick={onClose}>
        <svg aria-hidden="true" viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18" /></svg>
      </button>
      <div className="guest-auth-icon" aria-hidden="true">
        <svg viewBox="0 0 24 24"><path d="M12 13a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 7c.8-3.4 3.1-5 7-5s6.2 1.6 7 5" /></svg>
      </div>
      <h2 id="guest-auth-title">Đăng nhập để tiếp tục</h2>
      <p id="guest-auth-description">Vui lòng đăng nhập hoặc đăng ký tài khoản STO123 để sử dụng chức năng này.</p>
      <div className="guest-auth-actions">
        <Link className="primary-button" to="/login" state={authState} onClick={onClose}>Đăng nhập</Link>
        <Link className="outline-button" to="/register" state={authState} onClick={onClose}>Đăng ký</Link>
      </div>
    </section>
  </div>;
}

export default function GuestAuthPromptProvider({ children }) {
  const { isAuthenticated } = useAuth();
  const [prompt, dispatch] = useReducer(guestAuthPromptReducer, null);
  const close = useCallback(() => dispatch({ type: "close" }), []);
  const requireAuthentication = useCallback((destination, operation) => {
    const decision = authorizeProtectedAction(isAuthenticated, destination, operation);
    if (!decision.allowed && decision.returnTo) dispatch({ type: "open", returnTo: decision.returnTo });
    if (decision.allowed) dispatch({ type: "close" });
    return decision.allowed;
  }, [isAuthenticated]);
  const value = useMemo(() => ({ requireAuthentication, close }), [requireAuthentication, close]);

  return <GuestAuthPromptContext.Provider value={value}>
    {children}
    {prompt && !isAuthenticated && <GuestAuthDialog returnTo={prompt.returnTo} onClose={close} />}
  </GuestAuthPromptContext.Provider>;
}
