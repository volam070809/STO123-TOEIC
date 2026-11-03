import { useCallback, useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { apiRequest } from "../../services/api";
import "../../styles/contact-support.css";

const fetchLinks = () => apiRequest("/api/contact-support", { cache: "no-store" });

function Icon({ kind }) {
  if (kind === "zalo") return <span className="contact-icon contact-icon-zalo" aria-hidden="true">Z</span>;
  if (kind === "messenger") return <svg className="contact-icon contact-icon-messenger" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 2C6.5 2 2 6.2 2 11.5c0 3 1.4 5.7 3.7 7.4V22l3.4-1.9c.9.3 1.9.4 2.9.4 5.5 0 10-4.2 10-9.5S17.5 2 12 2Zm1 12.5-2.5-2.7-5 2.7 5.5-6 2.5 2.7 5-2.7-5.5 6Z"/></svg>;
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16v12H8l-4 3V5Z" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round"/><path d="M8 10h8M8 13h5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/></svg>;
}

export default function ContactSupportWidget() {
  const { pathname } = useLocation();
  const hidden = /^\/exam\//.test(pathname) || pathname === "/placement" ||
    /^\/courses\/\d+\/steps\//.test(pathname) || pathname === "/practice/parts";
  const [open, setOpen] = useState(false);
  const [links, setLinks] = useState(null);
  const [error, setError] = useState(false);
  const requestId = useRef(0);
  const load = useCallback(async () => {
    const current = ++requestId.current;
    try { const data = await fetchLinks(); if (current === requestId.current) { setLinks(data); setError(false); } }
    catch { if (current === requestId.current) { setLinks(null); setError(true); } }
  }, []);
  useEffect(() => {
    if (hidden) return undefined;
    let active = true;
    const current = ++requestId.current;
    fetchLinks().then(data => { if (active && current === requestId.current) { setLinks(data); setError(false); } })
      .catch(() => { if (active && current === requestId.current) { setLinks(null); setError(true); } });
    const refresh = () => { if (document.visibilityState === "visible") load(); };
    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", refresh);
    return () => { active = false; window.removeEventListener("focus", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [load, hidden]);
  if (hidden || (!error && !links?.zaloUrl && !links?.messengerUrl)) return null;
  return <div className="contact-widget">
    {open && <div className="contact-widget-menu" id="contact-options">
      <strong>Liên hệ hỗ trợ</strong>
      {error && <p>Chưa tải được liên kết. <button type="button" onClick={load}>Thử lại</button></p>}
      {links?.zaloUrl && <a href={links.zaloUrl} target="_blank" rel="noopener noreferrer"><Icon kind="zalo" /> Zalo</a>}
      {links?.messengerUrl && <a href={links.messengerUrl} target="_blank" rel="noopener noreferrer"><Icon kind="messenger" /> Messenger</a>}
    </div>}
    <button type="button" className="contact-widget-toggle" aria-label={open ? "Đóng liên hệ hỗ trợ" : "Mở liên hệ hỗ trợ"} aria-expanded={open} aria-controls="contact-options" onClick={() => setOpen(value => !value)}><Icon kind="bubble" /><span>{open ? "Đóng" : "Liên hệ"}</span></button>
  </div>;
}
