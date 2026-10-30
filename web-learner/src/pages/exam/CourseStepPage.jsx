import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { learningApi } from "../../services/adminContentApi";
import { placementApi } from "../../services/examApi";
import "../../styles/admin-content.css";

function Resource({ courseId, stepId, item, token, onReady }) {
  const [state, setState] = useState({ loading: true, url: "", error: "" });
  const videoRef = useRef(null); const resumeRef = useRef(null);
  useEffect(() => {
    let alive = true; let timer;
    async function refresh() {
      if (videoRef.current) resumeRef.current = { time: videoRef.current.currentTime, play: !videoRef.current.paused };
      try {
        const grant = await learningApi.resourceTicket(courseId, stepId, item.id, token);
        if (!alive) return;
        setState({ loading: false, url: learningApi.resourceStreamUrl(grant.url), error: "" });
        timer = setTimeout(refresh, Math.max(1000, new Date(grant.expiresAtUtc).getTime() - Date.now() - 60000));
      } catch (error) { if (alive) setState({ loading: false, url: "", error: error.status === 403 ?
        "Tài khoản chưa có quyền mở tài nguyên này." : "Không mở được tài nguyên. Vui lòng tải lại bài học." }); }
    }
    refresh(); return () => { alive = false; clearTimeout(timer); };
  }, [courseId, stepId, item.id, token]);
  function resumeVideo() {
    if (!videoRef.current || !resumeRef.current) return;
    videoRef.current.currentTime = resumeRef.current.time;
    if (resumeRef.current.play) videoRef.current.play().catch(() => {});
    resumeRef.current = null;
  }
  async function openDocument() {
    try {
      const response = await fetch(`${state.url}&download=true`);
      if (!response.ok) throw new Error();
      const blobUrl = URL.createObjectURL(await response.blob());
      const link = document.createElement("a");
      link.href = blobUrl; link.download = item.name;
      document.body.append(link); link.click(); link.remove();
      setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
      onReady();
    } catch { setState(x => ({ ...x, error: "Không tải được tài liệu. Vui lòng thử lại." })); }
  }
  const office = item.kind === "WORD" || item.kind === "POWERPOINT" || item.kind === "EXCEL";
  return <li className="course-resource"><div><strong>{item.name}</strong><small>{item.kind}</small></div>
    {state.loading && <p role="status">Đang chuẩn bị tài nguyên…</p>}
    {state.error && <p role="alert">{state.error}</p>}
    {state.url && <div className="course-resource-preview">
      {item.kind === "HINH_ANH" ? <img src={state.url} alt={item.name} onLoad={onReady}/> :
        item.kind === "AUDIO" ? <audio src={state.url} controls preload="metadata" onLoadedMetadata={onReady}/> :
          item.kind === "VIDEO" ? <video ref={videoRef} src={state.url} controls playsInline preload="metadata"
            onLoadedMetadata={() => { resumeVideo(); onReady(); }} onError={() => setState(x => ({ ...x, error: "Không phát được video. Vui lòng tải lại bài học." }))}/> :
            item.kind === "PDF" ? <iframe src={state.url} title={`Xem PDF: ${item.name}`} onLoad={onReady}/> : null}
      {office && <p>Tài liệu Office chưa có bản xem trước trong ứng dụng. Tải tệp để mở bằng phần mềm phù hợp.</p>}
      {office ? <button type="button" onClick={openDocument}>Tải tệp {item.name}</button> :
        <a href={`${state.url}&download=true`} download={item.name}>Tải tệp {item.name}</a>}</div>}
  </li>;
}

