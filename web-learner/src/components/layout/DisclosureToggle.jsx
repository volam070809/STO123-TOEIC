import "../../styles/disclosure-toggle.css";

export default function DisclosureToggle() {
  return <span className="learner-disclosure-toggle">
    <span className="learner-toggle-closed">Xem chi tiết</span>
    <span className="learner-toggle-open">Thu gọn</span>
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m5 7.5 5 5 5-5" />
    </svg>
  </span>;
}
