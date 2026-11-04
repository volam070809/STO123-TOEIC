import { useCallback, useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import ExamQuestionForm from "./ExamQuestionForm";
import ExamQuestionEdit from "./ExamQuestionEdit";
import ExamGroupEdit from "./ExamGroupEdit";
import QuestionContentPreview from "./QuestionContentPreview";
import AdminSectionStats from "../../components/admin/AdminSectionStats";
import "../../styles/admin-exams.css";
import "../../styles/admin-question-demo.css";

const api = "/api/admin/content";

export default function AdminQuestionBankPage({ bank }) {
  const { token } = useAuth();
  const { questionId } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const base = `/admin/content/questions/${bank === "DE_THI" ? "exams" : "practice"}`;
  const title = bank === "DE_THI" ? "Câu hỏi đề thi" : "Câu hỏi luyện tập";
  const adding = location.pathname.endsWith("/new");
  const [part, setPart] = useState("");
  const [search, setSearch] = useState("");
  const [scope, setScope] = useState("all");
  const [completeness, setCompleteness] = useState("all");
  const [page, setPage] = useState(1);
  const [list, setList] = useState({ items: [], total: 0 });
  const [detail, setDetail] = useState(null);
  const [editingQuestion, setEditingQuestion] = useState(null);
  const [groupEditing, setGroupEditing] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);
  const reload = useCallback(() => setRevision(value => value + 1), []);

  useEffect(() => {
    if (adding || questionId) return undefined;
    let live = true;
    setPending(true); setError("");
    apiRequest(`${api}/questions/units?${new URLSearchParams({ bank, scope, completeness,
      ...(part && { part }), search, page: String(page) })}`, { token, timeoutMs: 90_000 })
      .then(data => { if (live) setList(data); })
      .catch(e => { if (live) setError(e.message); })
      .finally(() => { if (live) setPending(false); });
    return () => { live = false; };
  }, [adding, questionId, bank, part, search, scope, completeness, page, revision, token]);

  useEffect(() => {
    if (!questionId) return undefined;
    let live = true;
    setPending(true); setError(""); setDetail(null);
    apiRequest(`${api}/questions/units/${questionId}`, { token })
      .then(data => {
        if (!live) return;
        if (data.bank !== bank) { setError("Câu hỏi không thuộc ngân hàng này."); return; }
        setDetail(data);
      })
      .catch(e => { if (live) setError(e.message); })
      .finally(() => { if (live) setPending(false); });
    return () => { live = false; };
  }, [questionId, bank, revision, token]);

  useEffect(() => {
    const editor = location.state?.openEditor;
    if (!detail || !editor || !questionId) return;
    if (editor === "group" && detail.groupId) setGroupEditing(true);
    else if (detail.questions.some(question => question.id === Number(editor)))
      setEditingQuestion(Number(editor));
    navigate(location.pathname, { replace: true, state: null });
  }, [detail, questionId, location.pathname, location.state, navigate]);

  async function deleteSource() {
    if (!detail || pending) return;
    const id = detail.questions[0].id;
    setError("");
    try {
      if (detail.groupId) {
        if (!window.confirm(`Xóa vĩnh viễn nhóm ${detail.groupId} và ${detail.questions.length} câu nguồn? Nội dung đề đã xuất bản và lịch sử học viên được giữ.`)) return;
        setPending(true);
        await apiRequest(`${api}/groups/${detail.groupId}`, { method: "DELETE", token });
      } else {
        const impact = await apiRequest(`${api}/questions/${id}/dependencies`, { token });
        if (!impact.canDelete) {
          setError(impact.examIds.length ? "Câu còn trong đề hiện hành. Hãy gỡ hoặc thay trong đề rồi xuất bản trước khi xóa nguồn." :
            "Câu còn được lượt thi cũ tham chiếu nên chưa thể xóa an toàn.");
          return;
        }
        if (!window.confirm(`Xóa vĩnh viễn câu ${id} khỏi ngân hàng nguồn?`)) return;
        setPending(true);
        await apiRequest(`${api}/questions/${id}`, { method: "DELETE", token });
      }
      navigate(base, { state: { notice: "Đã xóa câu hỏi nguồn. Nội dung đề đã xuất bản và lịch sử học viên được giữ." } });
      reload();
    } catch (e) { setError(e.message); } finally { setPending(false); }
  }

  const noticeText = notice || location.state?.notice;
  return <div className="admin-page admin-question-bank">
    <nav className="exam-breadcrumb" aria-label="Đường dẫn"><Link to="/admin/content/questions">Ngân hàng câu hỏi</Link>{adding && <span>› {title} › Thêm câu hỏi</span>}{questionId && <span>› {title} › Chi tiết</span>}</nav>
    <div className="admin-page-header"><div><h1>{adding ? `Thêm ${title.toLowerCase()}` : detail ?
      `${detail.groupId ? `Nhóm ${detail.groupId}` : `Câu ${detail.questions[0]?.id}`} · Part ${detail.part}` : "Ngân hàng câu hỏi"}</h1>
      <p>{adding ? "Tạo trực tiếp theo Part; câu và nhóm được lưu vào đúng ngân hàng." : detail ?
        `${detail.questions.length} câu · Xem đáp án, ngữ liệu và sửa ngay tại đây.` :
        "Nhóm câu hiển thị một mục cùng ngữ liệu và các câu thành viên."}</p></div>
      {adding || questionId ? <Link to={base} className="admin-button admin-button-secondary">← Danh sách</Link> :
        <Link to={`${base}/new`} className="admin-button">Thêm câu hỏi</Link>}</div>
    {noticeText && <p role="status" className="exam-alert exam-alert-success">{noticeText}</p>}
    {error && <p role="alert" className="exam-alert exam-alert-error">{error} <button type="button" onClick={reload}>Thử lại</button></p>}
    <AdminSectionStats endpoint={`questions?bank=${bank}`} title={`Thống kê ${title.toLowerCase()}`} revision={revision} questionIds={detail?.questions?.map(question => question.id)} />
    {adding ? <ExamQuestionForm token={token} inline fixedBank={bank}
      onClose={() => navigate(base)} onSaved={result => navigate(`${base}/detail/${result.questionIds[0]}`,
        { state: { notice: "Đã lưu câu hỏi vào ngân hàng." } })} /> : questionId ? <>
      {pending && <p role="status">Đang tải chi tiết…</p>}
      {detail && <div className="question-bank-detail-layout"><main className="admin-panel question-detail">
        {editingQuestion ? <ExamQuestionEdit key={editingQuestion} inline id={editingQuestion} token={token}
          onClose={nextId => setEditingQuestion(nextId || null)}
          onSaved={() => { setEditingQuestion(null); setNotice("Đã lưu câu nguồn và cập nhật đề hiện hành liên quan. Lượt thi đã bắt đầu giữ nguyên nội dung cũ."); reload(); }} /> :
          groupEditing ? <ExamGroupEdit unit={detail} token={token} onCancel={() => setGroupEditing(false)}
            onSaved={() => { setGroupEditing(false); setNotice("Đã lưu ngữ liệu nguồn và cập nhật đề hiện hành liên quan. Lượt thi đã bắt đầu giữ nguyên nội dung cũ."); reload(); }} /> : <>
            <div className="exam-editor-header"><div><h2>Nội dung {detail.groupId ? "nhóm câu" : "câu hỏi"}</h2><p>Đáp án đúng và giải thích được hiển thị bên dưới.</p></div>
              {detail.groupId && <button type="button" className="admin-button admin-button-secondary" onClick={() => setGroupEditing(true)}>Sửa ngữ liệu chung</button>}</div>
            <QuestionContentPreview unit={detail} token={token} onEditQuestion={setEditingQuestion} />
          </>}
      </main><aside className="admin-panel question-bank-usage"><h2>Đang thuộc đề thi</h2>
        {detail.exams?.length ? detail.exams.map(exam => <p key={exam.examId}><strong>{exam.name}</strong><br />#{exam.examId} · {exam.current ? "Đang sử dụng" : "Chưa xuất bản"}</p>) :
          <p>{bank === "DE_THI" ? "Chưa thuộc đề hiện hành." : "Câu hỏi luyện tập không thuộc đề thi."}</p>}
        {detail.exams?.some(exam => exam.current) && <p className="exam-edit-impact">Khi lưu, hệ thống cập nhật đề hiện hành liên quan. Lượt thi đã bắt đầu và lịch sử vẫn dùng phiên bản cũ.</p>}
        <button type="button" className="admin-button admin-button-secondary" disabled={pending || !!editingQuestion || groupEditing}
          onClick={deleteSource}>Xóa câu hỏi nguồn</button></aside></div>}
    </> : <><nav className="exam-tabs question-bank-tabs" aria-label="Loại câu hỏi"><Link className={bank === "DE_THI" ? "is-active" : ""} to="/admin/content/questions/exams">Câu hỏi đề thi</Link>
      <Link className={bank === "LUYEN_TAP" ? "is-active" : ""} to="/admin/content/questions/practice">Câu hỏi luyện tập</Link></nav>
    <section className="admin-panel question-bank-list">
      <div className="exam-filter-row"><input aria-label="Tìm câu hỏi" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} placeholder="Tìm mã câu, mã nhóm, nội dung hoặc đề thi" />
        <select aria-label="Loại câu hỏi" value={scope} onChange={event => { setScope(event.target.value); setPage(1); }}><option value="all">Tất cả câu hỏi</option><option value="groups">Nhóm câu</option><option value="single">Câu riêng</option></select>
        <select aria-label="Lọc Part" value={part} onChange={event => { setPart(event.target.value); setPage(1); }}><option value="">Tất cả Part</option>{Array.from({ length: 7 }, (_, i) => <option key={i + 1} value={i + 1}>Part {i + 1}</option>)}</select>
        <select aria-label="Mức độ đầy đủ" value={completeness} onChange={event => { setCompleteness(event.target.value); setPage(1); }}><option value="all">Tất cả nội dung</option><option value="complete">Đã đủ nội dung</option><option value="incomplete">Thiếu nội dung</option></select></div>
      {pending && <p role="status">Đang tải câu hỏi…</p>}
      <div className="question-bank-count">{list.total} nhóm hoặc câu riêng · Nhóm chỉ hiển thị một lần</div>
      <div className="question-bank-table"><div className="question-bank-list-head"><span>Câu hỏi / Nhóm câu</span><span>Đang thuộc đề thi</span><span>Thao tác</span></div>
        {list.items.map(unit => <article className="question-bank-row" key={unit.groupId ? `g${unit.groupId}` : `q${unit.questionIds[0]}`}>
        <div><strong>{unit.isGroup ? `Nhóm ${unit.groupId}` : `Câu ${unit.questionIds[0]}`}</strong> <span className="exam-pill">Part {unit.part}</span> <span className="exam-pill">{unit.questionCount} câu</span>
          <p>{(unit.preview || "Media câu hỏi").slice(0, 160)}</p>
          <small>Mã câu: {unit.questionIds.join(", ")}</small>
          {unit.missingFields?.length > 0 && <p className="question-bank-missing" role="status"><strong>Thiếu nội dung:</strong> {unit.missingFields.join(" · ")}{" "}
            <Link to={`${base}/detail/${unit.questionIds[0]}`} state={{ openEditor:
              Number(unit.missingFields[0].match(/^Câu (\d+):/)?.[1]) || "group" }}>Sửa ngay</Link></p>}</div>
        <div className="question-bank-related">{bank === "DE_THI" && unit.exams?.length ?
          unit.exams.map(exam => <span key={exam.examId}><strong>{exam.name}</strong><small>#{exam.examId} · {exam.current ? "Đang sử dụng" : "Chưa xuất bản"}</small></span>) :
          <span>{bank === "DE_THI" ? "Chưa thuộc đề thi" : "Ngân hàng luyện tập"}</span>}</div>
        <Link className="admin-button admin-button-secondary" to={`${base}/detail/${unit.questionIds[0]}`}>Xem chi tiết</Link>
      </article>)}{!pending && !error && list.items.length === 0 && <p className="question-bank-empty">Chưa có câu hỏi phù hợp. Chọn “Thêm câu hỏi” để tạo trực tiếp.</p>}</div>
      <div className="exam-pagination"><button type="button" disabled={page <= 1 || pending} onClick={() => setPage(value => value - 1)}>Trước</button>
        <span>Trang {page} · {list.total} mục</span><button type="button" disabled={page * 20 >= list.total || pending} onClick={() => setPage(value => value + 1)}>Sau</button></div>
    </section></>}
  </div>;
}
