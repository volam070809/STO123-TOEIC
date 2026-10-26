import { Link } from "react-router-dom";
import { courseAudienceLabel, courseEntryReference, courseStage, courseTargetLabel } from "./coursePresentationData";
import { coursesForKnnStage } from "./placementAttemptAnalysis";

const levels = { NEEDS_IMPROVEMENT: "Cần cải thiện", DEVELOPING: "Đang phát triển", GOOD: "Tốt" };

export default function PlacementAnalysisView({ analysis, courses = [], current = true,
  target = "", onTargetChange, onTargetSubmit, pending = false, showAttemptHeader = false,
  coursesLoaded = true }) {
  const { result, summary, parts, stage, targetScore } = analysis;
  const strongest = parts.find(part => part.part === summary.strongestPart);
  const lowest = parts.find(part => part.part === summary.weakestPart);
  const strong = parts.filter(part => part.percentage >= 65).map(part => `Part ${part.part}`);
  const improve = parts.filter(part => part.percentage < 40).map(part => `Part ${part.part}`);
  const recommended = current && targetScore != null ? courses.filter(course => course.recommended) : [];
  const stageCourses = targetScore == null ? courses.filter(course => course.stage === stage) : [];
  const displayedCourses = recommended.length ? recommended : stageCourses;
  const matchingStageCourses = coursesForKnnStage(courses, stage);

  return <div className="placement-analysis-view">
    {showAttemptHeader && <section id="placement-analysis" className="exam-score"><h2>Kết quả kiểm tra đầu vào</h2>
      <p>{result.finishedAt ? new Date(result.finishedAt).toLocaleString("vi-VN") : "—"} · {result.overall.correct} / {result.overall.total} câu đúng</p>
      <Link className="outline-button" to={`/exam/${result.attemptId}/review`}>Xem lại bài</Link></section>}
    <section><h2>Tổng quan năng lực</h2><div className="placement-summary">
      <div><span>Tỷ lệ năng lực trung bình</span><strong>{summary.overallPercentage}%</strong></div>
      <div><span>Listening</span><strong>{summary.listeningPercentage}%</strong></div>
      <div><span>Reading</span><strong>{summary.readingPercentage}%</strong></div>
    </div><p className="exam-card-note">Các tỷ lệ này là đánh giá nội bộ của STO123, không phải điểm TOEIC chính thức.</p></section>
    <section><h2>Listening / Reading</h2><div className="placement-summary">
      {[{ name: "Listening", value: summary.listeningPercentage }, { name: "Reading", value: summary.readingPercentage }]
        .map(item => <div key={item.name}><strong>{item.name} · {item.value}%</strong>
          <div className="placement-average"><span style={{ width: `${item.value}%` }} /></div></div>)}</div></section>
    <section><h2>Kết quả P1–P7</h2><div className="placement-parts">{parts.map(part =>
      <div key={part.part}><div className="placement-part-label"><strong>Part {part.part}</strong>
        <span>{part.correct}/{part.total} đúng · {part.percentage}% · {levels[part.level]}</span></div>
        <div className="placement-bar"><span className={part.level.toLowerCase()} style={{ width: `${part.percentage}%` }} /></div>
      </div>)}</div></section>
    <section><h2>Part mạnh / Part cần cải thiện</h2>
      <p>Mạnh nhất: Part {strongest.part} · {strongest.percentage}%</p>
      <p>{lowest.percentage < 40 ? "Cần cải thiện nhất" : "Kết quả thấp nhất hiện tại"}: Part {lowest.part} · {lowest.percentage}%</p>
      {!improve.length && <p>Không có Part nào nằm trong nhóm cần cải thiện nghiêm trọng.</p>}
      <p>{strong.length ? `Bạn có kết quả tốt ở ${strong.join(" và ")}. ` : ""}
        {improve.length ? `${improve.join(" và ")} nên được ưu tiên cải thiện. ` : ""}
        {summary.listeningPercentage > summary.readingPercentage ? "Listening hiện tốt hơn Reading." :
          summary.readingPercentage > summary.listeningPercentage ? "Reading hiện tốt hơn Listening." :
            "Listening và Reading hiện cân bằng."}</p></section>
    <section className="exam-score placement-stage"><h2>Giai đoạn KNN đề xuất</h2>
      <strong>{stage ? `Giai đoạn ${stage}` : "Chưa thể xác định giai đoạn"}</strong>
      <p>{stage ? `Năng lực hiện tại phù hợp với ${courseStage[stage]?.name}. ${courseStage[stage]?.description}` :
        "Kết quả bài làm đã được lưu; chưa có đề xuất giai đoạn cho lượt này."}</p>
      <p className="exam-card-note">Giai đoạn dựa trên kết quả 7 Part của lượt làm này, không được suy ra từ điểm TOEIC tổng.</p></section>
    {stage && coursesLoaded && matchingStageCourses.length > 0 && <section className="placement-stage-destination">
      <p>Có <strong>{matchingStageCourses.length} khóa học</strong> đang mở thuộc giai đoạn {stage}. Bạn có thể so sánh và tự chọn khóa phù hợp; đề xuất không tự mở quyền truy cập.</p>
      <Link className="primary-button" to="/courses">Xem các khóa học phù hợp</Link>
    </section>}
    {stage && coursesLoaded && matchingStageCourses.length === 0 &&
      <p className="exam-card-note" role="status">Chưa có khóa học đang mở cho giai đoạn {stage}, nên chưa có trang lộ trình phù hợp để xem.</p>}
    {stage && current && onTargetSubmit && <section><h2>Mục tiêu TOEIC của bạn</h2>
      <p>Chọn số điểm bạn muốn hướng tới để hệ thống đề xuất khóa học phù hợp.</p>
      <form className="exam-target-form" onSubmit={onTargetSubmit}>
        <label htmlFor="placement-target">Điểm TOEIC mục tiêu</label>
        <input id="placement-target" type="number" min="10" max="990" required value={target}
          onChange={event => onTargetChange(event.target.value)} />
        <button className="primary-button" type="submit" disabled={pending}>Xem khóa học đề xuất</button>
      </form></section>}
    {stage && !current && <p className="exam-card-note">Điểm mục tiêu và lộ trình hiện tại thuộc lần kiểm tra mới nhất; bạn có thể cập nhật tại <Link to="/placement">trang lộ trình học</Link>.</p>}
    {stage && displayedCourses.length > 0 && <section><h2>{recommended.length ? "Lộ trình khóa học đề xuất" : "Khóa học đang mở phù hợp giai đoạn"}</h2>
      <p>{recommended.length ? "Các khóa học được backend đề xuất theo giai đoạn KNN và điểm mục tiêu đã xác nhận." :
        `Các khóa học giai đoạn ${stage} đang mở; xác nhận điểm mục tiêu để nhận lộ trình đề xuất.`}</p>
      <div className="learning-path">{displayedCourses.map((course, index) => <article className="learning-path-step" key={course.courseId}>
        <span className="learning-path-number" aria-hidden="true">{index + 1}</span>
        <div className="learning-path-content"><div className="learning-path-labels"><span className="small-badge">{courseAudienceLabel(course.stage)}</span>
          {course.stage === stage && <span className="learning-path-indicator">Phù hợp để bắt đầu</span>}
          {current && course.recommendationOrder && <span className="learning-path-indicator">Bước {course.recommendationOrder} đề xuất</span>}</div>
          <h3>{course.name}</h3>
          <p className="learning-path-course-score"><span>{courseEntryReference(course.stage)}</span>
            <strong>{courseTargetLabel(course.maxTargetScore)}</strong></p>
          <p>{course.stage === stage ? "Giai đoạn khóa học khớp với giai đoạn KNN của lượt làm này." :
            `Bước tiếp theo theo lộ trình backend đề xuất để hướng tới mục tiêu ${targetScore} điểm.`}</p>
          <p>{course.description || courseStage[course.stage]?.description}</p>
          <p>{course.accessStatus === "LOCKED" ? "Khóa học chưa được mở quyền truy cập." : "Xem quyền truy cập trong chi tiết khóa học."}</p>
          <Link className="outline-button" to={`/courses/${course.courseId}`}>Xem khóa học này</Link>
        </div></article>)}</div></section>}
    {stage && coursesLoaded && matchingStageCourses.length > 0 && displayedCourses.length === 0 &&
      <p className="exam-card-note">Chưa có lộ trình theo điểm mục tiêu đã xác nhận; bạn vẫn có thể xem đề cương giai đoạn {stage} bằng nút bên trên.</p>}
  </div>;
}
