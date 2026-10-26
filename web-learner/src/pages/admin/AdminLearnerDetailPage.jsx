import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminLearnerApi } from "../../services/adminLearnerApi";
import { AdminMessage } from "./AdminLearnersPage";
import { formatDate, formatMoney, statusLabel, statusTone } from "./adminFormat";
import { groupVocabularyTopics, hasVocabularyPracticeAccuracy, learnerDetailParams, learnerDetailTab,
  mockScoreTrend, positivePage, practiceInsights } from "./adminLearnerDetailState";

const tabs = [
  ["overview", "Tổng quan"], ["vocabulary", "Từ vựng"],
  ["practice", "Luyện theo Part"], ["exams", "Thi thử & Phân lớp"],
  ["courses", "Khóa học & Đăng ký"],
];

export default function AdminLearnerDetailPage() {
  const { learnerId } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = learnerDetailTab(searchParams.get("tab"));
  const practicePage = positivePage(searchParams.get("practicePage"));
  const requestedPart = Number(searchParams.get("part"));
  const practicePart = Number.isInteger(requestedPart) && requestedPart >= 1 && requestedPart <= 7 ? requestedPart : null;
  const mockPage = positivePage(searchParams.get("mockPage"));
  const placementPage = positivePage(searchParams.get("placementPage"));
  const [revision, setRevision] = useState(0);
  const [tabRevision, setTabRevision] = useState(0);
  const overviewKey = `${token}|${learnerId}|${revision}`;
  const [overviewRequest, setOverviewRequest] = useState({ key: null, data: null, error: "" });
  const [tabRequests, setTabRequests] = useState({});
  const [notice, setNotice] = useState(location.state?.notice || "");
  const [action, setAction] = useState(null);
  const [mutation, setMutation] = useState({ busy: false, error: "", blockers: [] });
  const overview = overviewRequest.key === overviewKey ? overviewRequest.data : null;
  const overviewError = overviewRequest.key === overviewKey ? overviewRequest.error : "";
  const analyticsKey = `${activeTab}|${practicePage}|${practicePart || "all"}|${mockPage}|${placementPage}|${tabRevision}`;
  const tabRequest = tabRequests[analyticsKey];

  useEffect(() => {
    let active = true;
    adminLearnerApi.overview(learnerId, token)
      .then(data => { if (active) setOverviewRequest({ key: overviewKey, data, error: "" }); })
      .catch(error => { if (active) setOverviewRequest({ key: overviewKey, data: null,
        error: error.status === 404 ? "Không tìm thấy học viên này." : error.timeout
          ? "Kết nối dữ liệu mất nhiều thời gian hơn dự kiến. Hãy chọn Thử lại."
          : "Không thể tải hồ sơ học viên. Vui lòng thử lại." }); });
    return () => { active = false; };
  }, [learnerId, token, overviewKey]);

  useEffect(() => {
    if (activeTab === "overview") return;
    let active = true;
    const request = activeTab === "vocabulary" ? adminLearnerApi.vocabulary(learnerId, token) :
      activeTab === "practice" ? adminLearnerApi.practice(learnerId,
        { page: practicePage, pageSize: 10, part: practicePart }, token) :
        activeTab === "exams" ? adminLearnerApi.exams(learnerId,
          { mockPage, placementPage, pageSize: 10 }, token) : adminLearnerApi.courses(learnerId, token);
    request.then(data => { if (active) setTabRequests(current => ({ ...current,
      [analyticsKey]: { loading: false, data, error: "" } })); })
      .catch(error => { if (active) setTabRequests(current => ({ ...current, [analyticsKey]: {
        loading: false, data: null, error: error.timeout
          ? "Máy chủ phản hồi chậm. Vui lòng thử lại."
          : error.data?.message || "Không thể tải dữ liệu phân tích." } })); });
    return () => { active = false; };
  }, [activeTab, analyticsKey, learnerId, mockPage, placementPage, practicePage, practicePart, token]);

  function updateParams(changes) {
    setSearchParams(learnerDetailParams(searchParams, changes));
  }

  function selectTab(tab) {
    updateParams({ tab: tab === "overview" ? null : tab });
  }

  function openAction(kind) {
    setMutation({ busy: false, error: "", blockers: [] });
    setAction(kind);
  }

  async function confirmAction() {
    setMutation({ busy: true, error: "", blockers: [] });
    try {
      if (action === "delete") {
        await adminLearnerApi.remove(learnerId, token);
        navigate("/admin/learners", { replace: true, state: { notice: "Đã xóa tài khoản học viên." } });
        return;
      }
      const targetStatus = action === "lock" ? "BI_KHOA" : "HOAT_DONG";
      await adminLearnerApi.changeStatus(learnerId, targetStatus, token);
      setNotice(action === "lock" ? "Đã khóa tài khoản học viên." : "Đã mở khóa tài khoản học viên.");
      setAction(null);
      setMutation({ busy: false, error: "", blockers: [] });
      setRevision(value => value + 1);
    } catch (error) {
      setMutation({ busy: false, error: error.data?.message || "Không thể thực hiện thao tác. Vui lòng thử lại.",
        blockers: error.data?.blockers || [] });
    }
  }

  if (overviewRequest.key !== overviewKey) return <PageLoading label="Đang tải hồ sơ học viên" />;
  if (overviewError) return <div className="admin-page"><Link className="admin-back-link" to="/admin/learners">← Danh sách học viên</Link>
    <section className="admin-panel"><AdminMessage title="Không tải được hồ sơ" detail={overviewError}
      action={<button className="admin-button" type="button" onClick={() => setRevision(value => value + 1)}>Thử lại</button>} /></section></div>;

  return <div className="admin-page admin-learner-analytics">
    <Link className="admin-back-link" to="/admin/learners">← Danh sách học viên</Link>
    {notice && <div className="admin-feedback is-success" role="status">{notice}</div>}
    <LearnerHeader learner={overview} learnerId={learnerId} onAction={openAction} />
    {overview.status === "CHO_XAC_THUC" && <div className="admin-feedback is-info">
      Tài khoản đang chờ xác thực email. Không thể khóa hoặc mở khóa cho đến khi học viên xác thực.
    </div>}

    <div className="admin-learner-tabs" role="tablist" aria-label="Phân tích học tập">
      {tabs.map(([value, label]) => <button type="button" role="tab" key={value}
        id={`learner-tab-${value}`} aria-controls="learner-tab-panel" aria-selected={activeTab === value}
        className={activeTab === value ? "is-active" : ""}
        onClick={() => selectTab(value)}>{label}</button>)}
    </div>

    <section id="learner-tab-panel" className="admin-learner-tab-panel" role="tabpanel"
      aria-labelledby={`learner-tab-${activeTab}`}>
      {activeTab === "overview" ? <OverviewTab data={overview} /> :
        tabRequest?.loading || !tabRequest ? <TabLoading /> : tabRequest.error ?
          <AdminMessage title="Không tải được dữ liệu" detail={tabRequest.error}
            action={<button className="admin-button" type="button" onClick={() => setTabRevision(value => value + 1)}>Thử lại</button>} /> :
          activeTab === "vocabulary" ? <VocabularyTab data={tabRequest.data} /> :
            activeTab === "practice" ? <PracticeTab data={tabRequest.data} selectedPart={practicePart}
              onPart={part => updateParams({ part, practicePage: null })}
              onPage={page => updateParams({ practicePage: page })} /> :
              activeTab === "exams" ? <ExamsTab data={tabRequest.data}
                onMockPage={page => updateParams({ mockPage: page })}
                onPlacementPage={page => updateParams({ placementPage: page })} /> :
                <CoursesTab data={tabRequest.data} learnerId={learnerId} />}
    </section>
    {action && <ConfirmDialog kind={action} learner={overview} mutation={mutation}
      onCancel={() => !mutation.busy && setAction(null)} onConfirm={confirmAction} />}
  </div>;
}

