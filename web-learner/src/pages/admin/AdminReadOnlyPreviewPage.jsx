import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest, API_BASE_URL } from "../../services/api";
import { useContentRevision } from "../../services/contentRevisions";
import "../../styles/admin-read-only-preview.css";

const root = "/api/admin/preview";

function PreviewFile({ courseId, stepId, resource, token }) {
  const [state, setState] = useState({ url: "", error: "" });
  const [renewal, setRenewal] = useState(0);
  const videoRef = useRef(null);
  const resumeAt = useRef(null);
  const resumePlaying = useRef(false);
  const lastRenewal = useRef(0);
  useEffect(() => {
    let live = true;
    apiRequest(`${root}/courses/${courseId}/steps/${stepId}/resources/${resource.id}/ticket`,
      { method: "POST", token }).then(data => {
        if (live) setState({ url: `${API_BASE_URL}${data.url}`, error: "" });
      }).catch(() => { if (live) setState({ url: "", error: "Không mở được tài liệu. Hãy thử lại." }); });
    return () => { live = false; };
  }, [courseId, stepId, resource.id, token, renewal]);
  function videoError() {
    if (Date.now() - lastRenewal.current > 30_000) {
      lastRenewal.current = Date.now();
      resumeAt.current = videoRef.current?.currentTime || 0;
      resumePlaying.current = !videoRef.current?.paused;
      setRenewal(value => value + 1);
      return;
    }
    setState(current => ({ ...current, error: "Video không phát được. Tệp có thể đã bị gỡ hoặc kho lưu trữ đang gián đoạn." }));
  }
  return <div className="admin-preview-file"><strong>{resource.name}</strong>
    {state.error && <p role="alert">{state.error}</p>}
    {state.url && <>{resource.kind === "VIDEO" ? <video ref={videoRef} src={state.url} controls playsInline preload="metadata"
      onError={videoError} onLoadedMetadata={event => {
        if (resumeAt.current != null) {
          event.currentTarget.currentTime = resumeAt.current;
          resumeAt.current = null;
          if (resumePlaying.current) event.currentTarget.play().catch(() => {});
          resumePlaying.current = false;
        }
      }} /> :
      resource.kind === "AUDIO" ? <audio src={state.url} controls /> :
      resource.kind === "HINH_ANH" ? <img src={state.url} alt={resource.name} /> :
      resource.kind === "PDF" ? <iframe src={state.url} title={resource.name} /> : null}
      <a href={`${state.url}&download=true`} download={resource.name}>Tải tệp</a></>}
  </div>;
}

