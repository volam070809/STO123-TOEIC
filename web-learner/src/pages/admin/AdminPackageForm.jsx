import { useEffect, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { adminCourseApi } from "../../services/adminCourseApi";
import { courseStatuses, formatMoney, statusLabel, statusTone } from "./adminFormat";
import { validatePackage } from "./adminPackageFormData";

export default function AdminPackageForm({ mode, initial, submitting, serverError, serverErrors, onSubmit }) {
  const { token } = useAuth();
  const [values, setValues] = useState({ name: initial?.name ?? "", price: initial?.price?.toString() ?? "",
    validDays: initial?.validDays?.toString() ?? "", description: initial?.description ?? "",
    status: initial?.status ?? "DANG_MO", courseIds: initial?.courses?.map(item => item.courseId) ?? [] });
  const [options, setOptions] = useState({ loading: true, items: [], error: "" });
  const [clientErrors, setClientErrors] = useState({});
  const errors = { ...serverErrors, ...clientErrors };

  useEffect(() => {
    let active = true;
    adminCourseApi.list({ search: "", status: "", stage: "", page: 1, pageSize: 100 }, token)
      .then(result => { if (active) setOptions({ loading: false, items: result.items, error: "" }); })
      .catch(() => { if (active) setOptions({ loading: false, items: [], error: "Không thể tải danh sách khóa học." }); });
    return () => { active = false; };
  }, [token]);

  function change(field, value) { setValues(current => ({ ...current, [field]: value })); setClientErrors(current => ({ ...current, [field]: "" })); }
  function toggleCourse(id) { change("courseIds", values.courseIds.includes(id) ? values.courseIds.filter(item => item !== id) : [...values.courseIds, id]); }
  function submit(event) {
    event.preventDefault(); const next = validatePackage(values); setClientErrors(next);
    if (Object.keys(next).length) return;
    onSubmit({ name: values.name.trim(), price: Number(values.price), validDays: Number(values.validDays),
      description: values.description.trim() || null, status: values.status, courseIds: values.courseIds });
  }

  return <form className="admin-form" onSubmit={submit} noValidate><div className="admin-form-grid">
    <Field id="package-name" label="Tên gói học" required error={errors.name} wide><input id="package-name" value={values.name} maxLength={64} onChange={e => change("name", e.target.value)} /></Field>
    <Field id="package-price" label="Giá gói" required error={errors.price} hint="Giá nguyên VND, tương thích VNPay."><input id="package-price" type="number" min="1" max="9999999999" step="1" value={values.price} onChange={e => change("price", e.target.value)} /></Field>
    <Field id="package-days" label="Thời hạn sử dụng" required error={errors.validDays} hint="Số ngày được lưu tại thời điểm đăng ký."><input id="package-days" type="number" min="1" step="1" value={values.validDays} onChange={e => change("validDays", e.target.value)} /></Field>
    <Field id="package-status" label="Trạng thái" required error={errors.status}><select id="package-status" value={values.status} onChange={e => change("status", e.target.value)}>{Object.entries(courseStatuses).map(([value,label]) => <option value={value} key={value}>{label}</option>)}</select></Field>
    <Field id="package-description" label="Mô tả" error={errors.description} wide><textarea id="package-description" rows="4" maxLength={512} value={values.description} onChange={e => change("description", e.target.value)} /></Field>
    <div className={`admin-field admin-field-wide ${errors.courseIds ? "has-error" : ""}`}><label>Khóa học trong gói <span aria-hidden="true">*</span></label>
      {options.loading ? <div className="admin-option-state">Đang tải khóa học...</div> : options.error ? <div className="admin-feedback is-error">{options.error}</div> :
        <div className="admin-course-picker">{options.items.map(course => <label key={course.courseId} className={values.courseIds.includes(course.courseId) ? "is-selected" : ""}>
          <input type="checkbox" checked={values.courseIds.includes(course.courseId)} onChange={() => toggleCourse(course.courseId)} />
          <span><strong>Giai đoạn {course.stage} · {course.name}</strong><small>{course.retailPrice == null ? "Chưa có giá bán lẻ" : formatMoney(course.retailPrice)}</small></span>
          <em className={`admin-status is-${statusTone(course.status)}`}>{statusLabel(course.status)}</em>
        </label>)}</div>}
      {errors.courseIds && <small className="admin-field-error">{errors.courseIds}</small>}
    </div>
  </div>
  <div className="admin-form-note"><strong>Bảo vệ lịch sử</strong><p>Sau khi gói có đăng ký, danh sách khóa học không thể thay đổi. Giá và thời hạn mới chỉ áp dụng cho đăng ký mới; dữ liệu cũ được giữ nguyên.</p></div>
  {serverError && <div className="admin-feedback is-error" role="alert">{serverError}</div>}
  <div className="admin-form-actions"><button className="admin-button" disabled={submitting || options.loading || !!options.error}>{submitting ? "Đang lưu..." : mode === "create" ? "Tạo gói học" : "Lưu thay đổi"}</button></div></form>;
}

function Field({ id, label, required, error, hint, wide, children }) {
  return <div className={`admin-field ${error ? "has-error" : ""} ${wide ? "admin-field-wide" : ""}`}><label htmlFor={id}>{label}{required && <span aria-hidden="true"> *</span>}</label>{children}{error ? <small className="admin-field-error">{error}</small> : hint && <small>{hint}</small>}</div>;
}