function LearnerHeader({ learner, learnerId, onAction }) {
  return <section className="admin-profile-card admin-learner-profile">
    <span className="admin-profile-avatar">{initials(learner.fullName)}</span>
    <div className="admin-profile-title"><div><p className="admin-eyebrow">Hồ sơ học viên · ID #{learner.learnerId}</p>
      <h1>{learner.fullName}</h1></div><div className="admin-profile-controls">
        <span className={`admin-status is-${statusTone(learner.status)}`}>{statusLabel(learner.status)}</span>
        <div className="admin-action-buttons"><Link className="admin-button admin-button-secondary" to={`/admin/learners/${learnerId}/edit`}>Sửa</Link>
          {learner.status === "HOAT_DONG" && <button className="admin-button admin-button-warning" type="button" onClick={() => onAction("lock")}>Khóa</button>}
          {learner.status === "BI_KHOA" && <button className="admin-button" type="button" onClick={() => onAction("unlock")}>Mở khóa</button>}
          <button className="admin-button admin-button-danger" type="button" onClick={() => onAction("delete")}>Xóa</button>
        </div></div></div>
    <dl className="admin-profile-meta">
      <div><dt>Email</dt><dd>{learner.email}</dd></div><div><dt>Số điện thoại</dt><dd>{learner.phoneNumber || "Chưa cập nhật"}</dd></div>
      <div><dt>Ngày tham gia</dt><dd>{formatDate(learner.createdAt, true)}</dd></div><div><dt>Đăng nhập cuối</dt><dd>{formatDate(learner.lastLoginAt, true)}</dd></div>
    </dl>
  </section>;
}

