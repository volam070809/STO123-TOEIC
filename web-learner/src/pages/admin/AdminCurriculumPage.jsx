import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminContentApi as api } from "../../services/adminContentApi";
import { CourseCover } from "../exam/coursePresentation";
import AdminSectionStats from "../../components/admin/AdminSectionStats";
import "../../styles/admin-content.css";

const emptyQuestion = () => ({ content: "", passage: "", a: "", b: "", c: "", d: "", correct: "A", explanation: "" });
const emptyContent = () => ({ title: "", summary: "", content: "" });
const emptyExercise = () => ({ title: "", questions: [emptyQuestion()] });
const kindFromFile = (file, excelEnabled) => {
  const extension = file.name.split(".").at(-1)?.toLowerCase();
  const kinds = { mp4: "VIDEO", mp3: "AUDIO", wav: "AUDIO", pdf: "PDF", doc: "WORD", docx: "WORD",
    ppt: "POWERPOINT", pptx: "POWERPOINT", jpg: "HINH_ANH", jpeg: "HINH_ANH", png: "HINH_ANH", webp: "HINH_ANH" };
  if (excelEnabled) { kinds.xls = "EXCEL"; kinds.xlsx = "EXCEL"; }
  return kinds[extension];
};
const errorText = e => e.data?.message || ({ MEDIA_UNAVAILABLE: "Không kết nối được kho lưu trữ tệp.",
  MEDIA_PERSISTENCE_UNCERTAIN: "Chưa xác định được kết quả lưu tệp. Mở lại hoạt động để kiểm tra trước khi thử lại." })[e.data?.code]
  || e.message || "Không thực hiện được thao tác.";

function UploadStatus({ progress }) {
  if (!progress) return null;
  const stage = progress.phase === "verify" ? "Đang xác minh Blob và lưu SQL" :
    progress.phase === "saving" ? "Đang kiểm tra tệp trong danh sách đã lưu" :
      progress.phase === "saved" ? "Đã lưu và xác minh" :
        progress.phase === "error" ? "Chưa tải tệp" : "Đang tải lên";
  return <div className="builder-upload-status" role="status" aria-live="polite">
    <strong>{progress.name}</strong><span>{progress.index && progress.total > 1 ?
      `Tệp ${progress.index}/${progress.total} · ` : ""}{stage} · {progress.percent}%</span>
    <progress max="100" value={progress.percent} aria-label={`Tiến độ ${progress.name}`} />
  </div>;
}

function PreviewResource({ lectureId, resource, token }) {
  const [state, setState] = useState({ url: "", error: "" });
  const [renewal, setRenewal] = useState(0);
  const videoRef = useRef(null);
  const resumeAt = useRef(null);
  const resumePlaying = useRef(false);
  const lastRenewal = useRef(0);
  useEffect(() => {
    let live = true; let url = "";
    const request = resource.kind === "VIDEO" ?
      api.previewVideoTicket(lectureId, resource.id, token).then(result => ({
        url: api.previewVideoUrl(result.url), temporary: false })) :
      api.resourceBlob(lectureId, resource.id, token).then(blob => ({
        url: URL.createObjectURL(blob), temporary: true }));
    request.then(result => {
      if (!live) { if (result.temporary) URL.revokeObjectURL(result.url); return; }
      url = result.temporary ? result.url : "";
      setState({ url: result.url, error: "" });
    }).catch(e => live && setState({ url: "", error: errorText(e) }));
    return () => { live = false; if (url) URL.revokeObjectURL(url); };
  }, [lectureId, resource.id, resource.kind, token, renewal]);
  function videoError() {
    if (Date.now() - lastRenewal.current > 30_000) {
      lastRenewal.current = Date.now();
      resumeAt.current = videoRef.current?.currentTime || 0;
      resumePlaying.current = !videoRef.current?.paused;
      setRenewal(value => value + 1);
      return;
    }
    setState(current => ({ ...current,
      error: "Video không phát được. Tệp có thể đã bị gỡ hoặc kho lưu trữ đang gián đoạn." }));
  }
  return <div className="builder-preview-resource"><strong>{resource.name}</strong>
    {state.error && <p role="alert">{state.error}</p>}
    {state.url && <>{resource.kind === "VIDEO" ? <video ref={videoRef} controls playsInline preload="metadata"
      src={state.url} onError={videoError} onLoadedMetadata={event => {
        if (resumeAt.current != null) {
          event.currentTarget.currentTime = resumeAt.current;
          resumeAt.current = null;
          if (resumePlaying.current) event.currentTarget.play().catch(() => {});
          resumePlaying.current = false;
        }
      }}/> :
      resource.kind === "AUDIO" ? <audio controls src={state.url}/> :
      resource.kind === "HINH_ANH" ? <img src={state.url} alt={resource.name}/> :
      resource.kind === "PDF" ? <iframe src={state.url} title={resource.name}/> : null}
      <a href={state.url} download={resource.name}>Tải tệp</a></>}</div>;
}

function PreviewExercise({ questions }) {
  const [index, setIndex] = useState(0); const [selected, setSelected] = useState({});
  const [checked, setChecked] = useState({}); const [revealed, setRevealed] = useState({});
  const question = questions[index]; const choice = selected[index]; const result = checked[index];
  if (!question) return <p>Bài tập chưa có câu hỏi.</p>;
  return <div className="course-exercise builder-preview-exercise"><div className="course-exercise-head"><div><span className="course-player-eyebrow">BÀI TẬP XEM TRƯỚC</span><h2>Trả lời từng câu hỏi</h2></div><span>{Object.keys(checked).length}/{questions.length} câu đã kiểm tra</span></div>
    <nav aria-label="Câu hỏi">{questions.map((q, number) => <button key={number} className={`${number === index ? "is-current" : ""} ${checked[number] ? checked[number].correct ? "is-correct" : "is-incorrect" : ""}`} onClick={() => setIndex(number)}>{number + 1}</button>)}</nav>
    <div className={question.passage ? "course-exercise-columns" : "course-exercise-single"}>{question.passage && <div className="course-exercise-passage"><span className="course-player-eyebrow">NGỮ LIỆU</span><p>{question.passage}</p></div>}
      <div className="course-question-panel"><span className="course-player-eyebrow">CÂU HỎI {index + 1} / {questions.length}</span><h2>{question.content}</h2>
        {["a", "b", "c", "d"].map(letter => <label key={letter} className={`course-exercise-option ${choice === letter.toUpperCase() ? "is-selected" : ""} ${result && choice === letter.toUpperCase() ? result.correct ? "is-correct" : "is-incorrect" : ""}`}><input type="radio" name={`preview-question-${index}`} checked={choice === letter.toUpperCase()} disabled={!!result} onChange={() => setSelected(x => ({ ...x, [index]: letter.toUpperCase() }))}/>{letter.toUpperCase()}. {question[letter]}</label>)}
        {result && <p className={result.correct ? "course-exercise-correct" : "course-exercise-incorrect"}>{result.correct ? "Chính xác" : "Chưa đúng"}</p>}
        {revealed[index] && <div className="course-answer-panel"><strong>Đáp án {question.correct}</strong><p>{question.explanation || "Không có giải thích cho câu này."}</p></div>}
        <div className="course-exercise-actions"><button disabled={!choice || !!result} onClick={() => setChecked(x => ({ ...x, [index]: { correct: choice === question.correct } }))}>Kiểm tra</button>
          {result && <button onClick={() => { setChecked(x => { const next = { ...x }; delete next[index]; return next; }); setSelected(x => ({ ...x, [index]: "" })); setRevealed(x => ({ ...x, [index]: false })); }}>Làm lại câu</button>}
          {result && <button onClick={() => setRevealed(x => ({ ...x, [index]: true }))}>Xem đáp án</button>}
          {index < questions.length - 1 && <button onClick={() => setIndex(index + 1)}>Câu tiếp →</button>}</div></div></div></div>;
}