function ExercisePlayer({ courseId, stepId, token, onComplete }) {
  const [data, setData] = useState(null); const [index, setIndex] = useState(0);
  const [choice, setChoice] = useState(""); const [feedback, setFeedback] = useState(null);
  const [answer, setAnswer] = useState(null); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const [history, setHistory] = useState([]);
  useEffect(() => { learningApi.exerciseHistory(courseId, stepId, token).then(setHistory).catch(() => {}); }, [courseId, stepId, token]);
  useEffect(() => { let live = true; learningApi.exercise(courseId, stepId, token).then(async initial => {
    const x = initial.attemptId || initial.completed ? initial : (await learningApi.startExercise(courseId, stepId, null, token), await learningApi.exercise(courseId, stepId, token));
    if (!live) return;
    setData(x); const previous = x.checks.filter(c => c.questionId === x.questions[0]?.id).sort((a, b) => a.lanKiemTra - b.lanKiemTra).at(-1);
    setChoice(previous?.choice || ""); setFeedback(previous ? { correct: previous.correct, choice: previous.choice } : null); })
    .catch(e => live && setError(e.data?.message || e.message)); return () => { live = false; }; }, [courseId, stepId, token]);
  const question = data?.questions[index];
  async function start() { setBusy(true); setError(""); try { await learningApi.startExercise(courseId, stepId, data?.completed ? data.attemptId : null, token);
    setData(await learningApi.exercise(courseId, stepId, token)); setHistory(await learningApi.exerciseHistory(courseId, stepId, token)); setIndex(0); setChoice(""); setFeedback(null); setAnswer(null); }
    catch (e) { setError(e.data?.message || e.message); } finally { setBusy(false); } }
  async function check() { if (!choice || busy || !data?.attemptId) return; setBusy(true); setError("");
    try { const result = await learningApi.checkExercise(courseId, stepId, data.attemptId,
      { questionId: question.id, choice, requestKey: crypto.randomUUID() }, token);
      setFeedback(result); setData(await learningApi.exercise(courseId, stepId, token)); if (result.completed) onComplete(); }
    catch (e) { setError(e.data?.message || e.message); } finally { setBusy(false); } }
  async function seeAnswer() { try { setAnswer(await learningApi.exerciseAnswer(courseId, stepId, data.attemptId, question.id, token)); }
    catch (e) { setError(e.data?.message || e.message); } }
  function navigate(next) { const id = data.questions[next]?.id;
    const previous = data.checks.filter(x => x.questionId === id).sort((a, b) => a.lanKiemTra - b.lanKiemTra).at(-1);
    setIndex(next); setChoice(previous?.choice || "");
    setFeedback(previous ? { correct: previous.correct, choice: previous.choice } : null); setAnswer(null); }
  if (error && !data) return <p role="alert">{error}</p>;
  if (!data) return <p role="status">Đang tải bài tập…</p>;
  const checkedCount = new Set(data.checks.map(x => x.questionId)).size;
  return <section className="course-exercise"><div className="course-exercise-head"><div><span className="course-player-eyebrow">LUYỆN TẬP TƯƠNG TÁC</span><h2>Trả lời từng câu hỏi</h2></div><span>{checkedCount}/{data.questions.length} câu đã kiểm tra</span></div><details><summary>Lịch sử làm bài ({history.length})</summary>
    <ul>{history.map(x => <li key={x.id}>Lượt {x.number} · phiên bản {x.versionId} · {x.status === "HOAN_THANH" ? "Đã hoàn thành" : "Đang làm"}</li>)}</ul></details>
    <p role="status">Đã kiểm tra {checkedCount}/{data.questions.length} câu · {data.questions.length ? Math.round(100 * checkedCount / data.questions.length) : 0}%</p>
    <progress max={data.questions.length || 1} value={checkedCount}/>
    <nav aria-label="Câu hỏi">{data.questions.map((q, i) => <button key={q.id}
    className={`${index === i ? "is-current" : ""} ${data.checks.filter(c => c.questionId === q.id).at(-1)?.correct === true ? "is-correct" :
      data.checks.some(c => c.questionId === q.id) ? "is-incorrect" : ""}`} onClick={() => navigate(i)}>{i + 1}</button>)}</nav>
    {data.completed && <p role="status">Đã hoàn thành. Bạn có thể xem lại hoặc làm lượt mới.</p>}
    {!data.attemptId || data.completed ? <button className="primary-button" disabled={busy} onClick={start}>Làm lượt mới</button> : null}
    {question && <div className={question.passage ? "course-exercise-columns" : "course-exercise-single"}>
      {question.passage && <div className="course-exercise-passage"><span className="course-player-eyebrow">NGỮ LIỆU</span><p>{question.passage}</p></div>}
      <div className="course-question-panel"><span className="course-player-eyebrow">CÂU HỎI {index + 1} / {data.questions.length}</span><h2>{question.content}</h2>{["a", "b", "c", "d"].map(letter => <label className={`course-exercise-option ${choice === letter.toUpperCase() ? "is-selected" : ""} ${feedback && choice === letter.toUpperCase() ? feedback.correct ? "is-correct" : "is-incorrect" : ""}`} key={letter}>
        <input type="radio" name={`question-${question.id}`} checked={choice === letter.toUpperCase()} disabled={!!feedback || data.completed || !data.attemptId}
          onChange={() => setChoice(letter.toUpperCase())}/>{letter.toUpperCase()}. {question[letter]}</label>)}
        {feedback && <p role="status" className={feedback.correct ? "course-exercise-correct" : "course-exercise-incorrect"}>
          {feedback.correct ? "Chính xác" : "Chưa đúng"}</p>}
        {answer && <div className="course-answer-panel"><strong>Đáp án {answer.correctChoice}</strong><p>{answer.explanation || "Không có giải thích cho câu này."}</p></div>}
        {error && <p role="alert">{error}</p>}
        <div className="course-exercise-actions"><button disabled={!choice || busy || !!feedback || data.completed || !data.attemptId} onClick={check}>Kiểm tra</button>
          {feedback && !data.completed && <button onClick={() => { setFeedback(null); setChoice(""); setAnswer(null); }}>Làm lại câu</button>}
          {(feedback || data.checks.some(x => x.questionId === question.id)) && <button onClick={seeAnswer}>Xem đáp án</button>}
          {index < data.questions.length - 1 && <button onClick={() => navigate(index + 1)}>Câu tiếp →</button>}
        </div></div></div>}</section>;
}

