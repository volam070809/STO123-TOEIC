import { Link } from "react-router-dom";
import { courseStage } from "./coursePresentationData";

const percent = value => `${Number(value ?? 0).toLocaleString("vi-VN", { maximumFractionDigits: 1 })}%`;

export default function PlacementAnalysisView({ analysis, courses = [], current = true,
  target = "", onTargetChange, onTargetSubmit, pending = false, showAttemptHeader = false,
  coursesLoaded = true }) {
  const { result, summary, parts, stage, targetScore } = analysis;
  const recommended = current && targetScore != null ? courses.filter(course => course.recommended) : [];
  const stageCourses = courses.filter(course => course.stage === stage);
  const visibleCourses = recommended.length ? recommended : stageCourses;

  return <div className="placement-analysis-view" id={showAttemptHeader ? "placement-analysis" : undefined}>
    <section className="placement-result-hero" aria-label="Kết quả phân lớp KNN">
      <div><span className="placement-eyebrow">Kết quả kiểm tra đầu vào gần nhất</span>
        <h2>{stage ? `Giai đoạn ${stage}` : "Chưa có kết quả phân lớp"}</h2>
        {stage && <p>{courseStage[stage]?.name}</p>}
        <small>{result.finishedAt ? new Date(result.finishedAt).toLocaleString("vi-VN") : ""}</small></div>
      <div className="placement-result-hero-score"><strong>{result.overall.correct}/{result.overall.total}</strong>
        <span>câu đúng · {percent(summary.overallPercentage)}</span></div>
    </section>

    <section className="placement-result-section"><h3>Nghe và Đọc</h3>
      <div className="placement-skill-grid">
        <div><span>Listening</span><strong>{result.listening.correct}/{result.listening.total}</strong>
          <b>{percent(summary.listeningPercentage)}</b></div>
        <div><span>Reading</span><strong>{result.reading.correct}/{result.reading.total}</strong>
          <b>{percent(summary.readingPercentage)}</b></div>
      </div>
    </section>

    <section className="placement-result-section"><h3>Tỷ lệ đúng theo 7 Part</h3>
      <div className="placement-part-grid">{parts.map(part => <div key={part.part} className="placement-part-row">
        <div><strong>Part {part.part}</strong><span>{part.correct}/{part.total} đúng</span><b>{percent(part.percentage)}</b></div>
        <div className="placement-bar" role="progressbar" aria-label={`Part ${part.part}: ${percent(part.percentage)}`}
          aria-valuemin="0" aria-valuemax="100" aria-valuenow={part.percentage}>
          <span style={{ width: `${Math.max(0, Math.min(100, part.percentage))}%` }} /></div>
      </div>)}</div>
    </section>

    <section className="placement-result-section placement-next-steps"><div><h3>Bước tiếp theo</h3>
      <p>Xem lộ trình học phù hợp với giai đoạn hiện tại hoặc làm lại bài kiểm tra khi bạn muốn cập nhật kết quả.</p></div>
      <Link className="primary-button" to="/courses">Xem lộ trình học</Link></section>

    {stage && current && onTargetSubmit && <section className="placement-result-section"><h3>Điểm mục tiêu TOEIC</h3>
      <form className="exam-target-form" onSubmit={onTargetSubmit}>
        <label htmlFor="placement-target">Điểm muốn đạt</label>
        <input id="placement-target" type="number" min="10" max="990" required value={target}
          onChange={event => onTargetChange(event.target.value)} />
        <button className="outline-button" type="submit" disabled={pending}>{pending ? "Đang lưu…" : "Lưu mục tiêu"}</button>
      </form></section>}

    {stage && coursesLoaded && visibleCourses.length > 0 && <section className="placement-result-section"><h3>Khóa học phù hợp</h3>
      <div className="placement-course-list">{visibleCourses.slice(0, 3).map(course =>
        <Link key={course.courseId} to={`/courses/${course.courseId}`}><strong>{course.name}</strong>
          <span>Giai đoạn {course.stage} →</span></Link>)}</div></section>}
  </div>;
}