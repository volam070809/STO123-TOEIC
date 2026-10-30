import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminCourseApi } from "../../services/adminCourseApi";
import { AdminMessage } from "./AdminLearnersPage";
import { formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";

export default function AdminCourseDetailPage() {
  const { courseId } = useParams();
  const { token, user } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [revision, setRevision] = useState(0);
  const requestKey = `${token}|${courseId}|${revision}`;
  const [request, setRequest] = useState({ key: null, course: null, error: "" });
  const [notice, setNotice] = useState(location.state?.notice || "");
  const [action, setAction] = useState(null);
  const [mutation, setMutation] = useState({ busy: false, error: "", blockers: [] });
  const current = request.key === requestKey ? request : { course: null, error: "" };
  const canManage = user?.vaiTro === "ADMIN_QUAN_LY";

  useEffect(() => {
    let active = true;
    adminCourseApi.detail(courseId, token)
      .then(course => { if (active) setRequest({ key: requestKey, course, error: "" }); })
      .catch(error => { if (active) setRequest({ key: requestKey, course: null,
        error: error.status === 404 ? "Không tìm thấy khóa học này." : error.timeout ?
          "Máy chủ phản hồi quá lâu. Hãy chọn Thử lại." : "Không thể tải chi tiết khóa học." }); });
    return () => { active = false; };
  }, [courseId, token, requestKey, revision]);

  function openAction(kind) {
    setMutation({ busy: false, error: "", blockers: [] });
    setAction(kind);
  }

  async function confirmAction() {
    setMutation({ busy: true, error: "", blockers: [] });
    try {
      if (action === "delete") {
        await adminCourseApi.remove(courseId, token);
        navigate("/admin/courses", { replace: true, state: { notice: "Đã xóa khóa học." } });
        return;
      }
      const status = action === "close" ? "DA_DONG" : "DANG_MO";
      await adminCourseApi.changeStatus(courseId, status, token);
      setNotice(action === "close" ? "Đã đóng khóa học." : "Đã mở lại khóa học.");
      setAction(null);
      setMutation({ busy: false, error: "", blockers: [] });
      setRevision(value => value + 1);
    } catch (error) {
      setMutation({ busy: false, error: error.data?.message || "Không thể thực hiện thao tác. Vui lòng thử lại.",
        blockers: error.data?.blockers || [] });
    }
  }

  if (request.key !== requestKey) return <div className="admin-page"><div className="admin-panel admin-loading" role="status">
    <span className="admin-spinner" aria-hidden="true" /><strong>Đang tải khóa học</strong></div></div>;
  if (current.error) return <div className="admin-page"><Link className="admin-back-link" to="/admin/courses">← Danh sách khóa học</Link>
    <section className="admin-panel"><AdminMessage title="Không tải được khóa học" detail={current.error}
      action={<button className="admin-button" type="button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>} /></section></div>;

  const course = current.course;
  const lessonCount = course.units.reduce((total, unit) => total + unit.lessons.length, 0);
  const stepCount = course.units.reduce((total, unit) => total + unit.lessons.reduce((sum, lesson) => sum + lesson.stepCount, 0), 0);
  return <div className="admin-page">
    <Link className="admin-back-link" to="/admin/courses">← Danh sách khóa học</Link>
    {notice && <div className="admin-feedback is-success" role="status">{notice}</div>}
    <section className="admin-profile-card admin-course-profile">
      <span className="admin-profile-avatar">G{course.stage}</span>
      <div className="admin-profile-title"><div><p className="admin-eyebrow">Khóa học · ID #{course.courseId}</p><h1>{course.name}</h1></div>
        <div className="admin-profile-controls"><span className={`admin-status is-${statusTone(course.status)}`}>{statusLabel(course.status)}</span>
          {canManage && <div className="admin-action-buttons"><Link className="admin-button admin-button-secondary" to={`/admin/courses/${courseId}/edit`}>Sửa</Link>
            {course.status === "DANG_MO" ? <button className="admin-button admin-button-warning" type="button" onClick={() => openAction("close")}>Đóng</button> :
              <button className="admin-button" type="button" onClick={() => openAction("open")}>Mở lại</button>}
            <button className="admin-button admin-button-danger" type="button" onClick={() => openAction("delete")}>Xóa</button></div>}</div></div>
      <dl className="admin-profile-meta"><div><dt>Giai đoạn</dt><dd>{course.stage}</dd></div>
        <div><dt>Điểm mục tiêu tối đa</dt><dd>{course.maxTargetScore}</dd></div>
        <div><dt>Giá bán lẻ</dt><dd>{course.retailPrice == null ? "Chưa đặt giá" : formatMoney(course.retailPrice)}</dd></div>
        <div><dt>Ngày tạo</dt><dd>{formatDate(course.createdAt, true)}</dd></div>
        <div><dt>Admin nội dung</dt><dd>{course.contentAdminName || "Chưa gán"}</dd></div>
        <div><dt>Ảnh đại diện</dt><dd>{course.coverImageUrl || "Chưa có"}</dd></div></dl>
      {course.description && <p className="admin-course-description">{course.description}</p>}
    </section>

    <nav className="admin-course-workflow" aria-label="Quy trình quản lý khóa học"><div><span>01</span><strong>Tổng quan</strong><small>Thông tin và trạng thái</small></div><Link to={`/admin/courses/${courseId}/content`}><span>02</span><strong>Biên soạn Studio →</strong><small>Unit, Lesson, hoạt động và tài nguyên</small></Link><Link to={`/courses/${courseId}`}><span>03</span><strong>Xem trang khóa học →</strong><small>Kiểm tra hành trình học viên</small></Link></nav>

    <div className="admin-stat-grid"><Stat label="Đơn vị" value={course.units.length} note="unit trong khóa học" />
      <Stat label="Bài học" value={lessonCount} note="lesson đã cấu hình" />
      <Stat label="Bước lộ trình" value={stepCount} note="nội dung học tập" />
      <Stat label="Đăng ký trực tiếp" value={course.directEnrollmentCount} note="không qua gói học" /></div>

    <section className="admin-panel admin-detail-section">
      <div className="admin-section-heading"><div><h2>Cấu trúc khóa học</h2><p>Unit, bài học và số bước lộ trình hiện có</p></div></div>
      <Link className="admin-button" to={`/admin/courses/${courseId}/content`}>Biên soạn lộ trình</Link>
      {course.units.length ? <div className="admin-course-outline">{course.units.map(unit => <article key={unit.unitId} className="admin-course-unit">
        <div><span>UNIT {unit.order}</span><h3>{unit.name}</h3>{unit.description && <p>{unit.description}</p>}</div>
        {unit.lessons.length ? <ol>{unit.lessons.map(lesson => <li key={lesson.lessonId}>
          <span>{lesson.order}</span><div><strong>{lesson.name}</strong>{lesson.description && <small>{lesson.description}</small>}</div>
          <em>{lesson.stepCount} bước</em></li>)}</ol> : <p className="admin-inline-empty">Unit này chưa có bài học.</p>}
      </article>)}</div> : <AdminMessage title="Chưa có cấu trúc khóa học" detail="Khóa học chưa có unit hoặc bài học liên kết." />}
    </section>

    <section className="admin-panel admin-detail-section">
      <div className="admin-section-heading"><div><h2>Gói học liên kết</h2><p>Chỉ hiển thị; quan hệ thương mại không được sửa tại đây</p></div></div>
      {course.packages.length ? <div className="admin-table-wrap"><table className="admin-table">
        <thead><tr><th>Gói học</th><th>Giá</th><th>Thời hạn</th><th>Trạng thái</th><th>Đăng ký</th></tr></thead>
        <tbody>{course.packages.map(item => <tr key={item.packageId}><td><strong className="admin-cell-main">{item.name}</strong><small>ID #{item.packageId}</small></td>
          <td>{formatMoney(item.price)}</td><td>{item.validDays} ngày</td><td><span className={`admin-status is-${statusTone(item.status)}`}>{statusLabel(item.status)}</span></td>
          <td>{item.enrollmentCount}</td></tr>)}</tbody></table></div> :
        <AdminMessage title="Chưa liên kết gói học" detail="Khóa học này chưa thuộc gói học thương mại nào." />}
    </section>
    {action && <CourseConfirmDialog action={action} course={course} mutation={mutation}
      onCancel={() => !mutation.busy && setAction(null)} onConfirm={confirmAction} />}
  </div>;
}

function Stat({ label, value, note }) {
  return <article className="admin-stat"><span>{label}</span><strong>{value}</strong><small>{note}</small></article>;
}

function CourseConfirmDialog({ action, course, mutation, onCancel, onConfirm }) {
  const deleting = action === "delete";
  const closing = action === "close";
  const title = deleting ? "Xóa khóa học?" : closing ? "Đóng khóa học?" : "Mở lại khóa học?";
  const detail = deleting ? "Chỉ khóa học hoàn toàn chưa có unit, gói học hoặc đăng ký trực tiếp mới được xóa. Không dữ liệu liên quan nào bị xóa cascade." :
    closing ? "Khóa học sẽ ngừng xuất hiện trong danh sách mua và nội dung dành cho học viên. Dữ liệu hiện có được giữ nguyên." :
      "Khóa học sẽ xuất hiện lại trong luồng học viên nếu giá và các điều kiện mua hiện có hợp lệ.";
  return <div className="admin-modal-backdrop" role="presentation" onMouseDown={event => {
    if (event.target === event.currentTarget) onCancel();
  }}><section className="admin-modal" role="dialog" aria-modal="true" aria-labelledby="course-confirm-title">
    <span className={`admin-modal-icon ${deleting ? "is-danger" : ""}`}>{deleting ? "!" : "?"}</span>
    <h2 id="course-confirm-title">{title}</h2><p><strong>{course.name}</strong> · Giai đoạn {course.stage}</p><p>{detail}</p>
    {mutation.error && <div className="admin-feedback is-error" role="alert">{mutation.error}
      {mutation.blockers.length > 0 && <ul>{mutation.blockers.map(item => <li key={item}>{item}</li>)}</ul>}</div>}
    <div className="admin-modal-actions"><button className="admin-button admin-button-secondary" type="button" disabled={mutation.busy} onClick={onCancel}>Hủy</button>
      <button className={`admin-button ${deleting ? "admin-button-danger" : closing ? "admin-button-warning" : ""}`}
        type="button" disabled={mutation.busy} onClick={onConfirm}>{mutation.busy ? "Đang xử lý..." : deleting ? "Xác nhận xóa" : closing ? "Xác nhận đóng" : "Xác nhận mở"}</button></div>
  </section></div>;
}
