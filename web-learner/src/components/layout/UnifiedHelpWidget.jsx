import { useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import AiToeicChatWidget from "./AiToeicChatWidget";
import ContactSupportWidget from "./ContactSupportWidget";
import "../../styles/unified-help.css";

export default function UnifiedHelpWidget({ learningContext }) {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const [section, setSection] = useState("ai");
  const learner = user?.vaiTro === "HOC_VIEN";
  const admin = user?.vaiTro === "ADMIN_NOI_DUNG" || user?.vaiTro === "ADMIN_QUAN_LY";
  if (admin) return null;
  const current = learner ? section : "contact";
  return <div className="unified-help">
    <div className="unified-help-popup" role="dialog" aria-label="Trợ giúp STO123"
      style={!open ? { display: "none" } : undefined}>
      <div className="unified-help-tabs">
        {learner && <button type="button" className={current === "ai" ? "is-active" : ""}
          onClick={() => setSection("ai")}>Trợ lý AI</button>}
        <button type="button" className={current === "contact" ? "is-active" : ""}
          onClick={() => setSection("contact")}>Liên hệ hỗ trợ</button>
        <button type="button" className="unified-help-close" aria-label="Đóng trợ giúp"
          onClick={() => setOpen(false)}>×</button>
      </div>
      {learner && <div hidden={current !== "ai"}><AiToeicChatWidget embedded active={current === "ai"}
        onMinimize={() => setOpen(false)} learningContext={learningContext} /></div>}
      {current === "contact" && <ContactSupportWidget embedded />}
    </div>
    <button type="button" className="unified-help-launcher" aria-label={open ? "Thu nhỏ trợ giúp" : "Mở trợ giúp"}
      aria-expanded={open} onClick={() => setOpen(value => !value)}>
      <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M4 5h16v12H8l-4 3V5Z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round"/><path d="M8 10h8M8 13h5" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/></svg>
    </button>
  </div>;
}
