import { useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import AiToeicChatWidget from "./AiToeicChatWidget";
import ContactSupportWidget from "./ContactSupportWidget";
import "../../styles/unified-help.css";

export default function UnifiedHelpWidget({ learningContext }) {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const [section, setSection] = useState(null);
  const learner = user?.vaiTro === "HOC_VIEN";
  const admin = user?.vaiTro === "ADMIN_NOI_DUNG" || user?.vaiTro === "ADMIN_QUAN_LY";
  if (admin) return null;
  return <div className="unified-help">
    <div className={`unified-help-popup is-${section || "menu"}`} role="dialog" aria-label="Trợ giúp STO123" hidden={!open}>
      {section ? <div className="unified-help-tabs"><button type="button" className="unified-help-back"
        onClick={() => setSection(null)}>‹ Chọn hỗ trợ</button><strong>{section === "ai" ? "Trợ lý AI" : "Liên hệ"}</strong>
        <button type="button" className="unified-help-close" aria-label="Đóng trợ giúp" onClick={() => setOpen(false)}>×</button></div> :
        <div className="unified-help-choices">
          <strong>Bạn cần hỗ trợ gì?</strong>
          {learner && <button type="button" onClick={() => setSection("ai")}>Trợ lý AI <span>›</span></button>}
          <button type="button" onClick={() => setSection("contact")}>Liên hệ <span>›</span></button>
        </div>}
      {learner && <div hidden={section !== "ai"}><AiToeicChatWidget embedded active={open && section === "ai"} onMinimize={() => setOpen(false)} learningContext={learningContext} /></div>}
      {section === "contact" && <ContactSupportWidget embedded showTitle={false} />}
    </div>
    <button type="button" className="unified-help-launcher" aria-label={open ? "Thu nhỏ trợ giúp" : "Mở trợ giúp"}
      aria-expanded={open} onClick={() => { setOpen(value => !value); setSection(null); }}>
      <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M4 5h16v12H8l-4 3V5Z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round"/><path d="M8 10h8M8 13h5" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/></svg>
    </button>
  </div>;
}