export default function CourseStepPage() {
  const { courseId, stepId } = useParams(); const { token } = useAuth();
  const [state, setState] = useState({ loading: true, data: null, error: "" });
  const [neighbors, setNeighbors] = useState({ previous: null, next: null });
  const [completion, setCompletion] = useState({ done: false, busy: false, error: "" });
  const completionRef = useRef({ key: "", status: "idle" });
  useEffect(() => { let live = true;
    learningApi.step(courseId, stepId, token).then(data => live && setState({ courseId, stepId, loading: false, data, error: "" }))
      .catch(e => live && setState({ courseId, stepId, loading: false, data: null,
        error: e.status === 403 ? "Tài khoản chưa có quyền học khóa này." : e.data?.message || "Không mở được nội dung học." }));
    return () => { live = false; }; }, [courseId, stepId, token]);
  useEffect(() => { let live = true; placementApi.course(courseId, token).then(course => {
    if (!live) return; const steps = course.units.flatMap(u => u.lessons.flatMap(l => l.contents)).filter(x => !x.locked);
    const index = steps.findIndex(x => String(x.id) === String(stepId));
    setNeighbors({ previous: index > 0 ? steps[index - 1] : null, next: index >= 0 ? steps[index + 1] || null : null });
    const key = `${courseId}:${stepId}`;
    if (completionRef.current.key !== key) completionRef.current = { key, status: "idle" };
    if (steps[index]?.progressStatus === "HOAN_THANH") completionRef.current.status = "done";
    setCompletion({ done: completionRef.current.status === "done",
      busy: completionRef.current.status === "pending", error: "" });
  }).catch(() => {}); return () => { live = false; }; }, [courseId, stepId, token]);
  const currentState = state.courseId === courseId && state.stepId === stepId ? state : { loading: true, data: null, error: "" };
  const item = currentState.data;
  const courseReturnPath = item?.unitId ? `/courses/${courseId}?unit=${item.unitId}&lesson=${item.lessonId}` : `/courses/${courseId}`;
  async function complete() {
    const key = `${courseId}:${stepId}`;
    if (completionRef.current.key !== key) completionRef.current = { key, status: "idle" };
    if (completionRef.current.status === "pending" || completionRef.current.status === "done") return;
    completionRef.current.status = "pending";
    setCompletion({ done: false, busy: true, error: "" });
    try {
      await learningApi.complete(courseId, stepId, token);
      if (completionRef.current.key !== key) return;
      completionRef.current = { key, status: "done" };
      setCompletion({ done: true, busy: false, error: "" });
    } catch (e) {
      if (completionRef.current.key !== key) return;
      completionRef.current.status = "idle";
      setCompletion({ done: false, busy: false, error: e.data?.message || "Không lưu được tiến độ." });
    }
  }
  useEffect(() => {
    if (item?.type === "VOCABULARY" || item?.type === "LECTURE" && !item.lecture?.resources?.length) complete();
  }, [item, courseId, stepId, token]);
  return <SiteLayout><main className="site-container course-step-page"><Link className="course-player-back" to={courseReturnPath}>← Trở về lộ trình khóa học</Link>
    {currentState.loading ? <p role="status">Đang tải bài học…</p> : currentState.error ? <p role="alert">{currentState.error}</p> : <article>
      <header className="course-player-header"><p className="course-step-breadcrumb">{item.unitName} <span>›</span> {item.lessonName} <span>›</span> {item.title}</p><span className="course-player-eyebrow">{item.type === "EXERCISE" ? "BÀI TẬP" : "NỘI DUNG HỌC"}</span><h1>{item.title}</h1></header>
      <div className="course-player-workspace"><aside className="course-player-context"><span className="course-player-eyebrow">BẠN ĐANG HỌC</span><strong>{item.unitName}</strong><p>{item.lessonName}</p><div className="course-player-context-current">{item.title}</div><nav aria-label="Chuyển hoạt động">{neighbors.previous && <Link to={`/courses/${courseId}/steps/${neighbors.previous.id}`}>← {neighbors.previous.title}</Link>}{neighbors.next && <Link to={`/courses/${courseId}/steps/${neighbors.next.id}`}>{neighbors.next.title} →</Link>}</nav><Link className="course-player-context-return" to={courseReturnPath}>Xem toàn bộ lộ trình ↗</Link></aside><div className="course-player-content">
      {item.type === "EXERCISE" ? <ExercisePlayer key={`${courseId}-${stepId}`} courseId={courseId} stepId={stepId} token={token}
        onComplete={() => setCompletion({ done: true, busy: false, error: "" })}/> : item.type === "LECTURE" ? <><h2>{item.lecture.title}</h2>
        {item.lecture.summary && <p>{item.lecture.summary}</p>}
        {item.lecture.content ? <div className="course-step-text">{item.lecture.content}</div> :
          <p>Bài giảng chưa có nội dung văn bản.</p>}
        <h3>Tài nguyên</h3>{item.lecture.resources.length ? <ul className="course-resource-list">
          {item.lecture.resources.map(r => <Resource key={r.id} courseId={courseId} stepId={stepId} item={r} token={token} onReady={complete}/>)}</ul> :
          <p>Bài giảng không có tệp đính kèm.</p>}</> : <><h2>{item.topic.title}</h2>
        {item.topic.description && <p>{item.topic.description}</p>}
        {item.topic.words.length ? <div className="course-word-list">{item.topic.words.map((w, i) =>
          <div key={i}><strong>{w.word}</strong><span>{w.meaning}</span>{w.example && <small>{w.example}</small>}</div>)}</div> :
          <p>Chủ đề chưa có từ vựng.</p>}</>}
      </div></div>
    </article>}
    {item && item.type !== "EXERCISE" && <div className="course-completion">
      {completion.error && <p role="alert">{completion.error} <button type="button" onClick={complete}>Thử lại</button></p>}{completion.done && <p role="status">Tiến độ đã lưu. Quay về khóa học để xem cập nhật.</p>}</div>}
    {item && <nav className="course-player-navigation" aria-label="Điều hướng hoạt động">
      {neighbors.previous ? <Link to={`/courses/${courseId}/steps/${neighbors.previous.id}`}>← {neighbors.previous.title}</Link> : <span/>}
      {neighbors.next ? <Link to={`/courses/${courseId}/steps/${neighbors.next.id}`}>{neighbors.next.title} →</Link> : <Link to={courseReturnPath}>Về khóa học</Link>}
    </nav>}</main></SiteLayout>;
}