function OverviewTab({ data }) {
  const visibleActivity = data.recentActivity.slice(0, 3);
  const remainingActivity = data.recentActivity.slice(3);
  return <>
    <div className="admin-kpi-grid admin-learner-kpis">
      <Stat label="Chủ đề từ vựng đã bắt đầu" value={data.vocabularyTopicsStarted} note="chủ đề có tiến độ thực tế" />
      <Stat label="Từ đã nhớ" value={data.uniqueRememberedWords} note="từ duy nhất, không đếm trùng chủ đề" />
      <Stat label="Lượt luyện theo Part" value={data.practiceAttemptCount} note="bao gồm mọi trạng thái" />
      <Stat label="Bài thi thử hoàn tất" value={data.completedMockCount} note="đã nộp hoặc hết giờ" />
    </div>
    <section className="admin-panel admin-detail-section">
      <div className="admin-section-heading"><div><h2>Hoạt động gần đây</h2><p>Tối đa 8 hoạt động mới nhất</p></div>
        <span className="admin-analytics-latest">Gần nhất: {formatDate(data.lastLearningAt, true)}</span></div>
      {data.recentActivity.length ? <><div className="admin-activity-list">{visibleActivity.map(activity =>
        <ActivityRow key={`${activity.type}-${activity.referenceId}`} activity={activity} />)}</div>
        {remainingActivity.length > 0 && <details className="admin-analytics-disclosure admin-activity-disclosure">
          <summary><span>Xem tất cả hoạt động</span><small>{remainingActivity.length} hoạt động khác</small></summary>
          <div className="admin-activity-list">{remainingActivity.map(activity =>
            <ActivityRow key={`${activity.type}-${activity.referenceId}`} activity={activity} />)}</div>
        </details>}</> :
        <AdminMessage title="Chưa có dữ liệu học tập" detail="Học viên chưa phát sinh hoạt động từ vựng, luyện tập hoặc bài thi." />}
    </section>
  </>;
}

function ActivityRow({ activity }) {
  return <article><span className="admin-activity-icon" aria-hidden="true">{activityIcon(activity.type)}</span>
    <div><strong>{activity.label}</strong><small>#{activity.referenceId} · {analyticsStatus(activity.status)}</small></div>
    <time>{formatDate(activity.occurredAt, true)}</time></article>;
}

