import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminLearnerApi } from "../../services/adminLearnerApi";
import { AdminMessage } from "./AdminLearnersPage";
import AdminLearnerForm from "./AdminLearnerForm";

export default function AdminLearnerEditPage() {
  const { learnerId } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const requestKey = `${token}|${learnerId}`;
  const [request, setRequest] = useState({ key: null, learner: null, error: "" });
  const [save, setSave] = useState({ submitting: false, error: "", fieldErrors: {} });
  const current = request.key === requestKey ? request : { learner: null, error: "" };

  useEffect(() => {
    let active = true;
    adminLearnerApi.detail(learnerId, token)
      .then(learner => { if (active) setRequest({ key: requestKey, learner, error: "" }); })
      .catch(error => { if (active) setRequest({ key: requestKey, learner: null,
        error: error.status === 404 ? "Không tìm thấy học viên này." : "Không thể tải hồ sơ học viên." }); });
    return () => { active = false; };
  }, [learnerId, token, requestKey]);

  async function submit(values) {
    setSave({ submitting: true, error: "", fieldErrors: {} });
    try {
      await adminLearnerApi.update(learnerId, values, token);
      navigate(`/admin/learners/${learnerId}`, { replace: true,
        state: { notice: "Thông tin học viên đã được cập nhật." } });
    } catch (error) {
      setSave({ submitting: false, error: error.data?.message || "Không thể cập nhật học viên.",
        fieldErrors: error.data?.errors || {} });
    }
  }

  if (request.key !== requestKey) return <div className="admin-page"><div className="admin-panel admin-loading" role="status">
    <span className="admin-spinner" aria-hidden="true" /><strong>Đang tải thông tin học viên</strong></div></div>;
  if (current.error) return <div className="admin-page"><Link className="admin-back-link" to="/admin/learners">← Danh sách học viên</Link>
    <section className="admin-panel"><AdminMessage title="Không thể sửa học viên" detail={current.error} /></section></div>;

  return <div className="admin-page admin-form-page">
    <Link className="admin-back-link" to={`/admin/learners/${learnerId}`}>← Hồ sơ học viên</Link>
    <div className="admin-page-heading"><div><p className="admin-eyebrow">ID #{learnerId}</p><h1>Sửa thông tin học viên</h1>
      <p>Chỉ họ tên và số điện thoại được phép thay đổi.</p></div></div>
    <section className="admin-panel admin-form-panel">
      <AdminLearnerForm mode="edit" initial={current.learner} submitting={save.submitting}
        serverError={save.error} serverErrors={save.fieldErrors} onSubmit={submit} />
    </section>
  </div>;
}
