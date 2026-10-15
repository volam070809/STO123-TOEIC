import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { CourseCover, courseStage } from "./coursePresentation";
import { hasRoadmapContent } from "./courseOutline";
import "../../styles/course.css";

export default function CoursePage() {
  const { courseId } = useParams();
  const { token } = useAuth();
  const [view, setView] = useState({ token: undefined, courseId: null,
    course: null, error: "" });
  const { course, error } = view.token === token && view.courseId === courseId ?
    view : { course: null, error: "" };
  useEffect(() => {
    let live = true;
    placementApi.course(courseId, token).then(data => { if (live) setView({ token,
      courseId, course: data, error: "" }); })
      .catch(e => { if (live) setView({ token, courseId, course: null,
        error: e.data?.message || "Không thể tải khóa học." }); });
    return () => { live = false; };
  }, [courseId, token]);
  const info = courseStage[course?.stage];
  const hasContent = hasRoadmapContent(course);
  return <SiteLayout><div className="site-container course-page">
    <Link className="course-back" to="/courses">Tất cả khóa học</Link>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!course && !error && <p>Đang tải khóa học…</p>}
    {course && <>
      <div className="course-detail-hero">
        <CourseCover course={course} />
        <div className="course-detail-intro"><div className="course-badges"><span className="course-stage-badge">Giai đoạn {course.stage}</span>
            {course.recommended && <span className="course-recommend-badge">Đề xuất theo kết quả mới nhất</span>}</div>
          <h1>{course.name}</h1>
          <p className="course-detail-range">Mức điểm tham khảo: <strong>{info?.range || "Đang cập nhật"}</strong></p>
          <p className="course-detail-description">{course.description || info?.description}</p>
          <div className="course-detail-actions"><p className="course-access">Khóa học đang khóa</p></div>
        </div>
      </div>
      <div className={`course-detail-layout ${hasContent ? "" : "is-empty"}`}>
        <section className="course-curriculum"><div className="course-curriculum-heading">
          <h2>Đề cương khóa học</h2>
          {hasContent && <p>{course.contentCount} nội dung</p>}</div>
          {!hasContent && <div className="course-empty-state" role="status">
            <h3>Chưa có nội dung để xem</h3>
            <p>Đề cương sẽ xuất hiện tại đây khi khóa học có nội dung được cập nhật.</p>
          </div>}
          {course.units.length ? course.units.map((unit, index) => <section className="course-unit" key={index}>
            <div className="course-unit-heading"><span>UNIT {String(index + 1).padStart(2, "0")}</span>
              <h3>{unit.name}</h3></div>
            {unit.lessons.map((lesson, lessonIndex) => <div className="course-lesson" key={lessonIndex}>
              <h4>{lesson.name}</h4>
              <ul>{lesson.contents.map((content, contentIndex) =>
                <li className="is-locked" key={contentIndex}>
                  <span>{content.title}</span><span className="course-step-state">Đang khóa</span></li>)}</ul>
            </div>)}
          </section>) : null}
        </section>
        {hasContent && <aside className="course-detail-panel">
          <h2>Xem trước khóa học</h2>
          <p>Bạn có thể xem đề cương. Nội dung bài học hiện đang khóa.</p>
          <div className="course-locked-panel"><strong>Nội dung chưa được mở khóa</strong>
            <p>Nội dung bài học sẽ được cập nhật sau.</p></div>
        </aside>}
      </div>
    </>}
  </div></SiteLayout>;
}