function VocabularyTab({ data }) {
  const { started, inactive } = groupVocabularyTopics(data.topics);
  return <>
    <div className="admin-vocabulary-summary" aria-label="Tổng quan từ vựng">
      <VocabularyMetric label="Từ đã bắt đầu" value={data.uniqueStartedWords} />
      <VocabularyMetric label="Từ đã nhớ" value={data.uniqueRememberedWords} />
      <VocabularyMetric label="Chủ đề đã bắt đầu" value={`${data.topicsStarted}/${data.topics.length}`} />
    </div>
    {data.topics.length ? <div className="admin-vocabulary-groups">
      <section className="admin-panel admin-vocabulary-started">
        <div className="admin-vocabulary-group-heading"><h2>Đã bắt đầu</h2><span>{started.length} chủ đề</span></div>
        {started.length ? <div className="admin-vocabulary-started-list">{started.map(topic =>
          <StartedVocabularyTopic key={topic.topicId} topic={topic} />)}</div> :
          <p className="admin-vocabulary-inline-empty">Học viên chưa bắt đầu chủ đề nào.</p>}
      </section>
      {inactive.length > 0 && <details className="admin-panel admin-analytics-disclosure admin-vocabulary-inactive">
        <summary><span>Chưa bắt đầu</span><small>{inactive.length} chủ đề</small></summary>
        <div className="admin-vocabulary-inactive-list">{inactive.map(topic =>
          <article key={topic.topicId}><h3>{topic.name}</h3><span>{topic.totalWords} từ</span></article>)}</div>
      </details>}
    </div> : <AdminMessage title="Chưa có chủ đề từ vựng" detail="Hệ thống chưa có dữ liệu chủ đề để hiển thị." />}
  </>;
}

function VocabularyMetric({ label, value }) {
  return <article><span>{label}</span><strong>{value}</strong></article>;
}

function StartedVocabularyTopic({ topic }) {
  const showAccuracy = hasVocabularyPracticeAccuracy(topic);
  return <article className="admin-vocabulary-started-topic">
    <div className="admin-vocabulary-topic-title"><h3>{topic.name}</h3><strong>{percent(topic.progressPercent)}</strong></div>
    <div className="admin-progress-track" role="progressbar" aria-label={`Tiến độ ${topic.name}`}
      aria-valuemin="0" aria-valuemax="100" aria-valuenow={topic.progressPercent || 0}>
      <i style={{ width: `${topic.progressPercent || 0}%` }} /></div>
    <div className="admin-vocabulary-topic-numbers"><span><strong>{topic.rememberedWords}/{topic.totalWords}</strong> đã nhớ</span>
      <span><strong>{topic.practiceAttempts}</strong> lượt luyện</span>
      {showAccuracy && <span><strong>{percent(topic.practiceAccuracy)}</strong> chính xác</span>}</div>
    <details className="admin-topic-details"><summary>Chi tiết</summary>
      <dl className="admin-topic-metrics">
        {topic.learningWords > 0 && <div><dt>Đang học</dt><dd>{topic.learningWords} từ</dd></div>}
        {topic.practiceAttempts > 0 && <><div><dt>Đã hoàn tất</dt><dd>{topic.completedPracticeAttempts} lượt</dd></div>
          <div><dt>Kết quả luyện</dt><dd>{topic.practiceCorrect} đúng · {topic.practiceIncorrect} sai</dd></div></>}
        <div><dt>Hoạt động gần nhất</dt><dd>{formatDate(topic.lastActivityAt, true)}</dd></div>
      </dl>
    </details>
  </article>;
}

