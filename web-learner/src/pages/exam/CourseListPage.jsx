import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { CourseCover, courseStage } from "./coursePresentation";
import "../../styles/course.css";

export default function CourseListPage() {
  const { token } = useAuth();
  const [view, setView] = useState({ token: undefined, courses: null,
    placement: null, error: "" });
  const { courses, placement, error } = view.token === token ? view :
    { courses: null, placement: null, error: "" };
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.courses(token), placementApi.summary(token)])
      .then(([data, current]) => { if (live) setView({ token, courses: data,
        placement: current, error: "" }); })
      .catch(() => { if (live) setView({ token, courses: null,
        placement: null, error: "Không thể tải danh sách khóa học." }); });
    return () => { live = false; };
  }, [token]);
  const hasRecommendation = courses?.some(course => course.recommended);
  return <SiteLayout><div className="site-container course-page">
    <div className="page-heading course-page-heading"><div><h1>Khóa học</h1>
      <p>Khám phá nội dung từng giai đoạn.</p></div></div>
    {courses && <p className="course-intro">{hasRecommendation ?
      "Dựa trên năng lực hiện tại và mục tiêu đã xác nhận, các khóa học đề xuất đã được đánh dấu theo thứ tự." :
      placement?.currentStage && placement.targetScore == null ?
        <>Bạn đã có kết quả kiểm tra đầu vào. <Link to="/placement">Xác nhận điểm mục tiêu</Link> để xem khóa học đề xuất.</> :
        <>Bạn có thể <Link to="/placement">làm bài kiểm tra đầu vào</Link> để nhận đề xuất phù hợp hơn.</>}</p>}
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!courses && !error && <p>Đang tải khóa học…</p>}
    {courses?.length === 0 && <p>Chưa có khóa học đang mở.</p>}
    <div className="course-grid">{courses?.map(course => {
      const info = courseStage[course.stage];
      return <article className={`course-card ${course.recommended ? "is-recommended" : ""}`} key={course.courseId}>
        <CourseCover course={course} />
        <div className="course-card-body">
          <div className="course-badges"><span className="course-stage-badge">Giai đoạn {course.stage}</span>
            {course.recommended && <span className="course-recommend-badge">Đề xuất {course.recommendationOrder}</span>}</div>
          <h2>{course.name}</h2>
          <p className="course-score-range">Mức điểm tham khảo: {info?.range || "Đang cập nhật"}</p>
          <p className="course-description">{course.description || info?.description}</p>
          <div className="course-card-bottom"><p className="course-access">Khóa học đang khóa</p>
            <div className="course-card-actions"><Link className="primary-button course-cta"
              to={`/courses/${course.courseId}`}>Xem đề cương</Link></div></div>
        </div>
      </article>;
    })}</div>
  </div></SiteLayout>;
}
