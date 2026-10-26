import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminLearnerApi } from "../../services/adminLearnerApi";
import AdminLearnerForm from "./AdminLearnerForm";

export default function AdminLearnerCreatePage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState({ submitting: false, error: "", fieldErrors: {} });

  async function submit(values) {
    setState({ submitting: true, error: "", fieldErrors: {} });
    try {
      const learner = await adminLearnerApi.create(values, token);
      navigate(`/admin/learners/${learner.learnerId}`, { replace: true,
        state: { notice: "Đã tạo học viên. Email xác thực đã được gửi theo quy trình tài khoản hiện có." } });
    } catch (error) {
      setState({ submitting: false,
        error: error.data?.message || "Không thể tạo học viên. Vui lòng thử lại.",
        fieldErrors: error.data?.errors || {} });
    }
  }

  return <div className="admin-page admin-form-page">
    <Link className="admin-back-link" to="/admin/learners">← Danh sách học viên</Link>
    <div className="admin-page-heading"><div><p className="admin-eyebrow">Tài khoản mới</p><h1>Thêm học viên</h1>
      <p>Tạo tài khoản an toàn với vai trò Học viên và xác thực email bắt buộc.</p></div></div>
    <section className="admin-panel admin-form-panel">
      <AdminLearnerForm mode="create" submitting={state.submitting} serverError={state.error}
        serverErrors={state.fieldErrors} onSubmit={submit} />
    </section>
  </div>;
}