function PracticeTab({ data, selectedPart, onPart, onPage }) {
  const insights = practiceInsights(data.parts);
  return <>
    <div className="admin-tab-toolbar"><div><h2>Hiệu suất Parts 1–7</h2><p>Độ chính xác = số đúng / tổng số câu trong snapshot.</p></div>
      <label><span>Lọc lịch sử</span><select value={selectedPart || ""} onChange={event => onPart(event.target.value || null)}>
        <option value="">Tất cả Part</option>{data.parts.map(part => <option key={part.part} value={part.part}>Part {part.part}</option>)}</select></label></div>
    <section className="admin-panel admin-part-overview">
      <div className="admin-learning-insights" aria-label="Điểm mạnh và điểm cần chú ý">
        <Insight label="Mạnh nhất" part={insights.strongest} tone="positive" />
        <Insight label="Cần chú ý" part={insights.weakest} tone="attention" />
      </div>
      <div className="admin-part-comparison">{data.parts.map(part => <PartPerformance key={part.part} part={part} />)}</div>
    </section>
    <details className="admin-panel admin-analytics-disclosure admin-history-disclosure">
      <summary><span>Xem chi tiết lịch sử luyện tập</span><small>{data.totalItems} lượt</small></summary>
      <div className="admin-disclosure-content">
      <p className="admin-history-note">Preset và độ khó không được lưu trong snapshot hiện tại.</p>
      {data.items.length ? <><div className="admin-table-wrap"><table className="admin-table admin-analytics-table">
        <thead><tr><th>Lượt</th><th>Part</th><th>Ngày</th><th>Preset / Độ khó</th><th>Đúng</th><th>Sai</th><th>Chưa trả lời</th><th>Độ chính xác</th><th>Trạng thái</th></tr></thead>
        <tbody>{data.items.map(item => <tr key={item.attemptId}><td>#{item.attemptId}</td><td>{item.part ? `Part ${item.part}` : "—"}</td>
          <td>{formatDate(item.completedAt || item.startedAt, true)}</td><td>Chưa có dữ liệu</td><td>{item.correct}/{item.totalQuestions}</td>
          <td>{item.incorrect}</td><td>{item.unanswered}</td><td>{percent(item.accuracy)}</td>
          <td><span className={`admin-status is-${statusTone(item.status)}`}>{analyticsStatus(item.status)}</span></td></tr>)}</tbody>
      </table></div><Pagination page={data.page} totalPages={data.totalPages} onPage={onPage} /></> :
        <AdminMessage title="Chưa có lượt luyện tập" detail="Không có lịch sử phù hợp với bộ lọc hiện tại." />}
      </div>
    </details>
  </>;
}

function Insight({ label, part, tone }) {
  return <div className={`admin-insight is-${tone}`}><span>{label}</span>
    {part ? <><strong>Part {part.part}</strong><small>{percent(part.accuracy)} · {part.totalAttempts} lượt</small></> :
      <><strong>Chưa đủ dữ liệu</strong><small>Cần ít nhất một lượt luyện</small></>}
  </div>;
}

function PartPerformance({ part }) {
  const accuracy = part.accuracy == null ? 0 : Math.max(0, Math.min(100, Number(part.accuracy)));
  return <div className="admin-part-performance">
    <strong>Part {part.part}</strong>
    <div className="admin-part-progress" role="progressbar" aria-label={`Độ chính xác Part ${part.part}`}
      aria-valuemin="0" aria-valuemax="100" aria-valuenow={accuracy}><i style={{ width: `${accuracy}%` }} /></div>
    <b>{percent(part.accuracy)}</b>
    <span>{part.totalAttempts} lượt · {part.completedAttempts} hoàn tất</span>
    <span>{part.unanswered} chưa trả lời</span>
  </div>;
}

function ExamsTab({ data, onMockPage, onPlacementPage }) {
  return <>
    <ExamSection title="Thi thử TOEIC" description="Điểm TOEIC hiển thị là điểm ước tính, không phải điểm ETS chính thức."
      history={data.mockTests} onPage={onMockPage} placement={false} />
    <ExamSection title="Kiểm tra đầu vào" description="Kết quả phân lớp và giai đoạn KNN chỉ hiển thị khi đã được lưu."
      history={data.placementTests} onPage={onPlacementPage} placement />
  </>;
}

