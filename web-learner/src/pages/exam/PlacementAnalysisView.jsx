import { useState } from "react";
import { Link } from "react-router-dom";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import PlacementIcon from "./PlacementIcon";
import { courseImageSource } from "../../services/courseImage";
import courseFallback from "../../assets/placement/course-fallback.svg";
import { courseAudienceLabel, courseEntryReference, courseTargetLabel } from "./coursePresentationData";
import { practicePriorities, strengthWeakness } from "./placementPractice";
import { coursesForPlacementPath } from "./placementAttemptAnalysis";
import "../../styles/learner-journey.css";

const percent = value => `${Number(value ?? 0).toLocaleString("vi-VN", { maximumFractionDigits: 1 })}%`;
const missingKnn = "Bài này chưa có kết quả phân lớp sau khi kiểm tra lại. Kết quả thi vẫn được giữ nguyên.";
const knnLevels = { NEEDS_IMPROVEMENT: "Cần cải thiện", DEVELOPING: "Đang phát triển", GOOD: "Tốt" };
const placementStageNames = { 1: "Nền tảng", 2: "Phát triển", 3: "Nâng cao" };
const partLabels = { 1: "Mô tả tranh", 2: "Hỏi và đáp", 3: "Hội thoại", 4: "Bài nói ngắn", 5: "Hoàn thành câu", 6: "Hoàn thành đoạn văn", 7: "Đọc hiểu" };
const actualRate = stats => stats?.total ? 100 * stats.correct / stats.total : 0;

function PlacementCourseCover({ course }) {
  const [failed, setFailed] = useState(false);
  const source = courseImageSource(course.coverImageUrl);
  return <div className={`placement-course-cover stage-${course.stage}`}>
    {source && !failed ? <img src={source} alt={`Ảnh bìa khóa học ${course.name}`} loading="lazy"
      width="640" height="360" onError={() => setFailed(true)} /> : <>
      <img src={courseFallback} alt="" width="640" height="360" />
      <div className="placement-course-fallback-label"><span>STO123 · TOEIC</span><strong>{course.name}</strong></div>
    </>}
    <span className="placement-course-cover-badge">Giai đoạn {course.stage}</span>
  </div>;
}

function TargetForm({ target, onTargetChange, onTargetSubmit, pending, feedback }) {
  return <section className="placement-result-section placement-target-panel" aria-label="Điểm mục tiêu">
    <span className="placement-icon-tile"><PlacementIcon name="target" size={27} /></span>
    <div><h3>Đặt mục tiêu TOEIC</h3><p>Chọn điểm muốn đạt để xem lộ trình và các khóa học phù hợp.</p>
      {feedback?.text && <small role={feedback.type === "error" ? "alert" : "status"}
        className={`placement-target-feedback is-${feedback.type}`}>{feedback.text}</small>}</div>
    <form className="exam-target-form" onSubmit={onTargetSubmit}>
      <label htmlFor="placement-target">Điểm mục tiêu</label>
      <input id="placement-target" type="number" min="10" max="990" required value={target}
        onChange={event => onTargetChange(event.target.value)} />
      <button className="primary-button" type="submit" disabled={pending}>
        {pending ? "Đang lưu…" : "Lưu mục tiêu"}</button>
    </form>
  </section>;
}

function PracticeSection({ parts }) {
  const priorities = practicePriorities(parts);
  if (!priorities.length) return null;
  return <section className="placement-result-section" aria-label="Các Part nên ưu tiên luyện tập">
    <h3><PlacementIcon name="progress" /> Ba Part nên ưu tiên luyện tập</h3>
    <p className="placement-note">Sắp xếp theo tỷ lệ đúng tăng dần từ kết quả bài kiểm tra đầu vào này.</p>
    <ol className="placement-priority-list">{priorities.map(item =>
      <li key={item.part} className="placement-priority-card">
        <span className={`placement-priority-badge priority-${item.priority}`}>{item.priority}</span>
        <span className="placement-icon-tile"><PlacementIcon name={item.part <= 4 ? "headphones" : "book"} size={19} /></span>
         <div><strong>Part {item.part} · {partLabels[item.part]}</strong><span>{percent(item.percentage)} câu đúng</span></div>
        <Link className="outline-button" to={`/practice/parts?part=${item.part}`}>Luyện ngay <PlacementIcon name="arrow" size={16} /></Link>
      </li>)}</ol>
  </section>;
}

