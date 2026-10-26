import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminCourseApi } from "../../services/adminCourseApi";
import AdminCourseForm from "./AdminCourseForm";

export default function AdminCourseCreatePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState({ submitting: false, error: "", fieldErrors: {} });

  async function submit(values, imageFile) {
    setState({ submitting: true, error: "", fieldErrors: {} });
    try {
      const course = await adminCourseApi.create(values, token, imageFile);
      navigate(`/admin/courses/${course.courseId}`, { replace: true,
        state: { notice: "Đã tạo khóa học thành công." } });
    } catch (error) {
      setState({ submitting: false,
        error: error.data?.message || "Không thể tạo khóa học. Vui lòng thử lại.",
        fieldErrors: error.data?.errors || {} });
    }
  }

  return <div className="admin-page admin-form-page">
    <Link className="admin-back-link" to="/admin/courses">← Danh sách khóa học</Link>
    <div className="admin-page-heading"><div><p className="admin-eyebrow">Khóa học mới</p><h1>Thêm khóa học</h1>
      <p>Tạo thông tin nền cho một giai đoạn TOEIC chưa được sử dụng.</p></div></div>
    <section className="admin-panel admin-form-panel"><AdminCourseForm mode="create"
      submitting={state.submitting} serverError={state.error} serverErrors={state.fieldErrors} onSubmit={submit} /></section>
  </div>;
}