function ExamSection({ title, description, history, onPage, placement }) {
  const summary = history.summary;
  const scoreTrend = placement ? [] : mockScoreTrend(history.items);
  const latestPlacement = placement ? history.items[0] : null;
  return <section className="admin-panel admin-exam-section"><div className="admin-section-heading"><div><h2>{title}</h2><p>{description}</p></div></div>
    <div className="admin-exam-summary"><Stat label="Tổng lượt" value={summary.totalAttempts} note={`${summary.completedAttempts} hoàn tất`} />
      <Stat label="Đúng / Sai / Bỏ trống" value={`${summary.correct} / ${summary.incorrect} / ${summary.unanswered}`} note="trên các lượt hoàn tất" />
      <Stat label="Độ chính xác" value={percent(summary.accuracy)} note="số đúng / tổng câu" />
      {!placement && <Stat label="Điểm gần nhất" value={summary.latestEstimatedScore ?? "—"} note="điểm TOEIC ước tính" />}</div>
    {scoreTrend.length > 1 && <ScoreTrend points={scoreTrend} />}
    {latestPlacement && <PlacementSnapshot item={latestPlacement} />}
    {history.items.length ? <details className="admin-analytics-disclosure admin-history-disclosure">
      <summary><span>Xem chi tiết lịch sử {title.toLowerCase()}</span><small>{history.totalItems} lượt</small></summary>
      <div className="admin-disclosure-content"><div className="admin-table-wrap"><table className="admin-table admin-exam-table">
      <thead><tr><th>Lượt</th><th>Thời gian</th><th>Đúng / Tổng</th><th>Độ chính xác</th>
        {placement ? <><th>Giai đoạn KNN</th><th>Điểm mục tiêu</th></> : <><th>Nghe</th><th>Đọc</th><th>Tổng ước tính</th></>}<th>Trạng thái</th></tr></thead>
      <tbody>{history.items.map(item => <ExamRow key={item.attemptId} item={item} placement={placement} />)}</tbody>
    </table></div><Pagination page={history.page} totalPages={history.totalPages} onPage={onPage} /></div></details> :
      <AdminMessage title="Chưa có dữ liệu" detail={`Học viên chưa có lịch sử ${title.toLowerCase()}.`} />}
  </section>;
}

function ScoreTrend({ points }) {
  const labels = points.map(point => `${point.score} điểm`).join(", ");
  return <div className="admin-score-trend-section">
    <div className="admin-score-trend-heading"><div><strong>Xu hướng điểm TOEIC ước tính</strong>
      <small>Theo thứ tự thời gian trong trang lịch sử đang xem</small></div>
      <span>{points[0].score} → {points.at(-1).score}</span></div>
    <div className="admin-score-trend" role="img" aria-label={`Xu hướng điểm TOEIC ước tính: ${labels}`}>
      {points.map(point => <div className="admin-score-point" key={point.attemptId}>
        <span><i style={{ height: `${Math.max(6, Math.min(100, point.score / 9.9))}%` }} /></span>
        <strong>{point.score}</strong><small>{shortDate(point.completedAt)}</small>
      </div>)}
    </div>
  </div>;
}

function PlacementSnapshot({ item }) {
  return <div className="admin-placement-snapshot">
    <div><span>Kết quả phân lớp gần nhất</span><strong>{item.recommendedStage ? `Giai đoạn ${item.recommendedStage}` : "Chưa có giai đoạn KNN"}</strong>
      <small>{formatDate(item.completedAt || item.startedAt, true)} · Mục tiêu {item.targetScore ?? "chưa có dữ liệu"}</small></div>
    {item.parts.length > 0 && <div className="admin-placement-parts" aria-label="Kết quả theo Part">
      {item.parts.map(part => <span key={part.part}><b>P{part.part}</b><i>{part.correct}/{part.total}</i></span>)}</div>}
  </div>;
}

function ExamRow({ item, placement }) {
  return <tr><td><strong>#{item.attemptId}</strong><small>{item.examName || (placement ? "Bài phân lớp" : "Đề ngẫu nhiên")}</small>
    {placement && item.parts.length > 0 && <div className="admin-part-chips">{item.parts.map(part =>
      <span key={part.part}>P{part.part}: {part.correct}/{part.total}</span>)}</div>}</td>
    <td>{formatDate(item.completedAt || item.startedAt, true)}</td><td>{item.correct}/{item.total}</td><td>{percent(item.accuracy)}</td>
    {placement ? <><td>{item.recommendedStage ? `Giai đoạn ${item.recommendedStage}` : "Chưa có dữ liệu"}</td>
      <td>{item.targetScore ?? "Chưa có dữ liệu"}</td></> : <><td>{item.listeningScore ?? "—"}</td><td>{item.readingScore ?? "—"}</td><td>{item.totalScore ?? "—"}</td></>}
    <td><span className={`admin-status is-${statusTone(item.status)}`}>{analyticsStatus(item.status)}</span></td></tr>;
}

