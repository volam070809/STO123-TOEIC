import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminAnalyticsApi } from "../../services/adminAnalyticsApi";
import { formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";
import { purchaseTypeLabel } from "./adminEnrollmentFormat";
import { AdminMessage } from "./AdminLearnersPage";
import { Bars, DateRangeFilter, Kpi, LoadingAnalytics, TrendChart } from "./AdminAnalyticsUi";
import { rangeLabel } from "./adminAnalyticsFormat";
import { dashboardListPath } from "./adminDashboardLinks";
import { ADMIN_DATE_FILTER_STATUS, adminDateDisplayState, analyticsAppliedPeriod, validateAdminDateRange } from "./adminDateRange";

export default function AdminDashboardPage() {
  const { token } = useAuth(); const [params, setParams] = useSearchParams(); const [revision, setRevision] = useState(0);
  const filters = useMemo(() => { const period = analyticsAppliedPeriod(params.get("dateFrom"), params.get("dateTo")); return { dateFrom: period.from, dateTo: period.to }; }, [params]);
  const dateError = validateAdminDateRange({ from: filters.dateFrom, to: filters.dateTo }, 366).error;
  const appliedDateKey = `${filters.dateFrom}|${filters.dateTo}`;
  const [dateDraft, setDateDraft] = useState({ key: appliedDateKey, status: ADMIN_DATE_FILTER_STATUS.VALID_APPLIED });
  const draftStatus = dateDraft.key === appliedDateKey ? dateDraft.status : ADMIN_DATE_FILTER_STATUS.VALID_APPLIED;
  const dateDisplay = adminDateDisplayState(draftStatus, dateError);
  const key = `${token}|${filters.dateFrom}|${filters.dateTo}|${revision}`; const [request, setRequest] = useState({ key: null, data: null, error: "" }); const current = request.key === key ? request : { data: null, error: "" }; const loading = dateDisplay.showResults && !dateError && request.key !== key;
  useEffect(() => { let active = true; if (dateError) return () => { active = false; }; adminAnalyticsApi.dashboard(filters, token).then(data => { if (active) setRequest({ key, data, error: "" }); }).catch(error => { if (active) setRequest({ key, data: null, error: error.data?.message || "Không thể tải trang tổng quan." }); }); return () => { active = false; }; }, [key, token, filters, dateError]);
  function apply(values) { const next = new URLSearchParams(); if (values.from) next.set("dateFrom", values.from); if (values.to) next.set("dateTo", values.to); setParams(next); }
  function reset() { setParams(new URLSearchParams()); }
  const data = dateDisplay.showResults ? current.data : null; return <div className="admin-page"><div className="admin-page-heading"><div><h1>Tổng quan</h1><p>{data ? rangeLabel(data.range) : dateDisplay.showResults ? "Đang tải số liệu" : "Khoảng ngày trên URL chưa hợp lệ"}</p><small className="admin-dashboard-scope">Áp dụng cho toàn bộ trang Tổng quan</small></div><DateRangeFilter key={`${filters.dateFrom}|${filters.dateTo}`} from={filters.dateFrom} to={filters.dateTo} maxDays={366} onApply={apply} onReset={reset} onDraftStateChange={status => setDateDraft({ key: appliedDateKey, status })}/></div>
    {!dateDisplay.showResults ? <section className="admin-panel"><AdminMessage title={dateDisplay.title} detail={dateDisplay.detail}/></section> :
      loading ? <LoadingAnalytics/> : current.error ? <section className="admin-panel"><AdminMessage title="Không tải được tổng quan" detail={current.error} action={<button className="admin-button" onClick={() => setRevision(v => v + 1)}>Thử lại</button>}/></section> : <Dashboard data={data}/>}</div>;
}

function Dashboard({ data }) { const path = (target, filters) => dashboardListPath(target, data.range, filters); return <><div className="admin-kpi-grid"><Kpi label="Học viên mới trong kỳ" value={data.newLearners} note="Theo ngày tạo tài khoản" href={path("/admin/learners")} LinkComponent={Link}/><Kpi label="Đăng ký trong kỳ" value={data.enrollments} note="Theo ngày đăng ký" href={path("/admin/enrollments")} LinkComponent={Link}/><Kpi label="Doanh thu xác nhận trong kỳ" value={formatMoney(data.successfulRevenue)} note="Theo thời điểm thanh toán thành công" href={path("/admin/payments", { status: "THANH_CONG" })} LinkComponent={Link}/><Kpi label="Chờ xử lý trong kỳ" value={data.pendingPayments} note="Theo ngày tạo giao dịch" href={path("/admin/payments", { status: "CHO_XU_LY" })} LinkComponent={Link}/><Kpi label="Thất bại trong kỳ" value={data.failedPayments} note="Theo ngày tạo giao dịch" href={path("/admin/payments", { status: "THAT_BAI" })} LinkComponent={Link}/></div>
  <div className="admin-chart-grid"><TrendChart title="Học viên mới" data={data.learnerGrowth}/><TrendChart title="Doanh thu đã thanh toán" data={data.revenueTrend} valueKey="amount" money/></div>
  <div className="admin-chart-grid admin-chart-grid-wide"><Bars title="Đăng ký theo sản phẩm" items={data.offeringPerformance} value={item => item.count} href={item => path("/admin/enrollments", item.type === "PACKAGE" ? { packageId: item.id } : { courseId: item.id })} LinkComponent={Link}/><RecentPayments items={data.recentPayments} listPath={path("/admin/payments")}/></div><RecentEnrollments items={data.recentEnrollments} listPath={path("/admin/enrollments")}/></>; }
function RecentPayments({ items, listPath }) { return <section className="admin-chart-card"><div className="admin-section-heading"><div><h2>Thanh toán trong kỳ</h2></div><Link to={listPath} className="admin-row-link">Xem tất cả</Link></div>{items.length ? <div className="admin-compact-list">{items.map(item => <Link to={`/admin/payments/${item.paymentId}`} key={item.paymentId}><span><strong>#{item.paymentId} · {item.learnerName}</strong><small>{item.gateway} · {formatDate(item.createdAt, true)}</small></span><span><strong>{formatMoney(item.amount)}</strong><small className={`is-${statusTone(item.status)}`}>{statusLabel(item.status)}</small></span></Link>)}</div> : <p className="admin-chart-empty">Không có giao dịch được tạo trong kỳ.</p>}</section>; }
function RecentEnrollments({ items, listPath }) { return <section className="admin-panel admin-detail-section"><div className="admin-section-heading"><div><h2>Đăng ký trong kỳ</h2></div><Link to={listPath} className="admin-row-link">Xem tất cả</Link></div>{items.length ? <div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>Học viên</th><th>Loại mua</th><th>Giá</th><th>Trạng thái</th><th>Ngày đăng ký</th><th></th></tr></thead><tbody>{items.map(item => <tr key={item.enrollmentId}><td><Link to={`/admin/learners/${item.learnerId}`}>{item.learnerName}</Link></td><td>{purchaseTypeLabel(item.purchaseType)}<small>{item.offeringName}</small></td><td>{formatMoney(item.price)}</td><td><span className={`admin-status is-${statusTone(item.status)}`}>{statusLabel(item.status)}</span></td><td>{formatDate(item.registeredAt, true)}</td><td><Link className="admin-row-link" to={`/admin/enrollments/${item.enrollmentId}`}>Xem</Link></td></tr>)}</tbody></table></div> : <p className="admin-chart-empty">Không có đăng ký trong kỳ.</p>}</section>; }

export { Dashboard };
