import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { CourseCover } from "./coursePresentation";
import { courseAudienceLabel, courseEntryReference, courseStage, courseTargetLabel } from "./coursePresentationData";
import { hasRoadmapContent } from "./courseOutline";
import { publicCourseApi } from "../../services/publicCourseApi";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { requestGuestCoursePurchase } from "./coursePublicAccess";
import "../../styles/course.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

export default function CoursePage() {
  const { courseId } = useParams();
  const { token } = useAuth();
  const { requireAuthentication } = useGuestAuthPrompt();
  const isGuest = !token;
  const [view, setView] = useState({ token: undefined, courseId: null,
    course: null, error: "" });
  const { course, error } = view.token === token && view.courseId === courseId ?
    view : { course: null, error: "" };
  useEffect(() => {
    let live = true;
    const request = token ? placementApi.course(courseId, token) : publicCourseApi.detail(courseId);
    request.then(data => { if (live) setView({ token,
      courseId, course: data, error: "" }); })
      .catch(e => { if (live) setView({ token, courseId, course: null,
        error: e.status === 403 ? "Khóa học đang khóa. Hãy chọn khóa học hoặc combo tại trang Khóa học để mở quyền học." :
          e.data?.message || "Không thể tải khóa học." }); });
    return () => { live = false; };
  }, [courseId, token]);
  const info = courseStage[course?.stage];
  const hasContent = hasRoadmapContent(course);
  return <SiteLayout><div className="site-container course-page">
    <Link className="course-back" to="/courses">Tất cả khóa học</Link>
    {error && <div className="course-locked-state" role="alert"><h1>Khóa học đang khóa</h1>
      <p>{error}</p><Link className="primary-button" to="/courses">Xem khóa học và combo</Link></div>}
    {!course && !error && <p>Đang tải khóa học…</p>}
    {course && <>
      <div className="course-detail-hero">
        <CourseCover course={course} />
        <div className="course-detail-intro"><div className="course-badges"><span className="course-stage-badge">{courseAudienceLabel(course.stage)}</span>
            {course.recommended && <span className="course-recommend-badge">Đề xuất theo kết quả mới nhất</span>}</div>
          <h1>{course.name}</h1>
          <div className="course-detail-score-summary">
            <p>{courseEntryReference(course.stage)}</p>
            <strong>{courseTargetLabel(course.maxTargetScore)}</strong>
            <small>Mục tiêu là định hướng của khóa học, không phải cam kết điểm thi.</small>
          </div>
          <p className="course-detail-description">{course.description || info?.description}</p>
          <div className="course-detail-actions"><p className="course-access">{isGuest ?
            `${course.retailPrice > 0 ? money.format(course.retailPrice) : "Chưa có giá bán"} · ${course.accessDays} ngày` :
            course.accessStatus === "ACTIVE" ? "Đã có quyền truy cập" : "Khóa học đang khóa"}</p>
            {isGuest && course.retailPrice > 0 && <button type="button" className="primary-button course-detail-purchase"
              onClick={() => requestGuestCoursePurchase(requireAuthentication)}>Mua khóa học</button>}</div>
        </div>
      </div>
      {!isGuest && <div className={`course-detail-layout ${hasContent ? "" : "is-empty"}`}>
        <section className="course-curriculum"><div className="course-curriculum-heading">
          <h2>Đề cương khóa học</h2>
          {hasContent && <p>{course.contentCount} nội dung</p>}</div>
          {!hasContent && <div className="course-empty-state" role="status">
            <h3>Khóa học chưa có nội dung.</h3>
            <p>Khóa học chưa có nội dung. Nội dung sẽ được cập nhật sau.</p>
          </div>}
          {course.units.length ? course.units.map((unit, index) => <section className="course-unit" key={index}>
            <div className="course-unit-heading"><span>UNIT {String(index + 1).padStart(2, "0")}</span>
              <h3>{unit.name}</h3></div>
            {unit.lessons.map((lesson, lessonIndex) => <div className="course-lesson" key={lessonIndex}>
              <h4>{lesson.name}</h4>
              <ul>{lesson.contents.map((content, contentIndex) =>
                <li key={contentIndex}><span>{content.title}</span></li>)}</ul>
            </div>)}
          </section>) : null}
        </section>
      </div>}
      {isGuest && <section className="course-section course-public-overview"><h2>Thông tin khóa học</h2>
        <p>Giai đoạn thể hiện trình độ đầu vào phù hợp; mục tiêu điểm là định hướng riêng của khóa học.</p>
        <p>Nội dung bài học và tài liệu chỉ hiển thị sau khi tài khoản có quyền truy cập hợp lệ.</p>
      </section>}
    </>}
  </div></SiteLayout>;
}