export default function AdminReadOnlyPreviewPage() {
  const { token, user } = useAuth();
  const contentRevision = useContentRevision("COURSES", "CURRICULUM", "LECTURES", "VOCABULARY", "EXERCISES");
  const [params, setParams] = useSearchParams();
  const courseId = Number(params.get("course")) || null;
  const stepId = Number(params.get("step")) || null;
  const [courses, setCourses] = useState(null);
  const [outline, setOutline] = useState(null);
  const [step, setStep] = useState(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let live = true;
    apiRequest(`${root}/courses`, { token }).then(data => { if (live) setCourses(data); })
      .catch(() => { if (live) setError("Không tải được danh sách khóa học."); });
    return () => { live = false; };
  }, [token, revision, contentRevision]);
  useEffect(() => {
    if (!courseId) { setOutline(null); return undefined; }
    let live = true;
    setOutline(null); setError("");
    apiRequest(`${root}/courses/${courseId}`, { token }).then(data => { if (live) setOutline(data); })
      .catch(() => { if (live) setError("Không tải được lộ trình khóa học."); });
    return () => { live = false; };
  }, [courseId, token, revision, contentRevision]);
  useEffect(() => {
    if (!courseId || !stepId) { setStep(null); return undefined; }
    let live = true;
    setStep(null); setError("");
    apiRequest(`${root}/courses/${courseId}/steps/${stepId}`, { token })
      .then(data => { if (live) setStep(data); })
      .catch(() => { if (live) setError("Không mở được hoạt động này. Nội dung có thể chưa xuất bản."); });
    return () => { live = false; };
  }, [courseId, stepId, token, revision, contentRevision]);
  const setContext = (course, selectedStep) => setParams({ ...(course && { course: String(course) }),
    ...(selectedStep && { step: String(selectedStep) }) });
  const steps = outline?.units.flatMap(unit => unit.lessons.flatMap(lesson =>
    lesson.steps.map(item => ({ ...item, unit: unit.name, lesson: lesson.name })) )) || [];
  const stepIndex = steps.findIndex(item => item.id === stepId);
  return <div className="admin-page admin-read-preview">
    <div className="admin-page-header"><div><h1>Xem như học viên</h1><p>Chế độ chỉ đọc. Không mua khóa học, làm bài hay lưu tiến độ.</p></div>
      <Link className="admin-button admin-button-secondary" to={user?.vaiTro === "ADMIN_QUAN_LY" ? "/admin" : "/admin/content"}>Về trang quản trị</Link></div>
    {error && <p className="admin-feedback is-error" role="alert">{error} <button type="button" onClick={() => setRevision(value => value + 1)}>Thử lại</button></p>}
    <div className="admin-preview-layout"><aside className="admin-panel admin-preview-sidebar">
      <h2>Khóa học</h2>{!courses && !error && <p>Đang tải khóa học…</p>}
      {courses?.map(course => <button type="button" key={course.id} className={courseId === course.id ? "is-active" : ""}
        onClick={() => setContext(course.id, null)}>{course.name}</button>)}
      {courses?.length === 0 && <p>Chưa có khóa học có thể xem.</p>}
      {outline && <><h2>Unit · Bài học</h2>{outline.units.map(unit => <div key={unit.id} className="admin-preview-unit">
        <strong>{unit.name}</strong>{unit.lessons.map(lesson => <div key={lesson.id}><small>{lesson.name}</small>
          {lesson.steps.map(item => <button type="button" key={item.id} className={stepId === item.id ? "is-active" : ""}
            onClick={() => setContext(courseId, item.id)}>{item.title}</button>)}
        </div>)}</div>)}</>}
    </aside><section className="admin-panel admin-preview-content">
      {!courseId && <p>Chọn khóa học để xem lộ trình và tài liệu.</p>}
      {courseId && !outline && !error && <p>Đang tải lộ trình…</p>}
      {outline && !stepId && <><h2>{outline.course.name}</h2><p>{outline.course.description}</p>
        <p>Chọn một hoạt động trong lộ trình để xem chi tiết.</p></>}
      {stepId && !step && !error && <p>Đang tải hoạt động…</p>}
      {step && <><p className="admin-preview-breadcrumb">{outline?.course.name} › {step.unit} › {step.lesson}</p><h2>{step.title}</h2>
        {step.type === "LECTURE" && <><p>{step.lecture.summary}</p><div className="admin-preview-text">{step.lecture.content}</div>
          {step.lecture.resources.map(resource => <PreviewFile key={resource.id} courseId={courseId} stepId={stepId}
            resource={resource} token={token} />)}</>}
        {step.type === "VOCABULARY" && <><p>{step.topic.description}</p><ul>{step.topic.words.map((word, index) =>
          <li key={index}><strong>{word.word}</strong> — {word.meaning}{word.example && <small> · {word.example}</small>}</li>)}</ul></>}
        {step.type === "EXERCISE" && <div className="admin-preview-questions">{step.questions.map((question, index) =>
          <article key={index}><h3>Câu {index + 1}. {question.text}</h3>{question.passage && <p>{question.passage}</p>}
            <ol type="A"><li>{question.a}</li><li>{question.b}</li><li>{question.c}</li><li>{question.d}</li></ol>
            <p><strong>Đáp án đúng: {question.correct}</strong> {question.explanation && `· ${question.explanation}`}</p></article>)}</div>}
        <nav className="admin-preview-next">{stepIndex > 0 && <button type="button" onClick={() => setContext(courseId, steps[stepIndex - 1].id)}>← Hoạt động trước</button>}
          {stepIndex >= 0 && stepIndex < steps.length - 1 && <button type="button" onClick={() => setContext(courseId, steps[stepIndex + 1].id)}>Hoạt động tiếp →</button>}</nav></>}
    </section></div>
  </div>;
}
