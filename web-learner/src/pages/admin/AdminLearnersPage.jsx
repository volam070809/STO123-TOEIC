import { useEffect, useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminLearnerApi } from "../../services/adminLearnerApi";
import { formatDate, learnerStatuses, statusLabel, statusTone } from "./adminFormat";
import { DateRangeFields } from "./AdminDateRangeFields";
import { ADMIN_DATE_FILTER_STATUS, adminDateDisplayState, validateAdminDateRange } from "./adminDateRange";

const PAGE_SIZE = 20;

export default function AdminLearnersPage() {
  const { token } = useAuth();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const search = params.get("search")?.trim() ?? "";
  const status = params.get("status") ?? "";
  const dateFrom = params.get("dateFrom") ?? "";
  const dateTo = params.get("dateTo") ?? "";
  const dateError = validateAdminDateRange({ from: dateFrom, to: dateTo }).error;
  const appliedDateKey = `${dateFrom}|${dateTo}`;
  const [dateDraft, setDateDraft] = useState({ key: appliedDateKey, status: ADMIN_DATE_FILTER_STATUS.VALID_APPLIED });
  const draftStatus = dateDraft.key === appliedDateKey ? dateDraft.status : ADMIN_DATE_FILTER_STATUS.VALID_APPLIED;
  const dateDisplay = adminDateDisplayState(draftStatus, dateError);
  const requestedPage = Number(params.get("page"));
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const [revision, setRevision] = useState(0);
  const requestKey = `${token}|${search}|${status}|${dateFrom}|${dateTo}|${page}|${revision}`;
  const [request, setRequest] = useState({ key: null, result: null, error: "" });
  const current = request.key === requestKey ? request : { result: null, error: "" };
  const result = dateDisplay.showResults ? current.result : null;
  const error = current.error;
  const loading = dateDisplay.showResults && !dateError && request.key !== requestKey;

  useEffect(() => {
    let active = true;
    if (dateError) return () => { active = false; };
    adminLearnerApi.list({ search, status, dateFrom, dateTo, page, pageSize: PAGE_SIZE }, token)
      .then(data => { if (active) setRequest({ key: requestKey, result: data, error: "" }); })
      .catch(requestError => { if (active) setRequest({ key: requestKey, result: null,
        error: requestError.timeout
          ? "Kết nối dữ liệu lần đầu mất nhiều thời gian hơn dự kiến. Máy chủ hoặc kết nối cơ sở dữ liệu có thể chưa sẵn sàng. Hãy chọn Thử lại."
          : "Không thể tải danh sách học viên. Vui lòng thử lại." }); });
    return () => { active = false; };
  }, [token, search, status, dateFrom, dateTo, page, requestKey, revision, dateError]);

  function updateParams(changes) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([key, value]) => value ? next.set(key, String(value)) : next.delete(key));
    setParams(next);
  }

  return <div className="admin-page">
    <div className="admin-page-heading">
      <div><h1>Học viên</h1></div>
      <div className="admin-heading-actions"><div className="admin-count-card"><strong>{result?.totalItems ?? "—"}</strong><span>học viên</span></div>
        <Link className="admin-button" to="/admin/learners/new">+ Thêm học viên</Link></div>
    </div>

    {location.state?.notice && <div className="admin-feedback is-success" role="status">{location.state.notice}</div>}

    <section className="admin-panel">
      <div className="admin-enrollment-filters">
        <SearchForm key={search} search={search} onSearch={value => updateParams({ search: value, page: "" })} />
        <div className="admin-filter-grid"><label className="admin-filter admin-filter-field"><span>Trạng thái</span>
          <select value={status} onChange={event => updateParams({ status: event.target.value, page: "" })}>
            <option value="">Tất cả trạng thái</option>
            {Object.entries(learnerStatuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label><DateRangeFields key={`${dateFrom}|${dateTo}`} from={dateFrom} to={dateTo}
          fromLabel="Từ ngày tham gia" toLabel="Đến ngày tham gia"
          onApply={values => updateParams({ dateFrom: values.from, dateTo: values.to, page: "" })}
          onClear={() => setParams({})}
          onDraftStateChange={draftStatus => setDateDraft({ key: appliedDateKey, status: draftStatus })}/></div>
      </div>

      {!dateDisplay.showResults ? <AdminMessage title={dateDisplay.title} detail={dateDisplay.detail}/> :
        loading ? <ListSkeleton /> : error ? <AdminMessage title="Không tải được dữ liệu" detail={error}
        action={<button className="admin-button" type="button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>} /> :
        result?.items?.length ? <>
          <div className="admin-table-wrap"><table className="admin-table">
            <thead><tr><th>Học viên</th><th>Liên hệ</th><th>Trạng thái</th><th>Đăng ký</th><th>Lượt học</th><th>Làm bài gần nhất</th><th><span className="sr-only">Chi tiết</span></th></tr></thead>
            <tbody>{result.items.map(learner => <tr key={learner.learnerId}>
              <td><div className="admin-person"><span className="admin-person-avatar">{initials(learner.fullName)}</span>
                <span><strong>{learner.fullName}</strong><small>ID #{learner.learnerId} · Tham gia {formatDate(learner.createdAt)}</small></span></div></td>
              <td><strong className="admin-cell-main">{learner.email}</strong><small>{learner.phoneNumber || "Chưa có số điện thoại"}</small></td>
              <td><span className={`admin-status is-${statusTone(learner.status)}`}>{statusLabel(learner.status)}</span></td>
              <td><strong className="admin-metric">{learner.activeEnrollmentCount}</strong><small>đang dùng / {learner.enrollmentCount} tổng</small></td>
              <td><strong className="admin-metric">{learner.attemptCount}</strong><small>lượt làm bài</small></td>
              <td>{formatDate(learner.lastLearningAt, true)}</td>
              <td><Link className="admin-row-link" to={`/admin/learners/${learner.learnerId}`} aria-label={`Xem ${learner.fullName}`}>Xem <span aria-hidden="true">→</span></Link></td>
            </tr>)}</tbody>
          </table></div>
          <Pagination page={result.page} totalPages={result.totalPages} totalItems={result.totalItems}
            pageSize={result.pageSize} onPage={next => updateParams({ page: next === 1 ? "" : next })} />
        </> : <AdminMessage title="Không tìm thấy học viên"
          detail={search || status || dateFrom || dateTo ? "Hãy thử điều chỉnh bộ lọc." : "Chưa có tài khoản học viên trong hệ thống."}
          action={(search || status || dateFrom || dateTo) && <button className="admin-button admin-button-secondary" type="button"
            onClick={() => setParams({})}>Xóa bộ lọc</button>} />}
    </section>
  </div>;
}

function SearchForm({ search, onSearch }) {
  const [value, setValue] = useState(search);
  return <form className="admin-search-form" onSubmit={event => { event.preventDefault(); onSearch(value.trim()); }}>
    <label className="admin-search"><span className="sr-only">Tìm học viên</span><span aria-hidden="true">⌕</span>
      <input value={value} maxLength={100} onChange={event => setValue(event.target.value)}
        placeholder="Tìm theo tên, email hoặc số điện thoại" />
      {value && <button type="button" aria-label="Xóa từ khóa" onClick={() => setValue("")}>×</button>}
    </label>
    <button className="admin-button" type="submit">Tìm kiếm</button>
  </form>;
}

function initials(name) {
  return name?.split(/\s+/).filter(Boolean).slice(-2).map(part => part[0]).join("").toUpperCase() || "HV";
}

function ListSkeleton() {
  return <div className="admin-loading" role="status"><span className="admin-spinner" aria-hidden="true" />
    <strong>Đang tải danh sách học viên</strong><span>Dữ liệu sẽ xuất hiện trong giây lát.</span></div>;
}

export function AdminMessage({ title, detail, action }) {
  return <div className="admin-empty" role="status"><span className="admin-empty-icon" aria-hidden="true">i</span>
    <h2>{title}</h2><p>{detail}</p>{action}</div>;
}

function Pagination({ page, totalPages, totalItems, pageSize, onPage }) {
  if (totalPages <= 1) return <div className="admin-pagination admin-pagination-single">Hiển thị {totalItems} học viên</div>;
  const start = (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, totalItems);
  return <div className="admin-pagination"><span>Hiển thị {start}–{end} trong {totalItems}</span><div>
    <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>← Trước</button>
    <strong>Trang {page}/{totalPages}</strong>
    <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Sau →</button>
  </div></div>;
}
