import { useEffect, useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminCourseApi } from "../../services/adminCourseApi";
import { AdminMessage } from "./AdminLearnersPage";
import { courseStatuses, formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";

const PAGE_SIZE = 20;

export default function AdminCoursesPage() {
  const { token, user } = useAuth();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const search = params.get("search")?.trim() ?? "";
  const status = params.get("status") ?? "";
  const stage = params.get("stage") ?? "";
  const requestedPage = Number(params.get("page"));
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const [revision, setRevision] = useState(0);
  const requestKey = `${token}|${search}|${status}|${stage}|${page}|${revision}`;
  const [request, setRequest] = useState({ key: null, result: null, error: "" });
  const current = request.key === requestKey ? request : { result: null, error: "" };
  const loading = request.key !== requestKey;
  const canManage = user?.vaiTro === "ADMIN_QUAN_LY";

  useEffect(() => {
    let active = true;
    adminCourseApi.list({ search, status, stage, page, pageSize: PAGE_SIZE }, token)
      .then(result => { if (active) setRequest({ key: requestKey, result, error: "" }); })
      .catch(error => { if (active) setRequest({ key: requestKey, result: null,
        error: error.timeout ? "Máy chủ phản hồi quá lâu. Hãy chọn Thử lại." :
          "Không thể tải danh sách khóa học. Vui lòng thử lại." }); });
    return () => { active = false; };
  }, [token, search, status, stage, page, requestKey, revision]);

  function updateParams(changes) {
    const next = new URLSearchParams(params);
    Object.entries(changes).forEach(([key, value]) => value ? next.set(key, String(value)) : next.delete(key));
    setParams(next);
  }

  const result = current.result;
  return <div className="admin-page">
    <div className="admin-page-heading">
      <div><h1>Khóa học</h1>
        <p>Quản lý thông tin học thuật, giá bán lẻ và trạng thái cung cấp khóa học.</p></div>
      <div className="admin-heading-actions"><div className="admin-count-card"><strong>{result?.totalItems ?? "—"}</strong><span>khóa học</span></div>
        {canManage && <Link className="admin-button" to="/admin/courses/new">+ Thêm khóa học</Link>}</div>
    </div>

    {location.state?.notice && <div className="admin-feedback is-success" role="status">{location.state.notice}</div>}
    {!canManage && <div className="admin-feedback is-info">Admin Nội dung có quyền xem cấu trúc khóa học. Các thao tác thay đổi khóa học chỉ dành cho Admin Quản lý.</div>}

    <section className="admin-panel">
      <div className="admin-toolbar admin-list-toolbar admin-course-toolbar">
        <CourseSearch key={search} search={search} onSearch={value => updateParams({ search: value, page: "" })} />
        <label className="admin-filter"><span>Giai đoạn</span><select value={stage}
          onChange={event => updateParams({ stage: event.target.value, page: "" })}>
          <option value="">Tất cả</option><option value="1">Giai đoạn 1</option>
          <option value="2">Giai đoạn 2</option><option value="3">Giai đoạn 3</option>
        </select></label>
        <label className="admin-filter"><span>Trạng thái</span><select value={status}
          onChange={event => updateParams({ status: event.target.value, page: "" })}>
          <option value="">Tất cả</option>
          {Object.entries(courseStatuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
      </div>

      {loading ? <CourseLoading /> : current.error ? <AdminMessage title="Không tải được dữ liệu" detail={current.error}
        action={<button className="admin-button" type="button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>} /> :
        result?.items?.length ? <>
          <div className="admin-course-gallery">{result.items.map(course => <article className="admin-course-tile" key={course.courseId}>
            <div className="admin-course-tile-top"><span className="admin-course-stage">G{course.stage}</span><span className={`admin-status is-${statusTone(course.status)}`}>{statusLabel(course.status)}</span></div>
            <div><small>KHÓA HỌC #{course.courseId} · {formatDate(course.createdAt)}</small><h2>{course.name}</h2><p>{course.contentAdminName ? `Phụ trách nội dung: ${course.contentAdminName}` : "Chưa gán người phụ trách nội dung"}</p></div>
            <dl><div><dt>Đơn vị</dt><dd>{course.unitCount}</dd></div><div><dt>Bài học</dt><dd>{course.lessonCount}</dd></div><div><dt>Hoạt động</dt><dd>{course.stepCount}</dd></div></dl>
            <p className="admin-course-tile-meta">Giai đoạn {course.stage} · Mục tiêu {course.maxTargetScore} · {course.retailPrice == null ? "Chưa đặt giá" : formatMoney(course.retailPrice)}</p>
            <div className="admin-course-tile-actions"><Link to={`/admin/courses/${course.courseId}`}>Tổng quan</Link><Link className="admin-button" to={`/admin/courses/${course.courseId}/content`}>Mở Studio →</Link></div>
          </article>)}</div>
          <CoursePagination {...result} onPage={next => updateParams({ page: next === 1 ? "" : next })} />
        </> : <AdminMessage title="Không tìm thấy khóa học"
          detail={search || status || stage ? "Hãy thử từ khóa hoặc bộ lọc khác." : "Chưa có khóa học trong hệ thống."}
          action={(search || status || stage) && <button className="admin-button admin-button-secondary" type="button" onClick={() => setParams({})}>Xóa bộ lọc</button>} />}
    </section>
  </div>;
}

function CourseSearch({ search, onSearch }) {
  const [value, setValue] = useState(search);
  return <form className="admin-search-form" onSubmit={event => { event.preventDefault(); onSearch(value.trim()); }}>
    <label className="admin-search"><span className="sr-only">Tìm khóa học</span><span aria-hidden="true">⌕</span>
      <input value={value} maxLength={128} onChange={event => setValue(event.target.value)} placeholder="Tìm theo tên hoặc mô tả" />
      {value && <button type="button" aria-label="Xóa từ khóa" onClick={() => setValue("")}>×</button>}
    </label><button className="admin-button" type="submit">Tìm kiếm</button>
  </form>;
}

function CourseLoading() {
  return <div className="admin-loading" role="status"><span className="admin-spinner" aria-hidden="true" />
    <strong>Đang tải danh sách khóa học</strong><span>Dữ liệu sẽ xuất hiện trong giây lát.</span></div>;
}

function CoursePagination({ page, totalPages, totalItems, pageSize, onPage }) {
  if (totalPages <= 1) return <div className="admin-pagination admin-pagination-single">Hiển thị {totalItems} khóa học</div>;
  const start = (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, totalItems);
  return <div className="admin-pagination"><span>Hiển thị {start}–{end} trong {totalItems}</span><div>
    <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>← Trước</button>
    <strong>Trang {page}/{totalPages}</strong>
    <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Sau →</button>
  </div></div>;
}
