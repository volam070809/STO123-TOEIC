import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { CourseCover, courseStage } from "./coursePresentation";
import "../../styles/course.css";

export default function CourseListPage() {
  const { token } = useAuth();
  const [courses, setCourses] = useState(null);
  const [placement, setPlacement] = useState(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let live = true;
    Promise.all([placementApi.courses(token), placementApi.result(token)
      .catch(e => e.status === 404 ? null : Promise.reject(e))])
      .then(([data, current]) => { if (live) { setCourses(data); setPlacement(current); } })
      .catch(() => { if (live) setError("Không thể tải danh sách khóa học."); });
    return () => { live = false; };
  }, [token]);
  const hasRecommendation = courses?.some(course => course.recommended);
  return <SiteLayout><div className="site-container course-page">
    <div className="page-heading"><span>Học tập</span><h1>Khóa học</h1>
      <p>Chọn khóa học phù hợp với mục tiêu của bạn.</p></div>
    {courses && <p className="course-intro">{hasRecommendation ?
      "Dựa trên năng lực hiện tại và mục tiêu đã xác nhận, các khóa học đề xuất đã được đánh dấu theo thứ tự." :
      placement?.stage && placement.targetScore == null ?
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
            {course.recommended && <span className="course-recommend-badge">★ Bước {course.recommendationOrder} đề xuất</span>}</div>
          <h2>{course.name}</h2>
          <p className="course-score-range">Mức điểm tham khảo: {info?.range || "Đang cập nhật"}</p>
          <p className="course-description">{course.description || info?.description}</p>
          <p className="course-access">🔒 Khóa học đang khóa</p>
          {/* <Link className="primary-button course-cta" to={`/courses/${course.courseId}`}>Xem khóa học</Link> */}
          {/* Chỉnh lại link nút bấm */}
          {course.dangSuDungGoi ? (
            <Link
              className="primary-button course-cta"
              to={`/courses/${course.courseId}`}
            >
              Vào học
            </Link>
          ) : (
            <Link
              className="primary-button course-cta"
              to={`/goi-hoc/${course.maGoiHoc}`}
            >
              Mua khóa học
            </Link>
          )}
        </div>
      </article>;
    })}</div>
  </div></SiteLayout>;
}
