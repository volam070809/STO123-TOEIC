import { useState } from "react";

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const phonePattern = /^\+?[0-9]{9,15}$/;

export default function AdminLearnerForm({ mode, initial, submitting, serverError, serverErrors, onSubmit }) {
  const creating = mode === "create";
  const [values, setValues] = useState({
    fullName: initial?.fullName ?? "",
    email: initial?.email ?? "",
    phoneNumber: initial?.phoneNumber ?? "",
    password: "",
    confirmPassword: "",
  });
  const [clientErrors, setClientErrors] = useState({});
  const errors = { ...serverErrors, ...clientErrors };

  function change(field, value) {
    setValues(current => ({ ...current, [field]: value }));
    setClientErrors(current => ({ ...current, [field]: "" }));
  }

  function submit(event) {
    event.preventDefault();
    const nextErrors = validate(values, creating);
    setClientErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;
    onSubmit({
      fullName: values.fullName.trim(),
      phoneNumber: values.phoneNumber.trim() || null,
      ...(creating ? { email: values.email.trim(), password: values.password } : {}),
    });
  }

  return <form className="admin-form" onSubmit={submit} noValidate>
    <div className="admin-form-grid">
      <Field id="learner-name" label="Họ và tên" required error={errors.fullName}>
        <input id="learner-name" value={values.fullName} maxLength={64} autoComplete="name"
          onChange={event => change("fullName", event.target.value)} />
      </Field>
      <Field id="learner-email" label="Email" required error={errors.email}
        hint={!creating ? "Email đăng nhập không được thay đổi trong thao tác sửa hồ sơ." : "Mã xác thực sẽ được gửi đến địa chỉ này."}>
        <input id="learner-email" type="email" value={values.email} maxLength={254} autoComplete="email"
          readOnly={!creating} aria-readonly={!creating} onChange={event => change("email", event.target.value)} />
      </Field>
      <Field id="learner-phone" label="Số điện thoại" error={errors.phoneNumber}
        hint="Gồm 9–15 chữ số, có thể bắt đầu bằng dấu +.">
        <input id="learner-phone" value={values.phoneNumber} maxLength={16} inputMode="tel" autoComplete="tel"
          onChange={event => change("phoneNumber", event.target.value)} />
      </Field>
      {creating && <>
        <Field id="learner-password" label="Mật khẩu ban đầu" required error={errors.password}
          hint="Ít nhất 8 ký tự, gồm chữ và số.">
          <input id="learner-password" type="password" value={values.password} maxLength={128}
            autoComplete="new-password" onChange={event => change("password", event.target.value)} />
        </Field>
        <Field id="learner-confirm" label="Nhập lại mật khẩu" required error={errors.confirmPassword}>
          <input id="learner-confirm" type="password" value={values.confirmPassword} maxLength={128}
            autoComplete="new-password" onChange={event => change("confirmPassword", event.target.value)} />
        </Field>
      </>}
    </div>
    {creating && <div className="admin-form-note"><strong>Quy tắc xác thực</strong>
      <p>Tài khoản luôn được tạo với vai trò Học viên và trạng thái Chờ xác thực. Học viên phải xác thực email trước khi đăng nhập.</p></div>}
    {serverError && <div className="admin-feedback is-error" role="alert">{serverError}</div>}
    <div className="admin-form-actions">
      <button className="admin-button" type="submit" disabled={submitting}>{submitting ? "Đang lưu..." :
        creating ? "Tạo học viên" : "Lưu thay đổi"}</button>
    </div>
  </form>;
}

function Field({ id, label, required, error, hint, children }) {
  return <div className={"admin-field " + (error ? "has-error" : "")}>
    <label htmlFor={id}>{label}{required && <span aria-hidden="true"> *</span>}</label>
    {children}
    {error ? <small className="admin-field-error">{error}</small> : hint && <small>{hint}</small>}
  </div>;
}

function validate(values, creating) {
  const errors = {};
  const name = values.fullName.trim();
  const phone = values.phoneNumber.trim();
  if (!name || name.length > 64) errors.fullName = "Họ tên phải có từ 1 đến 64 ký tự.";
  if (phone && !phonePattern.test(phone)) errors.phoneNumber = "Số điện thoại không hợp lệ.";
  if (creating) {
    const email = values.email.trim();
    if (!emailPattern.test(email) || email.length > 254) errors.email = "Email không hợp lệ.";
    if (values.password.length < 8 || !/\p{L}/u.test(values.password) || !/\d/.test(values.password))
      errors.password = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ và số.";
    if (values.password !== values.confirmPassword) errors.confirmPassword = "Mật khẩu nhập lại không khớp.";
  }
  return errors;
}
