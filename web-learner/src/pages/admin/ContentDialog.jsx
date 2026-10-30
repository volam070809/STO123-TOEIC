export default function ContentDialog({ title, children, busy, error, onCancel, onSubmit, submitLabel = "Lưu", danger = false }) {
  return <div className="admin-modal-backdrop" role="presentation" onMouseDown={event => {
    if (event.target === event.currentTarget && !busy) onCancel();
  }}><section className="admin-modal" role="dialog" aria-modal="true" aria-label={title}>
    <h2>{title}</h2>
    <form onSubmit={event => { event.preventDefault(); onSubmit(); }}>
      {children}
      {error && <p className="content-error" role="alert">{error}</p>}
      <div className="admin-modal-actions">
        <button type="button" className="admin-button admin-button-secondary" disabled={busy} onClick={onCancel}>Hủy</button>
        <button type="submit" className={`admin-button ${danger ? "admin-button-danger" : ""}`} disabled={busy}>
          {busy ? "Đang lưu…" : submitLabel}
        </button>
      </div>
    </form>
  </section></div>;
}

export function ContentField({ label, children }) {
  return <label className="content-field"><span>{label}</span>{children}</label>;
}
