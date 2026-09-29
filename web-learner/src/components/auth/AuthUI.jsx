import { isValidElement, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";

export function AuthLayout({ title, subtitle, children, footer, wide = false }) {
  return <SiteLayout className="auth-page"><div className="site-container">
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
      aria-label={(visible ? "Ẩn " : "Hiện ") + label.toLowerCase()}>{visible ? "Ẩn" : "Hiện"}</button>
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