export default function AdminCurriculumPage() {
  const { courseId } = useParams(); const { token } = useAuth();
  const [outline, setOutline] = useState(null); const [selected, setSelected] = useState(null);
  const [expanded, setExpanded] = useState({}); const [mode, setMode] = useState("LECTURE");
  const [content, setContent] = useState(emptyContent); const [exercise, setExercise] = useState(emptyExercise);
  const [lecture, setLecture] = useState(null); const [files, setFiles] = useState([]);
  const [copyMode, setCopyMode] = useState(false);
  const [capabilities, setCapabilities] = useState({ excelEnabled: false, maxFileBytes: 250000000, maxVideoBytes: 950000000 });
  const [preview, setPreview] = useState(null); const [loading, setLoading] = useState(true);
  const [previewResourceId, setPreviewResourceId] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailLoadFailed, setDetailLoadFailed] = useState(false);
  const [previewIndex, setPreviewIndex] = useState(0);
  const [nameDialog, setNameDialog] = useState(null);
  const [deleteDialog, setDeleteDialog] = useState(null);
  const [studioTab, setStudioTab] = useState("curriculum");
  const [statsRevision, setStatsRevision] = useState(0);
  const [editorTab, setEditorTab] = useState("info");
  const [editorDirty, setEditorDirty] = useState(false);
  const [search, setSearch] = useState("");
  const [progressSummary, setProgressSummary] = useState(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [notice, setNotice] = useState("");
  const [uploadProgress, setUploadProgress] = useState(null);
  const [uploadError, setUploadError] = useState(null);
  const [lastSavedId, setLastSavedId] = useState(null);
  const outlineRequest = useRef(0);
  const selectionRequest = useRef(0);
  const editVersion = useRef(0);
  const unit = outline?.units.find(x => x.id === selected?.unitId);
  const lesson = unit?.lessons.find(x => x.id === selected?.lessonId);
  const activity = lesson?.steps.find(x => x.id === selected?.stepId);
  useEffect(() => {
    if (lastSavedId == null) return;
    document.querySelector(`.builder-editor [data-resource-id="${lastSavedId}"]`)
      ?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [lastSavedId]);

  async function refresh() { const request = ++outlineRequest.current; const data = await api.outline(courseId, token);
    if (request === outlineRequest.current) { setOutline(data); setLoading(false); setStatsRevision(value => value + 1); }
    return data; }
  useEffect(() => { let live = true; const request = ++outlineRequest.current;
    Promise.all([api.outline(courseId, token), api.resourceCapabilities(token)])
    .then(([data, caps]) => { if (live && request === outlineRequest.current) { setOutline(data); setCapabilities(caps); setLoading(false);
      if (data.units[0]) { const first = data.units.find(u => !u.isArchived) || data.units[0];
        const firstLesson = first.lessons.find(l => !l.isArchived) || first.lessons[0];
        const firstStep = firstLesson?.steps.find(s => !s.isArchived);
        if (firstStep) select(first.id, firstLesson.id, firstStep);
        else setSelected({ unitId: first.id, lessonId: firstLesson?.id || null });
        setExpanded(Object.fromEntries(data.units.flatMap(u => [[`u${u.id}`, true], ...u.lessons.map(l => [`l${l.id}`, true])]))); }
    } })
    .catch(e => { if (live && request === outlineRequest.current) { setError(errorText(e)); setLoading(false); } });
    return () => { live = false; }; }, [courseId, token]);
  useEffect(() => { if (studioTab !== "progress" || !token) return; let live = true;
    api.progressSummary(courseId, token).then(value => live && setProgressSummary(value))
      .catch(e => live && setError(errorText(e))); return () => { live = false; }; }, [courseId, studioTab, token]);
  useEffect(() => { if (!editorDirty && !files.length) return; const warn = e => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn); }, [editorDirty, files.length]);
  const expand = key => setExpanded(x => ({ ...x, [key]: !x[key] }));
  function resetEditor() { setContent(emptyContent()); setExercise(emptyExercise()); setLecture(null); setFiles([]); setCopyMode(false); setMode("LECTURE"); setEditorTab("info"); setEditorDirty(false); setDetailLoading(false); setDetailLoadFailed(false); setUploadProgress(null); setUploadError(null); setLastSavedId(null); setError(""); setNotice(""); }
  async function select(unitId, lessonId = null, step = null, discard = false) {
    if (busy && !discard) return;
    if ((editorDirty || files.length) && !discard && !window.confirm("Bạn có thông tin hoặc tệp chưa lưu. Rời khỏi hoạt động này?")) return;
    const request = ++selectionRequest.current;
    resetEditor(); setSelected({ unitId, lessonId, stepId: step?.id || null });
    if (!step) return;
    setDetailLoading(true);
    setMode(step.activityType);
    try {
      if (step.activityType === "LECTURE") {
        const item = await api.lecture(step.activityId, token);
        if (request !== selectionRequest.current) return;
        setLecture(item); setContent({ title: item.title, summary: item.summary || "", content: item.content || "" });
      } else if (step.activityType === "EXERCISE") {
        const item = await api.exerciseDetail(courseId, unitId, lessonId, step.id, token);
        if (request !== selectionRequest.current) return;
        setExercise({ title: item.title, questions: item.questions });
      }
    } catch (e) { if (request === selectionRequest.current) { setError(errorText(e)); setDetailLoadFailed(true); } }
    finally { if (request === selectionRequest.current) setDetailLoading(false); }
  }
  async function perform(action, success) {
    if (busy) return null; setBusy(true); setError(""); setNotice("");
    try { const value = await action(); await refresh(); if (success) setNotice(success); return value ?? true; }
    catch (e) { setError(errorText(e)); return null; } finally { setBusy(false); }
  }
  function addUnit() { setNameDialog({ type: "unit", name: "", description: "" }); }
  function addLesson(unitId) { setNameDialog({ type: "lesson", unitId, name: "", description: "" }); }
  function rename(type, item, unitId) { setNameDialog({ type, item, unitId, name: item.name || item.title, description: item.description || "" }); }
  async function saveName(e) {
    e.preventDefault(); const dialog = nameDialog; const name = dialog.name.trim(); if (!name) return;
    const body = { name, description: dialog.description.trim() || null };
    const result = await perform(() => dialog.item ? dialog.type === "unit" ? api.editUnit(courseId, dialog.item.id, body, token) :
      api.editLesson(courseId, dialog.unitId, dialog.item.id, body, token) : dialog.type === "unit" ? api.addUnit(courseId, body, token) :
        api.addLesson(courseId, dialog.unitId, body, token), dialog.item ? "Đã cập nhật." : "Đã thêm nội dung.");
    if (!result) return; setNameDialog(null);
    if (result.id && dialog.type === "unit") { setSelected({ unitId: result.id }); setExpanded(x => ({ ...x, [`u${result.id}`]: true })); }
    if (result.id && dialog.type === "lesson") { setSelected({ unitId: dialog.unitId, lessonId: result.id }); setExpanded(x => ({ ...x, [`u${dialog.unitId}`]: true, [`l${result.id}`]: true })); }
  }
  async function remove(type, item, unitId, lessonId) {
    try { const impact = type === "unit" ? await api.unitDeleteImpact(courseId, item.id, token) :
      type === "lesson" ? await api.lessonDeleteImpact(courseId, unitId, item.id, token) :
        await api.stepDeleteImpact(courseId, unitId, lessonId, item.id, token);
      setDeleteDialog({ type, item, unitId, lessonId, impact }); }
    catch (e) { setError(errorText(e)); }
  }
  async function removeResource(item) {
    try { const impact = await api.resourceDeleteImpact(lecture.id, item.id, token);
      setDeleteDialog({ type: "resource", item, lectureId: lecture.id, impact }); }
    catch (e) { setError(errorText(e)); }
  }
  async function archive(type, item, unitId, lessonId, isArchived = true) {
    const result = await perform(() => type === "unit" ? api.archiveUnit(courseId, item.id, isArchived, token) :
      type === "lesson" ? api.archiveLesson(courseId, unitId, item.id, isArchived, token) :
        api.archiveStep(courseId, unitId, lessonId, item.id, isArchived, token), isArchived ? "Đã lưu trữ khỏi lộ trình học." : "Đã khôi phục nội dung.");
    if (result && isArchived) setSelected(null);
  }
  async function duplicate(type, item, unitId, lessonId) {
    const result = await perform(() => type === "unit" ? api.duplicateUnit(courseId, item.id, token) :
      type === "lesson" ? api.duplicateLesson(courseId, unitId, item.id, token) :
        api.duplicateStep(courseId, unitId, lessonId, item.id, token), "Đã tạo bản sao độc lập.");
    if (result?.id && type === "step") { const data = await api.outline(courseId, token);
      const newStep = data.units.find(u => u.id === unitId)?.lessons.find(l => l.id === lessonId)?.steps.find(s => s.id === result.id);
      if (newStep) await select(unitId, lessonId, newStep); }
    else if (result?.id) setSelected(type === "unit" ? { unitId: result.id } : { unitId, lessonId: result.id });
  }
  async function confirmDelete() {
    const { type, item, unitId, lessonId, impact, lectureId } = deleteDialog;
    const includeHistory = impact.progressRecords + impact.attempts + impact.checks > 0 ? impact : null;
    const result = await perform(() => type === "resource" ? api.deleteResource(lectureId, item.id, token, includeHistory) :
      type === "unit" ? api.deleteUnit(courseId, item.id, token, includeHistory) :
      type === "lesson" ? api.deleteLesson(courseId, unitId, item.id, token, includeHistory) :
        api.deleteStep(courseId, unitId, lessonId, item.id, token, includeHistory), "Đã xóa vĩnh viễn.");
    if (result) { setDeleteDialog(null); if (type === "resource") setLecture(await api.lecture(lectureId, token));
      else { resetEditor(); setSelected(type === "unit" ? null : type === "lesson" ? { unitId } : { unitId, lessonId }); } }
  }
  async function move(type, items, index, direction, unitId, lessonId) {
    if (busy || index + direction < 0 || index + direction >= items.length) return;
    const next = [...items]; [next[index], next[index + direction]] = [next[index + direction], next[index]];
    await perform(() => type === "unit" ? api.orderUnits(courseId, next.map(x => x.id), token)
      : type === "lesson" ? api.orderLessons(courseId, unitId, next.map(x => x.id), token)
        : api.orderSteps(courseId, unitId, lessonId, next.map(x => x.id), token), "Đã sắp xếp.");
  }
  const actions = (type, item, items, index, unitId, lessonId) => <details className="builder-actions"><summary aria-label={`Thao tác với ${item.name || item.title}`} title="Thao tác">⋯</summary><span>
    <button type="button" onClick={() => showPreview(type === "unit" ? item.lessons.flatMap(l => l.steps) : type === "lesson" ? item.steps : [item], item.name || item.title)}>Xem trước</button>
    <button type="button" disabled={busy || index === 0} aria-label="Đưa lên" onClick={() => move(type, items, index, -1, unitId, lessonId)}>↑</button>
    <button type="button" disabled={busy || index === items.length - 1} aria-label="Đưa xuống" onClick={() => move(type, items, index, 1, unitId, lessonId)}>↓</button>
    {type !== "step" && <button type="button" onClick={() => rename(type, item, unitId, lessonId)}>Sửa</button>}
    <button type="button" onClick={() => duplicate(type, item, unitId, lessonId)}>Nhân bản</button>
    <button type="button" onClick={() => archive(type, item, unitId, lessonId, !item.isArchived)}>{item.isArchived ? "Khôi phục" : "Lưu trữ"}</button>
    <button type="button" onClick={() => remove(type, item, unitId, lessonId)}>Xóa</button></span></details>;
  function chooseFiles(list) {
    const next = [...list].map(file => ({ file, kind: kindFromFile(file, capabilities.excelEnabled) }));
    if (next.some(x => !x.kind || x.file.size > (x.kind === "VIDEO" ? capabilities.maxVideoBytes : capabilities.maxFileBytes))) {
      setUploadProgress(null); setUploadError("Tệp không đúng định dạng hoặc vượt quá giới hạn dung lượng."); return;
    }
    setFiles(next); setUploadError(null); setUploadProgress(null); setLastSavedId(null);
  }
  async function saveContent(e) {
    e.preventDefault(); if (busy || !lesson) return;
    const savedVersion = editVersion.current;
    setBusy(true); setError(""); setNotice("");
    let persisted = false;
    try {
      if (activity && !copyMode) {
        const lectureId = lecture?.id ?? activity.activityId;
        const changed = !lecture || content.title !== lecture.title || content.summary !== (lecture.summary || "") ||
          content.content !== (lecture.content || "");
        if (changed) await api.editCourseLecture(courseId, unit.id, lesson.id, activity.id,
          { ...content, categoryId: null, level: null, partId: null }, token);
        persisted = true;
        setLecture(await api.lecture(lectureId, token));
      }
      else {
        const result = await api.addCourseLecture(courseId, unit.id, lesson.id,
          { ...content, categoryId: null, level: null, partId: null }, token);
        persisted = true;
        setSelected({ unitId: unit.id, lessonId: lesson.id, stepId: result.id });
        setLecture(await api.lecture(result.lectureId, token));
      }
      await refresh();
      setCopyMode(false); if (editVersion.current === savedVersion) setEditorDirty(false);
      setNotice("Đã lưu thông tin hoạt động. Để thêm tài nguyên, chọn tệp rồi nhấn Tải tệp đã chọn.");
    } catch (e) {
      const needsCopy = e.status === 409 && activity && !copyMode;
      if (needsCopy) setCopyMode(true);
      setError(persisted ? "Thông tin đã lưu nhưng chưa tải lại được. Chọn lại hoạt động để xem dữ liệu mới; không lưu lặp." :
        needsCopy ? `${errorText(e)} Lưu thành hoạt động mới để giữ lịch sử học.` : errorText(e));
    } finally { setBusy(false); }
  }
  async function uploadSelectedFiles() {
    if (!lecture || !files.length || busy) return;
    setBusy(true); setUploadError(null); setError(""); setNotice(""); setLastSavedId(null);
    const total = files.length;
    let currentIndex = 0;
    try {
      for (const entry of files) {
        currentIndex++;
        const body = new FormData(); body.set("name", entry.file.name); body.set("kind", entry.kind); body.set("file", entry.file);
        setUploadProgress({ name: entry.file.name, percent: 0, phase: "upload", index: currentIndex, total });
        const uploaded = await api.uploadResource(lecture.id, null, body, token,
          (percent, phase = "upload") => setUploadProgress({ name: entry.file.name, percent, phase, index: currentIndex, total }));
        setUploadProgress({ name: entry.file.name, percent: 100, phase: "saving", index: currentIndex, total });
        setFiles(current => current.filter(file => file !== entry));
        let saved;
        try { saved = await api.lecture(lecture.id, token); }
        catch { throw new Error("Tệp đã lưu nhưng chưa tải lại được danh sách. Mở lại hoạt động để xem; không tải lại cùng tệp."); }
        setLecture(saved);
        if (!saved.resources.some(resource => resource.id === uploaded.id))
          throw new Error("Máy chủ đã xác nhận lưu tệp nhưng danh sách chưa cập nhật. Tải lại hoạt động để kiểm tra; không tải lại cùng tệp.");
        setLastSavedId(uploaded.id);
        setUploadProgress({ name: entry.file.name, percent: 100, phase: "saved", index: currentIndex, total });
      }
      await refresh(); setNotice("Đã tải và lưu tệp. Tài nguyên hiện có trong mục Tệp đã lưu.");
    } catch (e) {
      setUploadError(errorText(e));
      try { setLecture(await api.lecture(lecture.id, token)); } catch { /* Giữ thông báo lỗi gốc. */ }
    } finally { setBusy(false); }
  }
  async function saveExercise(e, publish) {
    e.preventDefault(); if (busy || !lesson) return; setBusy(true); setError(""); setNotice("");
    const savedVersion = editVersion.current;
    try {
      const body = { ...exercise, publish };
      const result = activity ? await api.reviseExercise(courseId, unit.id, lesson.id, activity.id, body, token)
        : await api.addExercise(courseId, unit.id, lesson.id, body, token);
      setSelected({ unitId: unit.id, lessonId: lesson.id, stepId: result.id });
      if (editVersion.current === savedVersion) setEditorDirty(false);
      try {
        const saved = await api.exerciseDetail(courseId, unit.id, lesson.id, result.id, token);
        if (editVersion.current === savedVersion) setExercise({ title: saved.title, questions: saved.questions });
        await refresh();
      } catch { setError("Bài tập đã lưu nhưng chưa tải lại được. Chọn lại hoạt động để kiểm tra; không lưu lặp."); return; }
      setNotice(publish ? "Đã xuất bản bài tập." : "Đã lưu bản nháp.");
    } catch (e) { setError(errorText(e)); } finally { setBusy(false); }
  }
  function questionField(index, key, value) {
    setExercise(current => ({ ...current, questions: current.questions.map((q, i) => i === index ? { ...q, [key]: value } : q) }));
  }
  function reorderQuestion(index, direction) {
    const next = [...exercise.questions]; const other = index + direction; if (other < 0 || other >= next.length) return;
    [next[index], next[other]] = [next[other], next[index]]; setExercise({ ...exercise, questions: next });
    editVersion.current++; setEditorDirty(true);
  }
  async function moveResource(index, direction) {
    if (!lecture || busy) return; const next = [...lecture.resources]; const other = index + direction;
    if (other < 0 || other >= next.length) return; [next[index], next[other]] = [next[other], next[index]];
    await perform(async () => { await api.orderResources(lecture.id, next.map(item => item.id), token);
      setLecture(await api.lecture(lecture.id, token)); }, "Đã sắp xếp tệp.");
  }
  async function downloadResource(resource) {
    try { const blob = await api.resourceBlob(lecture.id, resource.id, token); const url = URL.createObjectURL(blob);
      const link = document.createElement("a"); link.href = url; link.download = resource.name;
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch (e) { setError(errorText(e)); }
  }
  async function replaceResource(resource, file) {
    if (!file || !lecture) return; const kind = kindFromFile(file, capabilities.excelEnabled);
    if (!kind || file.size > (kind === "VIDEO" ? capabilities.maxVideoBytes : capabilities.maxFileBytes)) {
      setUploadProgress({ name: file.name, percent: 0, phase: "error", replaceId: resource.id });
      setUploadError("Tệp không đúng định dạng hoặc vượt giới hạn dung lượng."); return;
    }
    setBusy(true); setError(""); setNotice(""); setUploadError(null); setLastSavedId(null);
    let newResourceId = null;
    try { const body = new FormData(); body.set("name", file.name); body.set("kind", kind); body.set("file", file);
      setUploadProgress({ name: file.name, percent: 0, phase: "upload", replaceId: resource.id });
      const uploaded = await api.uploadResource(lecture.id, null, body, token,
        (percent, phase = "upload") => setUploadProgress({ name: file.name, percent, phase, replaceId: resource.id }));
      newResourceId = uploaded.id;
      setUploadProgress({ name: file.name, percent: 100, phase: "saving", replaceId: resource.id });
      await api.archiveResource(lecture.id, resource.id, true, token);
      setLecture(await api.lecture(lecture.id, token)); await refresh(); setLastSavedId(newResourceId);
      setUploadProgress({ name: file.name, percent: 100, phase: "saved", replaceId: resource.id });
      setNotice("Đã thay tệp; bản cũ được lưu trữ để bảo vệ lịch sử."); }
    catch (e) { setUploadError(newResourceId ?
        `Tệp mới đã lưu nhưng chưa hoàn tất lưu trữ tệp cũ hoặc làm mới danh sách: ${errorText(e)}. Tải lại danh sách để kiểm tra; không tải lặp.` :
        `${errorText(e)} Tải lại danh sách tệp để kiểm tra trạng thái trước khi thử lại.`);
      try { setLecture(await api.lecture(lecture.id, token)); } catch { /* giữ lỗi gốc */ } }
    finally { setBusy(false); }
  }
  async function showPreview(steps, title) {
    setPreviewIndex(0); setPreview({ loading: true, title });
    try { setPreview({ title, items: await Promise.all(steps.map(step => api.previewStep(courseId, step.id, token))) }); }
    catch (e) { setPreview({ error: errorText(e) }); }
  }
  const query = search.trim().toLocaleLowerCase("vi-VN");
  const displayUnits = outline?.units.filter(u => studioTab === "archived" ?
    u.isArchived || u.lessons.some(l => l.isArchived || l.steps.some(s => s.isArchived)) : !u.isArchived)
    .map(u => ({ ...u, lessons: u.lessons.filter(l => studioTab === "archived" ?
      u.isArchived || l.isArchived || l.steps.some(s => s.isArchived) : !l.isArchived)
      .map(l => ({ ...l, steps: l.steps.filter(s => studioTab === "archived" ?
        u.isArchived || l.isArchived || s.isArchived : !s.isArchived &&
          (studioTab === "exercises" ? s.activityType === "EXERCISE" :
            studioTab === "resources" ? s.activityType === "LECTURE" : true)) })) }))
    .filter(u => !query || u.name.toLocaleLowerCase("vi-VN").includes(query) ||
      u.lessons.some(l => l.name.toLocaleLowerCase("vi-VN").includes(query) || l.steps.some(s => s.title.toLocaleLowerCase("vi-VN").includes(query)))) || [];
  return <div className="admin-page builder-page"><div className="studio-breadcrumb"><Link to="/admin/courses">Khóa học</Link><span>›</span><span>{outline?.course.name || "Nội dung khóa học"}</span><span>›</span><strong>Nội dung khóa học</strong></div>
    {outline && <div className="studio-course-hero"><div className="studio-cover"><CourseCover course={outline.course}/></div><div className="studio-course-info"><div className="studio-title-line"><h1>{outline.course.name}</h1><span className="studio-status">{outline.course.status === "DANG_MO" ? "Đang mở" : "Đã đóng"}</span></div><p>{outline.course.description || "Nội dung và lộ trình học của khóa học."}</p><div className="studio-meta"><span>{outline.units.filter(u => !u.isArchived).length} Unit</span><span>{outline.units.reduce((sum, u) => sum + u.lessons.filter(l => !l.isArchived).length, 0)} Lesson</span><span>{outline.units.reduce((sum, u) => sum + u.lessons.reduce((n, l) => n + l.steps.filter(s => !s.isArchived).length, 0), 0)} hoạt động</span></div></div><button className="studio-hero-preview" onClick={() => showPreview(outline.units.filter(u => !u.isArchived).flatMap(u => u.lessons.filter(l => !l.isArchived).flatMap(s => s.steps.filter(step => !step.isArchived))), outline.course.name)}>◎ Xem như học viên</button></div>}
    <AdminSectionStats endpoint={`lectures?courseId=${courseId}`} title="Thống kê bài giảng và tài nguyên" revision={statsRevision} />
    <nav className="studio-tabs" aria-label="Nội dung khóa học">{[["curriculum","Chương trình học"],["resources","Thư viện tài nguyên"],["exercises","Bài tập"],["progress","Tiến độ học viên"],["archived","Đã lưu trữ"]].map(([key,label]) => <button key={key} className={studioTab === key ? "is-active" : ""} onClick={() => { if ((!editorDirty && !files.length) || window.confirm("Bạn có thông tin hoặc tệp chưa lưu. Chuyển mục?")) setStudioTab(key); }}>{label}</button>)}</nav>
    {error && <p className="admin-feedback is-error" role="alert">{error}</p>}
    {notice && !error && <p className="admin-feedback is-success" role="status">{notice}</p>}
    {studioTab === "progress" && progressSummary && <div className="studio-progress-summary"><div><strong>{progressSummary.enrolled}</strong><span>Học viên đang đăng ký</span></div><div><strong>{progressSummary.learnersStarted}</strong><span>Học viên đã bắt đầu</span></div><div><strong>{progressSummary.completedActivityRecords}</strong><span>Lượt hoàn thành hoạt động</span></div><div><strong>{progressSummary.activeActivities}</strong><span>Hoạt động đang hiển thị</span></div></div>}
    {loading ? <p role="status">Đang tải…</p> : outline && <div className="course-builder">
      <section className="admin-panel builder-tree"><header><div><h2>{studioTab === "archived" ? "Nội dung đã lưu trữ" : studioTab === "resources" ? "Nội dung có tài nguyên" : studioTab === "exercises" ? "Bài tập trong khóa học" : studioTab === "progress" ? "Tiến độ học viên" : "Chương trình học"}</h2><p>Quản lý cấu trúc khóa học, thêm, sửa, sắp xếp và xem trước.</p></div><div className="studio-tree-tools"><input type="search" aria-label="Tìm kiếm nội dung" placeholder="Tìm kiếm Lesson, hoạt động..." value={search} onChange={e => setSearch(e.target.value)}/><button className="admin-button" onClick={addUnit}>+ Thêm Unit</button></div></header>
        {!displayUnits.length && <p className="studio-empty">Không có nội dung phù hợp.</p>}
        {displayUnits.map((u, ui) => <div className="builder-unit" key={u.id}>
          <div className="builder-row builder-unit-row"><button aria-label="Mở hoặc thu gọn Unit" onClick={() => expand(`u${u.id}`)}>{expanded[`u${u.id}`] ? "▾" : "▸"}</button>
            <button className={selected?.unitId === u.id ? "is-selected" : ""} onClick={() => { select(u.id); setExpanded(x => ({ ...x, [`u${u.id}`]: true })); }}><small>UNIT {String(ui + 1).padStart(2, "0")}</small><strong>{u.name}</strong></button>
            <button type="button" onClick={() => showPreview(u.lessons.flatMap(l => l.steps), u.name)}>Xem trước</button>
            {actions("unit", u, outline.units, outline.units.findIndex(x => x.id === u.id))}</div>
          {expanded[`u${u.id}`] && <div className="builder-children"><button className="builder-add" onClick={() => addLesson(u.id)}>+ Lesson</button>
            {u.lessons.map((l, li) => <div key={l.id}><div className="builder-row builder-lesson-row"><button aria-label="Mở hoặc thu gọn Lesson" onClick={() => expand(`l${l.id}`)}>{expanded[`l${l.id}`] ? "▾" : "▸"}</button>
              <button className={selected?.lessonId === l.id ? "is-selected" : ""} onClick={() => { select(u.id, l.id); setExpanded(x => ({ ...x, [`l${l.id}`]: true })); }}><small>BÀI {String(li + 1).padStart(2, "0")}</small><strong>{l.name}</strong></button>
              <button type="button" onClick={() => showPreview(l.steps, l.name)}>Xem trước</button>
              {actions("lesson", l, outline.units.find(x => x.id === u.id).lessons, outline.units.find(x => x.id === u.id).lessons.findIndex(x => x.id === l.id), u.id)}</div>
              {expanded[`l${l.id}`] && <div className="builder-children"><button className="builder-add" onClick={() => select(u.id, l.id)}>+ Hoạt động</button>
                {l.steps.map((s, si) => <div className="builder-row" key={s.id}><button className={selected?.stepId === s.id ? "is-selected" : ""} onClick={() => select(u.id, l.id, s)}>
                  <span className={`studio-activity-icon ${s.activityType.toLowerCase()}`} aria-hidden="true">{s.activityType === "EXERCISE" ? "▤" : s.activityType === "VOCABULARY" ? "◇" : "▶"}</span> {s.title}</button>
                  {actions("step", s, outline.units.find(x => x.id === u.id).lessons.find(x => x.id === l.id).steps, outline.units.find(x => x.id === u.id).lessons.find(x => x.id === l.id).steps.findIndex(x => x.id === s.id), u.id, l.id)}</div>)}</div>}</div>)}</div>}</div>)}</section>
      <aside className="admin-panel builder-editor">
        <div className="builder-editor-body">
        {!lesson ? <div className="builder-context"><span className="builder-eyebrow">{unit ? "UNIT ĐANG CHỌN" : "BẮT ĐẦU"}</span><h2>{unit?.name || "Xây dựng lộ trình học"}</h2><p>{unit?.description || (unit ? "Tạo Lesson để bắt đầu sắp xếp nội dung trong Unit này." : "Tạo Unit đầu tiên, sau đó thêm Lesson và hoạt động học.")}</p><div className="builder-context-actions">{unit ? <><button className="admin-button" onClick={() => addLesson(unit.id)}>+ Thêm Lesson</button><button onClick={() => showPreview(unit.lessons.flatMap(l => l.steps), unit.name)}>Xem trước như học viên</button></> : <button className="admin-button" onClick={addUnit}>+ Tạo Unit</button>}</div></div> : <>
          <header><div><span className="builder-eyebrow">{unit.name} / {lesson.name}</span><h2>{activity ? "Chỉnh sửa hoạt động" : "Thêm hoạt động mới"}</h2></div><button onClick={() => showPreview(activity ? [activity] : lesson.steps, activity ? activity.title : lesson.name)}>Xem trước</button></header>
          {activity && <nav className="studio-editor-tabs" aria-label="Chỉnh sửa hoạt động"><button type="button" className={editorTab === "info" ? "is-active" : ""} onClick={() => { setEditorTab("info"); document.querySelector('.builder-editor-body')?.scrollTo({ top: 0, behavior: 'smooth' }); }}>Thông tin</button><button type="button" className={editorTab === "resources" ? "is-active" : ""} onClick={() => { setEditorTab("resources"); document.querySelector('.builder-editor-body .builder-resources, .builder-editor-body input[type=file]')?.scrollIntoView({ behavior: 'smooth', block: 'center' }); }}>Tài nguyên</button><button type="button" onClick={() => showPreview([activity], activity.title)}>Xem trước</button></nav>}
          {!activity && <div className="builder-type"><button className={mode === "LECTURE" ? "is-selected" : ""} onClick={() => setMode("LECTURE")}>Nội dung học</button>
            <button className={mode === "EXERCISE" ? "is-selected" : ""} onClick={() => setMode("EXERCISE")}>Bài tập</button></div>}
          {detailLoading && <p role="status">Đang tải nội dung hoạt động…</p>}
          {detailLoadFailed && <p role="alert">Chưa tải được nội dung hoạt động. <button type="button" onClick={() => select(unit.id, lesson.id, activity, true)}>Thử lại</button></p>}
          {!detailLoading && !detailLoadFailed && (activity?.activityType === "LECTURE" || !activity && mode === "LECTURE") && <form className="builder-form" onSubmit={saveContent} onChange={() => { editVersion.current++; }}>
            {activity && <label>Loại hoạt động<select value={lecture?.resources?.some(r => !r.isArchived && r.kind === "VIDEO") ? "Video" : "Bài giảng"} disabled><option>Video</option><option>Bài giảng</option></select></label>}
            <label>Tiêu đề<input required maxLength={128} value={content.title} onChange={e => { setContent({ ...content, title: e.target.value }); setEditorDirty(true); }}/></label>
            <label>Mô tả<textarea rows={3} maxLength={2000} value={content.summary} onChange={e => { setContent({ ...content, summary: e.target.value }); setEditorDirty(true); }}/></label>
            <label>Nội dung (không bắt buộc nếu có tệp)<textarea rows={7} maxLength={100000} value={content.content} onChange={e => { setContent({ ...content, content: e.target.value }); setEditorDirty(true); }}/></label>
            {!lecture && <p className="content-empty">Lưu thông tin hoạt động trước, sau đó chọn và tải video.</p>}
            {lecture && <label>Thêm tệp<input type="file" multiple disabled={busy} accept={capabilities.excelEnabled ? ".mp4,.mp3,.wav,.pdf,.doc,.docx,.ppt,.pptx,.jpg,.jpeg,.png,.webp" : ".mp4,.mp3,.wav,.pdf,.doc,.docx,.ppt,.pptx,.jpg,.jpeg,.png,.webp"} onChange={e => { chooseFiles(e.target.files || []); e.target.value = ""; }}/></label>}
            {!!files.length && <ul>{files.map((x, i) => <li key={i}>{x.file.name} · {x.kind} · Chưa tải lên</li>)}</ul>}
            <small>Video MP4 tối đa 950 MB, tải theo từng phần. Các tệp khác tối đa {(capabilities.maxFileBytes / 1000000).toFixed(0)} MB.</small>
            {lecture && <button type="button" className="admin-button" disabled={busy || !files.length} onClick={uploadSelectedFiles}>{busy && uploadProgress ? "Đang tải tệp…" : `Tải ${files.length || ""} tệp đã chọn`}</button>}
            {uploadProgress && !uploadProgress.replaceId && <UploadStatus progress={uploadProgress} />}
            {uploadError && !uploadProgress?.replaceId && <p role="alert" className="builder-upload-error">{uploadError} <button type="button" disabled={busy} onClick={async () => {
              try { setLecture(await api.lecture(lecture.id, token)); await refresh(); setUploadError(null); }
              catch (e) { setUploadError(errorText(e)); }
            }}>Tải lại danh sách đã lưu</button></p>}
            {lecture && <div className="builder-resources"><h3>Tệp đã lưu</h3>{lecture.resources.map((r, index) => <div key={r.id} data-resource-id={r.id}><span>{r.name} · {r.kind}{r.isArchived ? " · Đã lưu trữ" : " · Đã lưu"}</span>
              {lastSavedId === r.id && <small className="builder-saved-badge">Vừa lưu thành công</small>}
              <button type="button" disabled={index === 0 || busy} aria-label={`Đưa ${r.name} lên`} onClick={() => moveResource(index, -1)}>↑</button>
              <button type="button" disabled={index === lecture.resources.length - 1 || busy} aria-label={`Đưa ${r.name} xuống`} onClick={() => moveResource(index, 1)}>↓</button>
              <button type="button" onClick={async () => { try { if (r.kind === "VIDEO") { setPreviewResourceId(current => current === r.id ? null : r.id); return; } const blob = await api.resourceBlob(lecture.id, r.id, token); const url = URL.createObjectURL(blob); window.open(url, "_blank", "noopener,noreferrer"); setTimeout(() => URL.revokeObjectURL(url), 60000); } catch (e) { setError(errorText(e)); } }}>{previewResourceId === r.id ? "Đóng" : "Mở"}</button>
              <button type="button" onClick={() => downloadResource(r)}>Tải xuống</button>
              {!r.isArchived && <label className="builder-replace-file">Thay tệp<input type="file" disabled={busy} accept=".mp4,.mp3,.wav,.pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.jpg,.jpeg,.png,.webp" onChange={e => { const file = e.target.files?.[0]; e.target.value = ""; replaceResource(r, file); }}/></label>}
              {uploadProgress?.replaceId === r.id && <UploadStatus progress={uploadProgress} />}
              {uploadError && uploadProgress?.replaceId === r.id && <p role="alert" className="builder-upload-error">{uploadError}
                <button type="button" disabled={busy} onClick={async () => { try { setLecture(await api.lecture(lecture.id, token)); await refresh(); setUploadError(null); }
                  catch (e) { setUploadError(errorText(e)); } }}>Tải lại danh sách</button></p>}
              <button type="button" onClick={() => perform(async () => { await api.archiveResource(lecture.id, r.id, !r.isArchived, token); setLecture(await api.lecture(lecture.id, token)); }, r.isArchived ? "Đã khôi phục tệp." : "Đã lưu trữ tệp.")}>{r.isArchived ? "Khôi phục" : "Lưu trữ"}</button>
              <button type="button" onClick={() => removeResource(r)}>Xóa</button>
              {r.kind === "VIDEO" && previewResourceId === r.id && <PreviewResource lectureId={lecture.id} resource={r} token={token} />}</div>)}</div>}
          </form>}
          {!detailLoading && !detailLoadFailed && (activity?.activityType === "EXERCISE" || !activity && mode === "EXERCISE") && <form className="builder-form" onChange={() => { editVersion.current++; setEditorDirty(true); }} onSubmit={e => saveExercise(e, false)}>
            <label>Tiêu đề<input required maxLength={128} value={exercise.title} onChange={e => setExercise({ ...exercise, title: e.target.value })}/></label>
            {exercise.questions.map((q, i) => <fieldset key={i}><legend>Câu {i + 1}</legend>
              <label>Câu hỏi<textarea required value={q.content} onChange={e => questionField(i, "content", e.target.value)}/></label>
              <label>Ngữ liệu (tùy chọn)<textarea value={q.passage || ""} onChange={e => questionField(i, "passage", e.target.value)}/></label>
              {["a", "b", "c", "d"].map(key => <label key={key}>Đáp án {key.toUpperCase()}<input required value={q[key]} onChange={e => questionField(i, key, e.target.value)}/></label>)}
              <label>Đáp án đúng<select value={q.correct} onChange={e => questionField(i, "correct", e.target.value)}>{["A", "B", "C", "D"].map(x => <option key={x}>{x}</option>)}</select></label>
              <label>Giải thích<textarea value={q.explanation || ""} onChange={e => questionField(i, "explanation", e.target.value)}/></label>
              <div className="builder-actions"><button type="button" disabled={i === 0} onClick={() => reorderQuestion(i, -1)}>↑</button>
                <button type="button" disabled={i === exercise.questions.length - 1} onClick={() => reorderQuestion(i, 1)}>↓</button>
                <button type="button" disabled={exercise.questions.length === 1} onClick={() => { setExercise({ ...exercise, questions: exercise.questions.filter((_, index) => index !== i) }); editVersion.current++; setEditorDirty(true); }}>Xóa câu</button></div>
            </fieldset>)}
            <button type="button" onClick={() => { setExercise({ ...exercise, questions: [...exercise.questions, emptyQuestion()] }); editVersion.current++; setEditorDirty(true); }}>+ Câu hỏi</button>
            <div className="builder-save"><button disabled={busy} type="submit">Lưu nháp</button>
              <button disabled={busy} className="admin-button" type="button" onClick={e => { if (e.currentTarget.form?.reportValidity()) saveExercise(e, true); }}>Xuất bản</button></div>
          </form>}
          {activity?.activityType === "VOCABULARY" && <p>Chủ đề từ vựng hiện có. Dùng Xem trước để kiểm tra nội dung.</p>}
        </>}
        </div>
        {lesson && <div className="studio-editor-actions"><button type="button" disabled={busy} onClick={() => { if ((editorDirty || files.length) && !window.confirm("Bạn có thông tin hoặc tệp chưa lưu. Hủy thay đổi?")) return; activity ? select(unit.id, lesson.id, activity, true) : (resetEditor(), setSelected({ unitId: unit.id, lessonId: lesson.id })); }}>Hủy</button>{!detailLoading && !detailLoadFailed && (activity?.activityType === "LECTURE" || !activity && mode === "LECTURE") && <button type="button" className="is-primary" disabled={busy} onClick={() => document.querySelector('.builder-editor .builder-form')?.requestSubmit()}>{copyMode ? "Lưu hoạt động mới" : "Lưu thông tin"}</button>}{activity && <><button type="button" onClick={() => archive("step", activity, unit.id, lesson.id, !activity.isArchived)}>{activity.isArchived ? "Khôi phục" : "Lưu trữ"}</button><button type="button" className="is-danger" onClick={() => remove("step", activity, unit.id, lesson.id)}>Xóa hoạt động</button></>}</div>}
      </aside></div>}
    {deleteDialog && <div className="admin-modal-backdrop" onMouseDown={e => e.target === e.currentTarget && setDeleteDialog(null)}><section className="admin-modal builder-name-modal" role="alertdialog" aria-modal="true" aria-label="Xác nhận xóa vĩnh viễn">
      <span className="builder-eyebrow">XÓA VĨNH VIỄN</span><h2>{deleteDialog.item.name || deleteDialog.item.title}</h2>
      <p>Phạm vi xóa: {deleteDialog.type === "unit" ? "Unit và toàn bộ Lesson, hoạt động bên trong" : deleteDialog.type === "lesson" ? "Lesson và toàn bộ hoạt động bên trong" : deleteDialog.type === "resource" ? "tệp được chọn; tiến độ học liên quan được giữ nguyên" : "hoạt động được chọn"}.</p>
      <div className="builder-impact"><span>{deleteDialog.impact.affectedLearners ?? 0} học viên bị ảnh hưởng</span><span>{deleteDialog.impact.progressRecords} bản ghi tiến độ</span><span>{deleteDialog.impact.attempts} lượt làm bài</span><span>{deleteDialog.impact.checks} lần kiểm tra câu hỏi</span></div>
      <p>Thao tác này không thể hoàn tác. Tài khoản, đăng ký, thanh toán, bài thi và các khóa học khác không bị xóa.</p>
      <div className="builder-modal-actions"><button onClick={() => setDeleteDialog(null)}>Hủy</button><button onClick={async () => { if (deleteDialog.type === "resource") { await perform(async () => { await api.archiveResource(deleteDialog.lectureId, deleteDialog.item.id, true, token); setLecture(await api.lecture(deleteDialog.lectureId, token)); }, "Đã lưu trữ tệp."); } else await archive(deleteDialog.type, deleteDialog.item, deleteDialog.unitId, deleteDialog.lessonId); setDeleteDialog(null); }}>Lưu trữ an toàn</button><button className="builder-danger-button" disabled={busy} onClick={confirmDelete}>Xóa vĩnh viễn</button></div>
    </section></div>}
    {nameDialog && <div className="admin-modal-backdrop" onMouseDown={e => e.target === e.currentTarget && setNameDialog(null)}><form className="admin-modal builder-name-modal" role="dialog" aria-modal="true" onSubmit={saveName}>
      <span className="builder-eyebrow">{nameDialog.item ? "CHỈNH SỬA" : "TẠO MỚI"}</span><h2>{nameDialog.type === "unit" ? "Unit" : "Lesson"}</h2>
      <label>Tên hiển thị<input autoFocus required maxLength={128} value={nameDialog.name} onChange={e => setNameDialog(x => ({ ...x, name: e.target.value }))}/></label>
      <label>Mô tả ngắn<textarea rows={3} maxLength={512} value={nameDialog.description} onChange={e => setNameDialog(x => ({ ...x, description: e.target.value }))}/></label>
      <div className="builder-modal-actions"><button type="button" onClick={() => setNameDialog(null)}>Hủy</button><button className="admin-button" disabled={busy}>{busy ? "Đang lưu…" : "Lưu"}</button></div>
    </form></div>}
    {preview && <div className="admin-modal-backdrop" onMouseDown={e => e.target === e.currentTarget && setPreview(null)}>
      <section className="admin-modal content-preview-modal builder-learner-preview" role="dialog" aria-modal="true"><div className="builder-preview-head"><span className="builder-eyebrow">CHẾ ĐỘ XEM TRƯỚC · KHÔNG GHI TIẾN ĐỘ</span><h2>Xem trước như học viên</h2><p>{preview.title}</p></div>
        {preview.loading ? <p>Đang tải…</p> : preview.error ? <p role="alert">{preview.error}</p> :
          <>{!preview.items.length && <p>Chưa có hoạt động.</p>}
            {!!preview.items.length && <nav className="builder-preview-nav" aria-label="Điều hướng hoạt động">{preview.items.map((item, index) => <button key={index} className={index === previewIndex ? "is-active" : ""} onClick={() => setPreviewIndex(index)}>{index + 1}. {item.title}</button>)}</nav>}
            {preview.items.filter((_, index) => index === previewIndex).map((item, index) => <div className="builder-preview-body" key={index}><span className="builder-eyebrow">{item.type === "EXERCISE" ? "BÀI TẬP" : "NỘI DUNG HỌC"} · {previewIndex + 1}/{preview.items.length}</span><h4>{item.title}</h4>{item.type === "LECTURE" ? <>
              <p>{item.lecture.summary}</p><div className="course-step-text">{item.lecture.content}</div>
              {item.lecture.resources.map(r => <PreviewResource key={r.id} lectureId={item.lecture.id} resource={r} token={token}/>)}</> :
              item.type === "EXERCISE" ? <PreviewExercise key={item.title} questions={item.questions}/> :
                item.topic.words.map((w, i) => <p key={i}>{w.word} — {w.meaning}</p>)}</div>)}
            {preview.items.length > 1 && <div className="builder-preview-controls"><button disabled={previewIndex === 0} onClick={() => setPreviewIndex(i => i - 1)}>← Hoạt động trước</button><button disabled={previewIndex === preview.items.length - 1} onClick={() => setPreviewIndex(i => i + 1)}>Hoạt động tiếp →</button></div>}</>}
        <button className="admin-button" onClick={() => setPreview(null)}>Đóng</button></section></div>}
  </div>;
}
