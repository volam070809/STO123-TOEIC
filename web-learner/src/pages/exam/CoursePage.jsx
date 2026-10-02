import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { CourseCover, courseStage } from "./coursePresentation";
import "../../styles/course.css";

export default function CoursePage() {
  const { courseId } = useParams();
  const { token } = useAuth();
  const [course, setCourse] = useState(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    placementApi.course(courseId, token).then(data => { if (live) setCourse(data); })
      .catch(e => { if (live) setError(e.data?.message || "Không thể tải khóa học."); });
    return () => { live = false; };
  }, [courseId, token]);
  const info = courseStage[course?.stage];
  return <SiteLayout><div className="site-container course-page">
    <Link className="course-back" to="/courses">← Tất cả khóa học</Link>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!course && !error && <p>Đang tải khóa học…</p>}
    {course && <>
      <div className="course-detail-hero">
        <CourseCover course={course} />
        <div><div className="course-badges"><span className="course-stage-badge">Giai đoạn {course.stage}</span>
          {course.recommended && <span className="course-recommend-badge">★ Được đề xuất cho bạn</span>}</div>
          <h1>{course.name}</h1><p>Mức điểm tham khảo: {info?.range}</p>
          <p>{course.description || info?.description}</p>
          <p className="course-access">🔒 Khóa học đang khóa</p>
          <p>Đề xuất học tập không tự mở khóa nội dung.</p>
        </div>
      </div>
      <div className="course-detail-layout">
        <aside className="course-curriculum"><h2>Nội dung khóa học</h2>
          <p>{course.contentCount} nội dung</p>
          {course.units.length ? course.units.map((unit, index) => <section key={index}>
            <h3>{unit.name}</h3>
            {unit.lessons.map((lesson, lessonIndex) => <div key={lessonIndex}>
              <h4>{lesson.name}</h4>
              <ul>{lesson.contents.map((content, contentIndex) =>
                <li key={contentIndex}><span>{content.title}</span><span aria-label="Đang khóa">🔒</span></li>)}</ul>
            </div>)}
          </section>) : <p>Đề cương đang được cập nhật.</p>}
        </aside>
        <section className="course-detail-panel"><h2>Xem trước khóa học</h2>
          <p>Bạn có thể xem đề cương bên trái. Nội dung bài học, bài luyện tập và bài kiểm tra vẫn đang khóa.</p>
          <div className="course-locked-panel"><span aria-hidden="true">🔒</span><strong>Nội dung chưa được mở khóa</strong>
            <p>Thông tin mở khóa và đăng ký sẽ được cập nhật sau.</p></div>
        </section>
      </div>
    </>}
  </div></SiteLayout>;
}