function CourseSection({ courses, stage, targetScore, current, coursesState, onRetry }) {
  const visible = coursesForPlacementPath(courses, stage, targetScore, current);
  const recommended = current && targetScore != null ? visible.filter(course => course.recommended) : [];
  return <section className="placement-result-section" aria-label="Khóa học và lộ trình phù hợp">
    <h3>Khóa học và lộ trình phù hợp với bạn</h3>
    {coursesState === "loading" && <p role="status">Đang tải khóa học phù hợp…</p>}
    {coursesState === "error" && <>
      <p role="alert">Không thể tải khóa học hiện có.</p>
      <button type="button" className="outline-button" onClick={onRetry}>Thử tải lại khóa học</button>
    </>}
    {coursesState === "ready" && visible.length === 0 && <>
      <p>Hiện chưa có khóa học đang mở phù hợp với giai đoạn KNN này.</p>
      <Link className="outline-button" to="/courses">Xem tất cả khóa học</Link>
    </>}
    {coursesState === "ready" && visible.length > 0 && <>
      <p className="placement-note">{recommended.length
        ? `KNN phân lớp hiện tại ở Giai đoạn ${stage}. Các khóa học được API đánh dấu đề xuất theo mục tiêu ${targetScore} điểm hiển thị theo thứ tự giai đoạn.`
        : `KNN phân lớp hiện tại ở Giai đoạn ${stage}. Đang hiển thị khóa học cùng giai đoạn; nhập điểm mục tiêu để xem lộ trình đầy đủ.`}</p>
      <div className={`placement-course-grid${visible.length === 1 ? " is-single" : visible.length === 4 ? " is-four" : ""}`}>{visible.map(course =>
        <article key={course.courseId} className={`placement-course-card${current && targetScore != null && course.recommended ? " is-recommended" : ""}`}>
          <PlacementCourseCover course={course} />
          <div className="placement-course-body">
            <div className="placement-course-badges">
              <span>{courseAudienceLabel(course.stage)}</span>
              {current && targetScore != null && course.recommended && <span className="is-recommended">Đề xuất {course.recommendationOrder ?? ""}</span>}
            </div>
            <h4>{course.name}</h4>
            <p className="placement-course-description">{course.description || "Thông tin khóa học đang được cập nhật."}</p>
            <div className="placement-course-metadata"><div><small>Đầu vào</small><strong>{courseEntryReference(course.stage)}</strong></div>
              <div><small>Mục tiêu</small><strong>{courseTargetLabel(course.maxTargetScore)}</strong></div></div>
            <span className={`placement-access-status${course.accessStatus === "ACTIVE" ? " is-active" : ""}`}>
              <PlacementIcon name={course.accessStatus === "ACTIVE" ? "check" : "course"} size={16} />
              {course.accessStatus === "ACTIVE" ? "Đã có quyền học" : "Xem thông tin quyền học"}</span>
            <div className="placement-course-actions">
              <Link className="outline-button" to={`/courses/${course.courseId}`}>Xem khóa học</Link>
              {course.accessStatus === "ACTIVE"
                ? <Link className="primary-button" to={`/courses/${course.courseId}`}>Vào học</Link>
                : <Link className="primary-button" to="/courses">Xem lộ trình</Link>}
            </div>
          </div>
        </article>)}</div>
    </>}
  </section>;
}

