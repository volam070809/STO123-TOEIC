import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminCourseApi } from "../../services/adminCourseApi";
import { AdminMessage } from "./AdminLearnersPage";
import AdminCourseForm from "./AdminCourseForm";

export default function AdminCourseEditPage() {
  const { courseId } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const requestKey = `${token}|${courseId}`;
  const [request, setRequest] = useState({ key: null, course: null, error: "" });
  const [save, setSave] = useState({ submitting: false, error: "", fieldErrors: {} });
  const current = request.key === requestKey ? request : { course: null, error: "" };

  useEffect(() => {
    let active = true;
    adminCourseApi.detail(courseId, token)
      .then(course => { if (active) setRequest({ key: requestKey, course, error: "" }); })
      .catch(error => { if (active) setRequest({ key: requestKey, course: null,
        error: error.status === 404 ? "Không tìm thấy khóa học này." : "Không thể tải thông tin khóa học." }); });
    return () => { active = false; };
  }, [courseId, token, requestKey]);

  async function submit(values, imageFile) {
    setSave({ submitting: true, error: "", fieldErrors: {} });
    try {
      await adminCourseApi.update(courseId, values, token, imageFile);
      navigate(`/admin/courses/${courseId}`, { replace: true,
        state: { notice: "Thông tin khóa học đã được cập nhật." } });
    } catch (error) {
      setSave({ submitting: false, error: error.data?.message || "Không thể cập nhật khóa học.",
        fieldErrors: error.data?.errors || {} });
    }
  }

  if (request.key !== requestKey) return <div className="admin-page"><div className="admin-panel admin-loading" role="status">
    <span className="admin-spinner" aria-hidden="true" /><strong>Đang tải thông tin khóa học</strong></div></div>;
  if (current.error) return <div className="admin-page"><Link className="admin-back-link" to="/admin/courses">← Danh sách khóa học</Link>
    <section className="admin-panel"><AdminMessage title="Không thể sửa khóa học" detail={current.error} /></section></div>;

  return <div className="admin-page admin-form-page">
    <Link className="admin-back-link" to={`/admin/courses/${courseId}`}>← Chi tiết khóa học</Link>
    <div className="admin-page-heading"><div><p className="admin-eyebrow">ID #{courseId}</p><h1>Sửa khóa học</h1>
      <p>Cập nhật thông tin khóa học mà không thay đổi unit, bài học hoặc gói học liên kết.</p></div></div>
    <section className="admin-panel admin-form-panel"><AdminCourseForm mode="edit" initial={current.course}
      submitting={save.submitting} serverError={save.error} serverErrors={save.fieldErrors} onSubmit={submit} /></section>
  </div>;
}
