import { useState } from "react";
import { ADMIN_DATE_FILTER_STATUS, ADMIN_DATE_INVALID_DETAIL, ADMIN_DATE_INVALID_TITLE,
  ADMIN_DATE_MAX, ADMIN_DATE_MIN, adminDateDraftStatus, applyAdminDateDraft } from "./adminDateRange";

export function DateRangeFields({ from, to, maxDays, onApply, onClear,
  onDraftStateChange, fromLabel = "Từ ngày", toLabel = "Đến ngày" }) {
  const [values, setValues] = useState({ from, to });
  const [error, setError] = useState("");
  const appliedValues = { from, to };
  function publishStatus(nextValues, validate = false) {
    const status = adminDateDraftStatus(nextValues, appliedValues, maxDays, validate);
    onDraftStateChange?.(status);
    return status;
  }
  function change(name, value) {
    const next = { ...values, [name]: value };
    setValues(next);
    setError("");
    publishStatus(next);
  }
  function apply() {
    const result = applyAdminDateDraft(values, maxDays, next => {
      publishStatus(next);
      onApply(next);
    });
    setError(result.valid ? "" : `${ADMIN_DATE_INVALID_TITLE} ${ADMIN_DATE_INVALID_DETAIL}`);
    if (!result.valid) onDraftStateChange?.(ADMIN_DATE_FILTER_STATUS.INVALID_FILTER);
  }
  return <><label className="admin-filter admin-filter-field"><span>{fromLabel}</span><input type="date"
    min={ADMIN_DATE_MIN} max={ADMIN_DATE_MAX} value={values.from}
    onChange={event => change("from", event.target.value)}/></label>
    <label className="admin-filter admin-filter-field"><span>{toLabel}</span><input type="date"
      min={ADMIN_DATE_MIN} max={ADMIN_DATE_MAX} value={values.to}
      onChange={event => change("to", event.target.value)}/></label>
    <div className="admin-filter-actions">
      <button className="admin-button" type="button" onClick={apply}>Áp dụng</button>
      <button className="admin-button admin-button-secondary admin-filter-clear" type="button"
        onClick={() => { setValues({ from: "", to: "" }); setError("");
          publishStatus({ from: "", to: "" }); onClear(); }}>Xóa bộ lọc</button>
    </div>
    {error && !onDraftStateChange && <p className="admin-date-error admin-filter-date-error" role="alert">{error}</p>}</>;
}