function CoursesTab({ data, learnerId }) {
  const accessibleCourses = [...new Map(data.enrollments.flatMap(enrollment => enrollment.courses)
    .filter(course => course.hasCurrentAccess).map(course => [course.courseId, course])).values()];
  return <>
    <div className="admin-stat-grid admin-learner-summary-grid"><Stat label="Lượt đăng ký" value={data.enrollmentCount} note="gói và mua trực tiếp" />
      <Stat label="Khóa đang truy cập" value={data.accessibleCourseCount} note="theo CourseEntitlement hiện tại" /></div>
    <section className="admin-panel admin-detail-section"><div className="admin-section-heading"><div><h2>Khóa học và quyền truy cập</h2>
      <p>Không hiển thị phần trăm hoàn thành vì tiến độ lộ trình chưa được lưu đầy đủ.</p></div></div>
      {accessibleCourses.length > 0 && <div className="admin-current-access"><h3>Khóa đang truy cập</h3>
        <div className="admin-access-course-list">{accessibleCourses.map(course =>
          <Link key={course.courseId} to={`/admin/courses/${course.courseId}`}><span><strong>{course.name}</strong>
            <small>Giai đoạn {course.stage}</small></span><em className="is-active">Đang truy cập</em></Link>)}</div></div>}
      {data.enrollments.length ? <details className="admin-analytics-disclosure admin-history-disclosure admin-enrollment-disclosure">
        <summary><span>Xem chi tiết đăng ký và thanh toán</span><small>{data.enrollments.length} đăng ký</small></summary>
        <div className="admin-enrollment-list">{data.enrollments.map(enrollment =>
        <article className="admin-enrollment" key={enrollment.enrollmentId}>
          <div className="admin-enrollment-head"><div><small>{enrollment.purchaseType === "PACKAGE" ? "Mua gói" : "Mua khóa học trực tiếp"} · Đăng ký #{enrollment.enrollmentId}</small>
            <h3>{enrollment.packageName || enrollment.courses.map(course => course.name).join(", ") || "Đăng ký chưa có khóa học"}</h3></div>
            <span className={`admin-status is-${statusTone(enrollment.storedStatus)}`}>{analyticsStatus(enrollment.storedStatus)}</span></div>
          <dl className="admin-enrollment-meta"><div><dt>Ngày đăng ký</dt><dd>{formatDate(enrollment.registeredAt, true)}</dd></div>
            <div><dt>Hiệu lực</dt><dd>{formatDate(enrollment.startedAt)} – {formatDate(enrollment.endsAt)}</dd></div>
            <div><dt>Giá đăng ký</dt><dd>{formatMoney(enrollment.registrationPrice)}</dd></div>
            <div><dt>Thanh toán</dt><dd>{analyticsStatus(enrollment.paymentStatus)}</dd></div></dl>
          <div className="admin-access-course-list">{enrollment.courses.length ? enrollment.courses.map(course =>
            <Link key={course.courseId} to={`/admin/courses/${course.courseId}`}><span><strong>{course.name}</strong><small>Giai đoạn {course.stage}{course.priceSnapshot != null ? ` · ${formatMoney(course.priceSnapshot)}` : ""}</small></span>
              <em className={`is-${course.hasCurrentAccess ? "active" : "inactive"}`}>{analyticsStatus(course.accessStatus)}</em></Link>) :
            <p>Chưa có khóa học liên kết.</p>}</div>
          <Link className="admin-row-link" to={`/admin/enrollments/${enrollment.enrollmentId}`}>Xem đăng ký →</Link>
        </article>)}</div></details> : <AdminMessage title="Chưa có đăng ký" detail={`Học viên #${learnerId} chưa mua gói hoặc khóa học nào.`} />}
    </section>
  </>;
}

