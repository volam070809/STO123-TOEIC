import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminEnrollmentApi } from "../../services/adminEnrollmentApi";
import { formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";
import { accessStateLabel, accessTone, enrollmentStatuses, paymentStateLabel, paymentTone, purchaseTypeLabel } from "./adminEnrollmentFormat";
import { AdminMessage } from "./AdminLearnersPage";
import { DateRangeFields } from "./AdminDateRangeFields";
import { ADMIN_DATE_FILTER_STATUS, adminDateDisplayState, validateAdminDateRange } from "./adminDateRange";

const PAGE_SIZE = 20;

export default function AdminEnrollmentsPage() {
  const { token } = useAuth();
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => ({
    search: params.get("search")?.trim() ?? "", status: params.get("status") ?? "",
    packageId: params.get("packageId") ?? "", courseId: params.get("courseId") ?? "",
    dateFrom: params.get("dateFrom") ?? "", dateTo: params.get("dateTo") ?? "",
    page: Math.max(1, Number(params.get("page")) || 1), pageSize: PAGE_SIZE,
  }), [params]);
  const dateError = validateAdminDateRange({ from: filters.dateFrom, to: filters.dateTo }).error;
  const appliedDateKey = `${filters.dateFrom}|${filters.dateTo}`;
  const [dateDraft, setDateDraft] = useState({ key: appliedDateKey, status: ADMIN_DATE_FILTER_STATUS.VALID_APPLIED });
  const draftStatus = dateDraft.key === appliedDateKey ? dateDraft.status : ADMIN_DATE_FILTER_STATUS.VALID_APPLIED;
  const dateDisplay = adminDateDisplayState(draftStatus, dateError);
  const [revision, setRevision] = useState(0);
  const key = `${token}|${JSON.stringify(filters)}|${revision}`;
  const [request, setRequest] = useState({ key: null, result: null, error: "" });
  const current = request.key === key ? request : { result: null, error: "" };
  const loading = dateDisplay.showResults && !dateError && request.key !== key;
  useEffect(() => {
    let active = true;
    if (dateError) return () => { active = false; };
    adminEnrollmentApi.list(filters, token).then(result => {
      if (active) setRequest({ key, result, error: "" });
    }).catch(error => {
      if (active) setRequest({ key, result: null, error: error.data?.message || "Không thể tải danh sách đăng ký học." });
    });
    return () => { active = false; };
  }, [key, token, filters, dateError]);

  function update(changes) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([name, value]) => value ? next.set(name, String(value)) : next.delete(name));
    setParams(next);
  }
  const result = dateDisplay.showResults ? current.result : null;
  const hasFilters = ["search", "status", "packageId", "courseId", "dateFrom", "dateTo"].some(name => filters[name]);
  return <div className="admin-page">
    <div className="admin-page-heading"><div><h1>Đăng ký</h1></div>
      <div className="admin-count-card"><strong>{result?.totalItems ?? "—"}</strong><span>đăng ký</span></div></div>
    {result && <Summary data={result.summary} />}
    <section className="admin-panel">
      <EnrollmentFilters key={filters.search} filters={filters} update={update} clear={() => setParams({})}
        onDraftStateChange={status => setDateDraft({ key: appliedDateKey, status })}/>
      {!dateDisplay.showResults ? <AdminMessage title={dateDisplay.title} detail={dateDisplay.detail}/> :
        loading ? <Loading /> : current.error ? <AdminMessage title="Không tải được dữ liệu" detail={current.error}
        action={<button className="admin-button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>} /> :
      result?.items?.length ? <><div className="admin-table-wrap"><table className="admin-table admin-enrollment-table">
        <thead><tr><th>Học viên</th><th>Loại mua</th><th>Giá đăng ký</th><th>Ngày đăng ký</th><th>Trạng thái lưu</th><th>Thanh toán</th><th>Hiệu lực</th><th></th></tr></thead>
        <tbody>{result.items.map(item => <tr key={item.enrollmentId}>
          <td><strong className="admin-cell-main">{item.learnerName}</strong><small>{item.learnerEmail}</small><small>ID học viên #{item.learnerId}</small></td>
          <td><strong className="admin-cell-main">{purchaseTypeLabel(item.purchaseType)}</strong><small>{item.packageName || courseSummary(item.courses)}</small></td>
          <td><strong>{formatMoney(item.registrationPrice)}</strong><small>{item.accessDays} ngày</small></td>
          <td>{formatDate(item.registeredAt, true)}<small>Hết hạn: {formatDate(item.expiresAt, true)}</small></td>
          <td><span className={`admin-status is-${statusTone(item.storedStatus)}`}>{statusLabel(item.storedStatus)}</span></td>
          <td><span className={`admin-status is-${paymentTone(item.paymentState)}`}>{paymentStateLabel(item.paymentState)}</span><small>{item.paymentCount} giao dịch</small></td>
          <td><span className={`admin-status is-${accessTone(item.accessState)}`}>{accessStateLabel(item.accessState)}</span></td>
          <td><Link className="admin-row-link" to={`/admin/enrollments/${item.enrollmentId}`}>Xem →</Link></td>
        </tr>)}</tbody></table></div><Pagination result={result} onPage={page => update({ page: page === 1 ? "" : page })} /></> :
        <AdminMessage title="Không tìm thấy đăng ký học" detail={hasFilters ? "Hãy thử điều chỉnh bộ lọc." : "Chưa có đăng ký học trong hệ thống."}
          action={hasFilters && <button className="admin-button admin-button-secondary" onClick={() => setParams({})}>Xóa bộ lọc</button>} />}
    </section>
  </div>;
}

