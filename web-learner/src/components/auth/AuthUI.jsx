import { isValidElement, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";

export function AuthLayout({ title, subtitle, children, footer, wide = false, entry }) {
  return entry ? <SiteLayout className="auth-page auth-entry-page"><div className="site-container auth-entry-grid">
    <aside className="auth-visual-panel">
      <img className="auth-visual-photo" src="/images/home/students.jpg" alt="" width="480" height="320" />
      <Link className="auth-visual-brand" to="/">STO123</Link>
      <h2>Học đúng trọng tâm,<br />tiến bộ mỗi ngày</h2>
      <p>Lộ trình cá nhân hóa theo năng lực và mục tiêu TOEIC của bạn.</p>
      <div className="auth-visual-note"><span>MỖI NGÀY MỘT BƯỚC TIẾN</span><strong>Học từ vựng, luyện theo Part<br />và kiểm tra năng lực</strong><p>Học đều mỗi ngày, tiến bộ rõ ràng.</p></div>
      <ul className="auth-visual-benefits">
        <li><img src="/images/home/auth-dot.svg" alt="" width="10" height="10" />Lộ trình phù hợp trình độ</li>
        <li><img src="/images/home/auth-dot.svg" alt="" width="10" height="10" />Luyện tập đủ 7 Part</li>
        <li><img src="/images/home/auth-dot.svg" alt="" width="10" height="10" />Theo dõi tiến độ rõ ràng</li>
      </ul>
    </aside>
    <section className={"auth-card" + (wide ? " auth-card-wide" : "")}>
      <nav className="auth-entry-tabs" aria-label="Tài khoản STO123"><Link to="/login" aria-current={entry === "login" ? "page" : undefined}>Đăng nhập</Link><Link to="/register" aria-current={entry === "register" ? "page" : undefined}>Đăng ký</Link></nav>
      <div className="card-heading"><h1>{title}</h1><p>{subtitle}</p></div>
      {children}{footer && <div className="auth-footer">{footer}</div>}
    </section>
  </div></SiteLayout> : <SiteLayout className="auth-page"><div className="site-container">
    <Link className="auth-brand-label" to="/">STO123</Link>
    <div className={"auth-card" + (wide ? " auth-card-wide" : "")}>
      <div className="card-heading"><h1>{title}</h1><p>{subtitle}</p></div>
      {children}{footer && <div className="auth-footer">{footer}</div>}
    </div>
  </div></SiteLayout>;
}

export function FormInput({ label, id, type = "text", value, onChange, autoComplete, required = true, readOnly = false, hint, maxLength, inputMode, pattern }) {
  return <div className="form-field"><label htmlFor={id}>{label}</label>
    <input id={id} name={id} type={type} value={value} onChange={onChange} autoComplete={autoComplete}
      required={required} readOnly={readOnly} maxLength={maxLength} inputMode={inputMode} pattern={pattern} aria-describedby={hint ? id + "-hint" : undefined} />
    {hint && <small id={id + "-hint"}>{hint}</small>}</div>;
}

export function PasswordInput({ label, id, value, onChange, autoComplete }) {
  const [visible, setVisible] = useState(false);
  return <div className="form-field"><label htmlFor={id}>{label}</label><div className="password-wrap">
    <input id={id} name={id} type={visible ? "text" : "password"} value={value} onChange={onChange} autoComplete={autoComplete} required />
    <button className="reveal-button" type="button" onClick={() => setVisible(!visible)}
      aria-label={(visible ? "Ẩn " : "Hiện ") + label.toLowerCase()}><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z" /><circle cx="12" cy="12" r="3" />{visible && <path d="m3 3 18 18" />}</svg><span>{visible ? "Ẩn" : "Hiện"}</span></button>
  </div></div>;
}

function hasVisibleContent(value) {
  if (typeof value === "string") return value.trim().length > 0;
  if (typeof value === "number") return true;
  if (Array.isArray(value)) return value.some(hasVisibleContent);
  if (isValidElement(value)) return hasVisibleContent(value.props.children);
  return false;
}

export function AlertMessage({ children, type = "error" }) {
  return hasVisibleContent(children) ? <div className={"alert alert-" + type} role="alert">{children}</div> : null;
}

export function LoadingButton({ loading, children, busyText }) {
  return <button className="primary-button" type="submit" disabled={loading} aria-busy={loading}>{loading ? busyText : children}</button>;
}

export function PasswordRules({ password }) {
  return <ul className="password-rules" aria-label="Yêu cầu mật khẩu">
    <li className={password.length >= 8 ? "valid" : ""}>Ít nhất 8 ký tự</li>
    <li className={/[\p{L}]/u.test(password) ? "valid" : ""}>Có chữ cái</li>
    <li className={/\d/.test(password) ? "valid" : ""}>Có chữ số</li>
  </ul>;
}