export default function PlacementAnalysisView({ analysis, courses = [], current = true,
  target = "", onTargetChange, onTargetSubmit, pending = false, showAttemptHeader = false,
  coursesState = "ready", onCoursesRetry, targetFeedback, onRetake, retakePending = false }) {
  const { result, parts, stage, targetScore, classification } = analysis;
  const pair = strengthWeakness(analysis);
  const noKnn = classification === "UNAVAILABLE" || !stage;

  return <div className="placement-analysis-view" id={showAttemptHeader ? "placement-analysis" : undefined}>
    <section className="placement-result-hero" aria-label="Kết quả phân lớp KNN">
      <div className="placement-result-hero-copy"><span className="placement-result-badge"><PlacementIcon name="chart" size={17} /> HÀNH TRÌNH TOEIC CỦA BẠN BẮT ĐẦU TỪ ĐÂY</span>
        <h2>{stage ? placementStageNames[stage] : "Chưa có kết quả phân lớp"}</h2>
        {stage && <p>Giai đoạn {stage} · Kết quả bài kiểm tra giúp chọn nhịp học phù hợp với bạn.</p>}
        {noKnn && <p role="status">{missingKnn}</p>}
        <small>{result.finishedAt ? `Hoàn thành ${new Date(result.finishedAt).toLocaleString("vi-VN")}` : ""}</small></div>
      <div className="placement-stage-visual" role="img" aria-label={stage ? `KNN phân lớp giai đoạn ${stage}` : "Chưa có giai đoạn KNN"}>
        <div className="placement-stage-line" aria-hidden="true" />
        {[1, 2, 3].map(value => <div key={value} className={`placement-stage-node${stage === value ? " is-current" : ""}`} aria-hidden="true">
          <span>{value}</span><small>Giai đoạn {value}</small></div>)}
      </div>
    </section>

    <section className="placement-result-section placement-essential-results" aria-label="Tổng quan kết quả">
      <h3>Kết quả của bạn</h3>
      <div className="placement-kpi-grid">
        {[
          { icon: "chart", label: "TOEIC ước tính", value: result.totalScore == null ? "—" : `${result.totalScore}/990`, detail: "Điểm tham khảo", rate: result.totalScore == null ? 0 : 100 * result.totalScore / 990 },
          { icon: "headphones", label: "Listening", value: `${result.listening.correct}/${result.listening.total}`, detail: percent(actualRate(result.listening)), rate: actualRate(result.listening) },
          { icon: "book", label: "Reading", value: `${result.reading.correct}/${result.reading.total}`, detail: percent(actualRate(result.reading)), rate: actualRate(result.reading) },
        ].map(item => <article key={item.label} className="placement-kpi-card"><div className="placement-kpi-top"><span className="placement-icon-tile"><PlacementIcon name={item.icon} size={21} /></span><small>{item.label}</small></div>
          <strong>{item.value}</strong><span>{item.detail}</span><div className="placement-kpi-meter" aria-hidden="true"><i style={{ width: `${Math.max(0, Math.min(100, item.rate))}%` }} /></div>
        </article>)}
      </div>
    </section>

    {pair && <section className="placement-result-section" aria-label="Điểm mạnh và điểm yếu">
      <h3>Bạn đang làm tốt ở đâu?</h3>
      <div className="placement-strength-grid">
        <article className="placement-strength-card is-strong"><span className="placement-eyebrow">Điểm mạnh</span><strong>Part {pair.strongest.part} · {partLabels[pair.strongest.part]}</strong><p>{pair.strongest.correct}/{pair.strongest.total} câu đúng</p></article>
        <article className="placement-strength-card is-weak"><span className="placement-eyebrow">Nên ưu tiên</span><strong>Part {pair.weakest.part} · {partLabels[pair.weakest.part]}</strong><p>{pair.weakest.correct}/{pair.weakest.total} câu đúng</p></article>
      </div>
    </section>}

    <PracticeSection parts={parts} />

    {stage && current && onTargetSubmit &&
      <TargetForm target={target} onTargetChange={onTargetChange} onTargetSubmit={onTargetSubmit} pending={pending} feedback={targetFeedback} />}
    {stage && <CourseSection courses={courses} stage={stage} targetScore={targetScore}
      current={current} coursesState={coursesState} onRetry={onCoursesRetry} />}

    <details className="learner-disclosure placement-deep-analysis"><summary><span><small>KẾT QUẢ ĐẦY ĐỦ</small><strong>Phân tích bài kiểm tra</strong><em>Xem tỷ lệ đúng, số câu và cách phân lớp theo từng Part</em></span><DisclosureToggle /></summary>
      <div className="learner-disclosure-body"><div className="placement-deep-facts"><span>Tổng câu đúng <strong>{result.overall.correct}/{result.overall.total}</strong></span>
        <span>Listening <strong>{result.listening.correct}/{result.listening.total} · {percent(actualRate(result.listening))}</strong></span>
        <span>Reading <strong>{result.reading.correct}/{result.reading.total} · {percent(actualRate(result.reading))}</strong></span>
        <span>Phân lớp <strong>{stage ? `Giai đoạn ${stage} · ${placementStageNames[stage]}` : "Chưa có kết quả"}</strong></span></div>
      <h3>Hiệu suất 7 Part</h3>
      <div className="placement-part-grid">{parts.map(part => <div key={part.part} className={`placement-part-row${part.part <= 4 ? " is-listening" : " is-reading"}`}>
        <div className="placement-part-info"><span className="placement-icon-tile"><PlacementIcon name={part.part <= 4 ? "headphones" : "book"} size={17} /></span>
          <strong>Part {part.part}</strong><small>{partLabels[part.part]}</small></div>
        <div className="placement-bar" role="progressbar" aria-label={`Part ${part.part}: ${percent(part.percentage)}`}
          aria-valuemin="0" aria-valuemax="100" aria-valuenow={part.percentage}>
          <span style={{ width: `${Math.max(0, Math.min(100, part.percentage))}%` }} /></div>
        <div className="placement-part-value"><strong>{percent(part.percentage)}</strong><small>{part.correct}/{part.total} đúng</small></div>
      </div>)}</div>
      {pair && <p className="placement-deep-note">{knnLevels[pair.strongest.level] ?? ""} ở Part {pair.strongest.part}; {knnLevels[pair.weakest.level] ?? ""} ở Part {pair.weakest.part}. Kết quả KNN được tính từ bài đã hoàn thành gần nhất.</p>}
      </div></details>

    <section className="placement-result-section placement-next-steps">
      <div><h3>Bước tiếp theo</h3>
         <p>Bắt đầu từ Part nên ưu tiên hoặc tiếp tục với khóa học phù hợp.</p></div>
      <div className="placement-latest-actions">
         <Link className="primary-button" to="/smart-learning">Xem gợi ý học tiếp →</Link>
         <Link className="outline-button" to="/courses">Xem khóa học</Link>
        {pair && <Link className="outline-button" to={`/practice/parts?part=${pair.weakest.part}`}>
          Luyện Part {pair.weakest.part}</Link>}
         {onRetake && <button className="outline-button" type="button" disabled={retakePending} onClick={onRetake}>
          {retakePending ? "Đang bắt đầu…" : "Làm lại kiểm tra"}</button>}
      </div>
    </section>
  </div>;
}