function EnrollmentFilters({ filters, update, clear, onDraftStateChange }) {
  const [search, setSearch] = useState(filters.search);
  return <div className="admin-enrollment-filters"><form className="admin-search-form" onSubmit={event => { event.preventDefault(); update({ search: search.trim(), page: "" }); }}>
    <label className="admin-search"><span aria-hidden="true">⌕</span><input value={search} maxLength={100} onChange={event => setSearch(event.target.value)} placeholder="Tên, email, ID đăng ký/học viên/giao dịch" /></label>
    <button className="admin-button">Tìm kiếm</button></form>
    <div className="admin-filter-grid admin-enrollment-filter-grid">
      <Filter label="Trạng thái"><select value={filters.status} onChange={event => update({ status: event.target.value, page: "" })}><option value="">Tất cả</option>{Object.entries(enrollmentStatuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></Filter>
      <Filter label="ID gói học"><input inputMode="numeric" min="1" type="number" value={filters.packageId} onChange={event => update({ packageId: event.target.value, page: "" })} placeholder="Tất cả" /></Filter>
      <Filter label="ID khóa học"><input inputMode="numeric" min="1" type="number" value={filters.courseId} onChange={event => update({ courseId: event.target.value, page: "" })} placeholder="Tất cả" /></Filter>
      <DateRangeFields key={`${filters.dateFrom}|${filters.dateTo}`} from={filters.dateFrom} to={filters.dateTo}
        onApply={values => update({ dateFrom: values.from, dateTo: values.to, page: "" })}
        onClear={clear} onDraftStateChange={onDraftStateChange}/>
    </div></div>;
}
function Filter({ label, children }) { return <label className="admin-filter admin-filter-field"><span>{label}</span>{children}</label>; }
function Summary({ data }) { return <div className="admin-stat-grid admin-enrollment-stats"><Stat label="Tổng đăng ký" value={data.total}/><Stat label="Mua gói" value={data.packagePurchases}/><Stat label="Mua trực tiếp" value={data.directCoursePurchases}/><Stat label="Đang có quyền học" value={data.currentlyActive}/><Stat label="Chờ thanh toán" value={data.awaitingPayment}/></div>; }
function Stat({ label, value }) { return <div className="admin-stat"><span>{label}</span><strong>{value}</strong></div>; }
function Loading() { return <div className="admin-loading"><span className="admin-spinner"/><strong>Đang tải đăng ký học</strong><span>Đang tổng hợp thanh toán và quyền truy cập.</span></div>; }
function courseSummary(courses) { return courses.length ? courses.map(course => course.name).join(" · ") : "Chưa có khóa học liên kết"; }
function Pagination({ result, onPage }) { const { page, pageSize, totalItems, totalPages } = result; if (totalPages <= 1) return <div className="admin-pagination admin-pagination-single">Hiển thị {totalItems} đăng ký</div>; return <div className="admin-pagination"><span>Hiển thị {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, totalItems)} trong {totalItems}</span><div><button disabled={page <= 1} onClick={() => onPage(page - 1)}>← Trước</button><strong>Trang {page}/{totalPages}</strong><button disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Sau →</button></div></div>; }
