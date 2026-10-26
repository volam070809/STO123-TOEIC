import { Fragment, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminPaymentApi } from "../../services/adminPaymentApi";
import { formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";
import { purchaseTypeLabel } from "./adminEnrollmentFormat";
import { AdminMessage } from "./AdminLearnersPage";
import { DateRangeFields } from "./AdminDateRangeFields";
import { ADMIN_DATE_FILTER_STATUS, adminDateDisplayState, validateAdminDateRange } from "./adminDateRange";
import { nextPaymentHistoryLearner, paymentHistoryRegionId, paymentHistoryState } from "./adminPaymentViewState";

const PAGE_SIZE = 20;
const paymentStatuses = { CHO_XU_LY: "Chờ xử lý", THANH_CONG: "Thành công", THAT_BAI: "Thất bại" };

export default function AdminPaymentsPage() {
  const { token } = useAuth(); const [params, setParams] = useSearchParams();
  const view = params.get("view") === "transactions" ? "transactions" : "learners";
  const filters = useMemo(() => ({ search: params.get("search")?.trim() ?? "",
    status: params.get("status") ?? "", gateway: params.get("gateway") ?? "",
    dateFrom: params.get("dateFrom") ?? "", dateTo: params.get("dateTo") ?? "",
    page: Math.max(1, Number(params.get("page")) || 1), pageSize: PAGE_SIZE }), [params]);
  const dateError = validateAdminDateRange({ from: filters.dateFrom, to: filters.dateTo }).error;
  const appliedDateKey = `${filters.dateFrom}|${filters.dateTo}`;
  const [dateDraft, setDateDraft] = useState({ key: appliedDateKey, status: ADMIN_DATE_FILTER_STATUS.VALID_APPLIED });
  const draftStatus = dateDraft.key === appliedDateKey ? dateDraft.status : ADMIN_DATE_FILTER_STATUS.VALID_APPLIED;
  const dateDisplay = adminDateDisplayState(draftStatus, dateError);
  const [revision, setRevision] = useState(0); const key = `${token}|${view}|${JSON.stringify(filters)}|${revision}`;
  const [request, setRequest] = useState({ key: null, result: null, error: "" });
  const [selectedLearner, setSelectedLearner] = useState(null);
  const current = request.key === key ? request : { result: null, error: "" };
  const loading = dateDisplay.showResults && !dateError && request.key !== key;
  useEffect(() => { let active = true; if (dateError) return () => { active = false; };
    const load = view === "learners" ? adminPaymentApi.learners(filters, token) : adminPaymentApi.list(filters, token);
    load.then(result => {
    if (active) setRequest({ key, result, error: "" });
  }).catch(error => { if (active) setRequest({ key, result: null,
    error: error.data?.message || "Không thể tải danh sách giao dịch thanh toán." }); });
    return () => { active = false; }; }, [key, token, filters, dateError, view]);
  function update(changes) { const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([name, value]) => value ? next.set(name, String(value)) : next.delete(name)); setParams(next); }
  function clearFilters() { setParams(view === "transactions" ? { view: "transactions" } : {}); }
  function switchView(nextView) { setSelectedLearner(null); update({ view: nextView === "transactions" ? "transactions" : "", page: "" }); }
  const result = dateDisplay.showResults ? current.result : null; const hasFilters = ["search", "status", "gateway", "dateFrom", "dateTo"].some(name => filters[name]);
  const unit = view === "learners" ? "học viên" : "giao dịch";
  return <div className="admin-page"><div className="admin-page-heading"><div><h1>Thanh toán</h1><p>Trạng thái theo dữ liệu giao dịch đã lưu.</p></div><div className="admin-count-card"><strong>{result?.totalItems ?? "—"}</strong><span>{unit}</span></div></div>
    <div className="admin-payment-view-tabs" role="tablist" aria-label="Chế độ xem thanh toán">
      <button type="button" role="tab" aria-selected={view === "learners"} className={view === "learners" ? "is-active" : ""} onClick={() => switchView("learners")}>Theo học viên</button>
      <button type="button" role="tab" aria-selected={view === "transactions"} className={view === "transactions" ? "is-active" : ""} onClick={() => switchView("transactions")}>Theo giao dịch</button>
    </div>
    {result && <Summary data={result.summary}/>}<section className="admin-panel"><Filters key={filters.search} filters={filters} gateways={current.result?.gateways || []} update={update} clear={clearFilters}
      onDraftStateChange={status => setDateDraft({ key: appliedDateKey, status })}/>
      {!dateDisplay.showResults ? <AdminMessage title={dateDisplay.title} detail={dateDisplay.detail}/> : loading ? <Loading/> : current.error ? <AdminMessage title="Không tải được dữ liệu thanh toán" detail={current.error} action={<button className="admin-button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>}/> : result?.items?.length ? <>
        {view === "learners" ? <LearnerSummaryTable items={result.items} selectedLearner={selectedLearner}
          filters={filters} token={token} onSelect={setSelectedLearner}/> : <TransactionTable items={result.items}/>}
        <Pagination result={result} unit={unit} onPage={page => { setSelectedLearner(null); update({ page: page === 1 ? "" : page }); }}/>
      </> : <AdminMessage title={view === "learners" ? "Không tìm thấy học viên có giao dịch" : "Không tìm thấy giao dịch"} detail={hasFilters ? "Hãy thử điều chỉnh bộ lọc." : "Chưa có giao dịch thanh toán trong hệ thống."} action={hasFilters && <button className="admin-button admin-button-secondary" onClick={clearFilters}>Xóa bộ lọc</button>}/>}</section>
  </div>;
}

function LearnerSummaryTable({ items, selectedLearner, filters, token, onSelect }) {
  return <><div className="admin-payment-table-note">Số tiền “Đã thanh toán” chỉ tính các giao dịch thành công.</div>
    <div className="admin-table-wrap"><table className="admin-table admin-payment-learner-table"><thead><tr><th>Học viên</th><th>Số giao dịch</th><th>Kết quả</th><th>Đã thanh toán</th><th>Gần nhất</th><th></th></tr></thead><tbody>{items.map(item => {
      const expanded = selectedLearner?.learnerId === item.learnerId;
      const regionId = paymentHistoryRegionId(item.learnerId);
      return <Fragment key={item.learnerId}><tr className={expanded ? "is-expanded" : ""}>
        <td><strong className="admin-cell-main">{item.learnerName}</strong><small>{item.learnerEmail}</small><small>Học viên #{item.learnerId}</small></td>
        <td><strong className="admin-payment-total-count">{item.totalTransactions}</strong></td>
        <td><div className="admin-payment-result-counts"><span className="is-success"><b>{item.successfulTransactions}</b> Thành công</span>
          <span className="is-failed"><b>{item.failedTransactions}</b> Thất bại</span><span className="is-pending"><b>{item.pendingTransactions}</b> Đang chờ</span></div></td>
        <td><strong className="admin-payment-paid-amount">{formatMoney(item.successfulAmount)}</strong></td><td>{formatDate(item.latestTransactionAt, true)}</td>
        <td><button className="admin-history-button" type="button" aria-expanded={expanded} aria-controls={regionId}
          onClick={() => onSelect(nextPaymentHistoryLearner(selectedLearner, item))}>{expanded ? "Đóng lịch sử" : "Xem lịch sử giao dịch"}</button></td>
      </tr>{expanded && <tr className="admin-payment-history-row"><td colSpan="6">
        <LearnerPaymentHistory key={`${item.learnerId}|${JSON.stringify(filters)}`} regionId={regionId}
          learner={item} filters={filters} token={token} onClose={() => onSelect(null)}/>
      </td></tr>}</Fragment>;
    })}</tbody></table></div></>;
}

function TransactionTable({ items }) {
  return <div className="admin-table-wrap"><table className="admin-table admin-payment-table"><thead><tr><th>Giao dịch</th><th>Học viên</th><th>Đăng ký</th><th>Số tiền</th><th>Cổng</th><th>Trạng thái</th><th>Thời gian</th><th></th></tr></thead><tbody>{items.map(item => <tr key={item.paymentId}>
    <td><strong className="admin-cell-main">#{item.paymentId}</strong><small title={item.gatewayReference}>{item.gatewayReference}</small></td>
    <td><strong className="admin-cell-main">{item.learnerName}</strong><small>{item.learnerEmail}</small><small>ID #{item.learnerId}</small></td>
    <td><strong>ID #{item.enrollmentId}</strong><small>{purchaseTypeLabel(item.purchaseType)}</small><small>{offeringLabel(item)}</small></td>
    <td><strong>{formatMoney(item.paymentAmount)}</strong>{item.paymentAmount !== item.registeredAmount && <small>Đăng ký: {formatMoney(item.registeredAmount)}</small>}</td>
    <td><strong>{item.gateway.name}</strong><small>{item.gateway.code}</small></td>
    <td><span className={`admin-status is-${statusTone(item.status)}`}>{statusLabel(item.status)}</span></td>
    <td>{formatDate(item.createdAt, true)}<small>Hoàn tất: {formatDate(item.paidAt, true)}</small></td><td><Link className="admin-row-link" to={`/admin/payments/${item.paymentId}`}>Xem →</Link></td>
  </tr>)}</tbody></table></div>;
}

function LearnerPaymentHistory({ learner, filters, token, onClose, regionId }) {
  const [page, setPage] = useState(1); const [revision, setRevision] = useState(0);
  const scopedFilters = useMemo(() => ({ ...filters, page, pageSize: 10 }), [filters, page]);
  const key = `${learner.learnerId}|${JSON.stringify(scopedFilters)}|${revision}`;
  const [request, setRequest] = useState({ key: null, result: null, error: "" });
  const state = paymentHistoryState(request, key);
  useEffect(() => { let active = true; adminPaymentApi.learnerHistory(learner.learnerId, scopedFilters, token)
    .then(result => { if (active) setRequest({ key, result, error: "" }); })
    .catch(error => { if (active) setRequest({ key, result: null, error: error.data?.message || "Không thể tải lịch sử thanh toán của học viên." }); });
    return () => { active = false; }; }, [key, learner.learnerId, scopedFilters, token]);
  return <section id={regionId} className="admin-learner-payment-history" aria-labelledby={`${regionId}-title`}>
    <div className="admin-section-heading"><div><h2 id={`${regionId}-title`}>Lịch sử giao dịch</h2><p>{learner.learnerName} · {learner.learnerEmail}</p></div><button className="admin-row-action" type="button" onClick={onClose}>Đóng</button></div>
    {state.kind === "loading" ? <HistoryLoading/> : state.kind === "error" ? <AdminMessage title="Không tải được lịch sử" detail={state.error} action={<button className="admin-button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>}/> : state.kind === "success" ? <><TransactionTable items={state.result.items}/><Pagination result={state.result} unit="giao dịch" onPage={setPage}/></> : <AdminMessage title="Không có giao dịch phù hợp" detail="Học viên không có giao dịch trong phạm vi bộ lọc hiện tại."/>}
  </section>;
}

function HistoryLoading() {
  return <div className="admin-payment-history-loading" role="status"><span className="admin-spinner" aria-hidden="true"/><span>Đang tải lịch sử giao dịch…</span></div>;
}

function offeringLabel(item) {
  if (item.packageName) return item.packageName;
  return item.courseNames?.length ? item.courseNames.join(", ") : "Mua khóa học trực tiếp";
}

function Filters({ filters, gateways, update, clear, onDraftStateChange }) { const [search, setSearch] = useState(filters.search); const dateBasis = filters.status === "THANH_CONG" ? "hoàn tất" : "tạo"; return <div className="admin-enrollment-filters"><form className="admin-search-form" onSubmit={event => { event.preventDefault(); update({ search: search.trim(), page: "" }); }}><label className="admin-search"><span aria-hidden="true">⌕</span><input value={search} maxLength={128} onChange={event => setSearch(event.target.value)} placeholder="ID giao dịch, học viên, đăng ký hoặc mã tham chiếu"/></label><button className="admin-button">Tìm kiếm</button></form><div className="admin-filter-grid admin-payment-filter-grid">
    <Filter label="Trạng thái"><select value={filters.status} onChange={event => update({ status: event.target.value, page: "" })}><option value="">Tất cả</option>{Object.entries(paymentStatuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></Filter>
    <Filter label="Cổng thanh toán"><select value={filters.gateway} onChange={event => update({ gateway: event.target.value, page: "" })}><option value="">Tất cả</option>{filters.gateway && !gateways.some(item => item.code === filters.gateway) && <option value={filters.gateway}>{filters.gateway}</option>}{gateways.map(item => <option key={item.gatewayId} value={item.code}>{item.name}</option>)}</select></Filter>
    <DateRangeFields key={`${filters.dateFrom}|${filters.dateTo}`} from={filters.dateFrom} to={filters.dateTo}
      fromLabel={`Từ ngày ${dateBasis}`} toLabel={`Đến ngày ${dateBasis}`}
      onApply={values => update({ dateFrom: values.from, dateTo: values.to, page: "" })}
      onClear={clear} onDraftStateChange={onDraftStateChange}/>
  </div></div>; }
function Filter({ label, children }) { return <label className="admin-filter admin-filter-field"><span>{label}</span>{children}</label>; }
function Summary({ data }) { return <div className="admin-stat-grid admin-enrollment-stats"><Stat label="Tổng giao dịch" value={data.total}/><Stat label="Chờ xử lý" value={data.pending}/><Stat label="Thành công" value={data.successful}/><Stat label="Thất bại" value={data.failed}/><Stat label="Đã xác nhận" value={formatMoney(data.successfulAmount)}/></div>; }
function Stat({ label, value }) { return <div className="admin-stat"><span>{label}</span><strong>{value}</strong></div>; }
function Loading() { return <div className="admin-loading"><span className="admin-spinner"/><strong>Đang tải giao dịch</strong><span>Đang tổng hợp trạng thái thanh toán.</span></div>; }
function Pagination({ result, onPage, unit = "giao dịch" }) { const { page, pageSize, totalItems, totalPages } = result; if (totalPages <= 1) return <div className="admin-pagination admin-pagination-single">Hiển thị {totalItems} {unit}</div>; return <div className="admin-pagination"><span>Hiển thị {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, totalItems)} trong {totalItems} {unit}</span><div><button disabled={page <= 1} onClick={() => onPage(page - 1)}>← Trước</button><strong>Trang {page}/{totalPages}</strong><button disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Sau →</button></div></div>; }
