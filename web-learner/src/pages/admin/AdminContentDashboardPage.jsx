import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminContentApi } from "../../services/adminContentApi";

export default function AdminContentDashboardPage() {
  const { token } = useAuth();
  const [state, setState] = useState({ loading: true, data: null, error: "" });
  useEffect(() => {
    let live = true;
    adminContentApi.dashboard(token).then(data => live && setState({ loading: false, data, error: "" }))
      .catch(() => live && setState({ loading: false, data: null, error: "Không tải được tổng quan nội dung." }));
    return () => { live = false; };
  }, [token]);
  return <div className="admin-page"><div className="admin-page-header"><div><h1>Tổng quan nội dung</h1>
    <p>Chọn khóa học hiện có để xây dựng chương trình học.</p></div></div>
    {state.loading ? <p role="status">Đang tải…</p> : state.error ? <p role="alert">{state.error}</p> : <>
      <div className="admin-stat-grid">{[
        ["Khóa học", state.data.courses], ["Unit", state.data.units], ["Lesson", state.data.lessons],
        ["Bước lộ trình", state.data.steps], ["Bài giảng", state.data.lectures]
      ].map(([label, value]) => <article className="admin-stat" key={label}><span>{label}</span><strong>{value}</strong></article>)}</div>
      <section className="admin-panel content-next"><h2>Bắt đầu biên soạn</h2><p>Thêm Unit, Lesson và hoạt động vào từng khóa học. Khóa học chưa có nội dung vẫn giữ nguyên trạng thái rỗng.</p>
        <div className="content-actions"><Link className="admin-button" to="/admin/courses">Chọn khóa học</Link></div></section>
    </>}
  </div>;
}