function Pagination({ page, totalPages, onPage }) {
  if (totalPages <= 1) return null;
  return <div className="admin-pagination"><span>Trang {page} / {totalPages}</span><div>
    <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>Trang trước</button>
    <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Trang sau</button>
  </div></div>;
}

function PageLoading({ label }) {
  return <div className="admin-page"><div className="admin-panel admin-loading" role="status">
    <span className="admin-spinner" aria-hidden="true" /><strong>{label}</strong></div></div>;
}

function TabLoading() {
  return <div className="admin-panel admin-loading admin-tab-loading" role="status"><span className="admin-spinner" aria-hidden="true" />
    <strong>Đang tải dữ liệu phân tích</strong><span>Chỉ dữ liệu của tab đang mở được tải.</span></div>;
}

function Stat({ label, value, note }) {
  return <article className="admin-stat"><span>{label}</span><strong>{value}</strong><small>{note}</small></article>;
}

function initials(name) {
  return name?.split(/\s+/).filter(Boolean).slice(-2).map(part => part[0]).join("").toUpperCase() || "HV";
}

function percent(value) {
  return value == null ? "Chưa có dữ liệu" : `${value}%`;
}

function shortDate(value) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit" }).format(new Date(value));
}

function analyticsStatus(value) {
  const labels = { DA_NOP: "Đã nộp", HET_GIO: "Hết giờ", DANG_LAM: "Đang làm", BO_DO: "Bỏ dở",
    SUCCESS: "Thành công", PENDING: "Chờ xử lý", FAILED: "Thất bại", NO_PAYMENT: "Chưa có thanh toán",
    ACTIVE: "Đang truy cập", PAYMENT_PENDING: "Chờ thanh toán", EXPIRED: "Đã hết hạn", INACTIVE: "Không hiệu lực" };
  return labels[value] || statusLabel(value);
}

function activityIcon(type) {
  return type === "VOCABULARY" ? "V" : type === "PRACTICE" ? "P" : type === "MOCK" ? "M" : "K";
}

function ConfirmDialog({ kind, learner, mutation, onCancel, onConfirm }) {
  const deleting = kind === "delete";
  const locking = kind === "lock";
  const title = deleting ? "Xóa tài khoản học viên?" : locking ? "Khóa tài khoản học viên?" : "Mở khóa tài khoản học viên?";
  const detail = deleting ? "Chỉ tài khoản không có lịch sử nghiệp vụ mới được xóa; dữ liệu học tập không bị xóa cascade." :
    locking ? "Học viên sẽ bị từ chối ở các API được bảo vệ, kể cả khi JWT chưa hết hạn." :
      "Tài khoản bị khóa sẽ được chuyển lại sang trạng thái Hoạt động.";
  return <div className="admin-modal-backdrop" role="presentation" onMouseDown={event => {
    if (event.target === event.currentTarget) onCancel();
  }}><section className="admin-modal" role="dialog" aria-modal="true" aria-labelledby="admin-confirm-title">
    <span className={`admin-modal-icon ${deleting ? "is-danger" : ""}`}>{deleting ? "!" : "?"}</span>
    <h2 id="admin-confirm-title">{title}</h2><p><strong>{learner.fullName}</strong> · {learner.email}</p><p>{detail}</p>
    {mutation.error && <div className="admin-feedback is-error" role="alert">{mutation.error}
      {mutation.blockers.length > 0 && <ul>{mutation.blockers.map(item => <li key={item}>{item}</li>)}</ul>}</div>}
    <div className="admin-modal-actions"><button className="admin-button admin-button-secondary" type="button" disabled={mutation.busy} onClick={onCancel}>Hủy</button>
      <button className={`admin-button ${deleting ? "admin-button-danger" : locking ? "admin-button-warning" : ""}`}
        type="button" disabled={mutation.busy} onClick={onConfirm}>{mutation.busy ? "Đang xử lý..." :
          deleting ? "Xác nhận xóa" : locking ? "Xác nhận khóa" : "Xác nhận mở khóa"}</button></div>
  </section></div>;
}
