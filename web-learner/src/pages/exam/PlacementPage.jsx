import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import "../../styles/exam.css";

export default function PlacementPage() {
  const { token } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState(null);
  const [result, setResult] = useState(null);
  const [courses, setCourses] = useState([]);
  const [target, setTarget] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.state(token), placementApi.courses(token)]).then(async ([current, available]) => {
      const outcome = current.status === "DA_NOP" || current.status === "HET_GIO" ?
        await placementApi.result(token) : null;
      if (!live) return;
      setState(current); setCourses(available); setResult(outcome);
      setTarget(outcome?.targetScore?.toString() ?? "");
    }).catch(() => { if (live) setError("Không thể tải thông tin phân lớp."); });
    return () => { live = false; };
  }, [token]);

  async function start() {
    if (pending) return;
    setPending(true); setError("");
    try {
      const started = await placementApi.start(token);
      if (started.status === "DA_NOP" || started.status === "HET_GIO") {
        setState(started);
        setResult(await placementApi.result(token));
      } else navigate(`/exam/${started.attemptId}`);
    } catch (e) {
      setError(e.data?.code === "KHONG_DU_DU_LIEU_TAO_DE" ?
        "Chưa có đề phân lớp đầu vào hợp lệ để bắt đầu." : "Không thể bắt đầu phân lớp.");
    } finally { setPending(false); }
  }

  async function saveTarget(event) {
    event.preventDefault();
    if (pending) return;
    setPending(true); setError("");
    try {
      const saved = await placementApi.target(target === "" ? null : Number(target), token);
      setResult(old => ({ ...old, targetScore: saved.targetScore }));
      setCourses(await placementApi.courses(token));
    } catch (e) {
      setError(e.data?.message || "Không thể lưu điểm mục tiêu.");
    } finally { setPending(false); }
  }

  return <SiteLayout><div className="site-container exam-home">
    <div className="page-heading"><span>Phân lớp</span><h1>Bài kiểm tra đầu vào</h1>
      <p>200 câu · 7 Part · 120 phút · Mỗi học viên một lượt</p></div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!state && !error && <p>Đang tải thông tin phân lớp…</p>}
    {state?.status === "NOT_STARTED" && <section className="exam-card">
      <div><h2>Bắt đầu phân lớp</h2><p>Kết quả sẽ dùng để gợi ý lộ trình khi có mô hình KNN hợp lệ.</p></div>
      <button type="button" className="primary-button" disabled={pending} onClick={start}>Bắt đầu</button>
    </section>}
    {(state?.status === "DANG_LAM" || state?.status === "BO_DO") && <section className="exam-card">
      <div><h2>Tiếp tục bài phân lớp</h2><p>Thời hạn ban đầu được giữ nguyên.</p></div>
      <Link className="primary-button" to={`/exam/${state.attemptId}`}>Tiếp tục</Link>
    </section>}
    {result && <>
      <section className="exam-score"><h2>Kết quả phân lớp</h2><p>Điểm TOEIC ước tính</p>
        <strong>{result.result.totalScore ?? "—"} <small>/ 990</small></strong>
        <p>{result.result.overall.correct} đúng · {result.result.overall.incorrect} sai · {result.result.overall.unanswered} bỏ trống</p>
        <p>{result.classification === "AVAILABLE" ? `Giai đoạn đề xuất: ${result.stage}` :
          "Chưa có dữ liệu/mô hình KNN hợp lệ để phân lớp. Không có giai đoạn giả định."}</p>
        <div className="exam-part-list">{result.partPercentages.map((value, index) =>
          <div key={index}><strong>Part {index + 1}</strong><span>{value}% đúng</span></div>)}</div>
        <Link className="outline-button" to={`/exam/${state.attemptId}/review`}>Xem lại bài</Link>
      </section>
      {result.classification === "AVAILABLE" && <section><h2>Điểm mục tiêu</h2>
        <form className="exam-target-form" onSubmit={saveTarget}>
          <label htmlFor="placement-target">Điểm TOEIC mục tiêu</label>
          <input id="placement-target" type="number" min="10" max="990" value={target}
            onChange={event => setTarget(event.target.value)} />
          <button className="primary-button" type="submit" disabled={pending}>Lưu mục tiêu</button>
        </form><p>Đổi mục tiêu không chạy lại KNN.</p>
      </section>}
    </>}
    <section><h2>Khóa học đang mở</h2>
      {courses.length ? <div className="exam-card-list">{courses.map(course => <article className="exam-card" key={course.courseId}>
        <div><h3>{course.name} {course.recommended && <span className="exam-recommended">Được gợi ý</span>}</h3>
          <p>Giai đoạn {course.stage} · Mục tiêu tối đa {course.maxTargetScore}</p>
          {course.description && <p>{course.description}</p>}</div>
      </article>)}</div> : <p>Chưa có khóa học đang mở.</p>}
      <p className="exam-card-note">Gợi ý khóa học không cấp quyền học hoặc đăng ký gói.</p>
    </section>
  </div></SiteLayout>;
}
