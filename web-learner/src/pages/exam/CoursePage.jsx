import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import { placementApi } from "../../services/examApi";
import { CourseCover } from "./coursePresentation";
import { courseAudienceLabel, courseEntryReference, courseStage, courseTargetLabel } from "./coursePresentationData";
import { hasRoadmapContent } from "./courseOutline";
import { publicCourseApi } from "../../services/publicCourseApi";
import { useContentRevision } from "../../services/contentRevisions";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { requestGuestCoursePurchase } from "./coursePublicAccess";
import "../../styles/course.css";
import "../../styles/learner-journey.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

export default function CoursePage() {
  const { courseId } = useParams();
  const { search } = useLocation();
  const requestedUnitId = Number(new URLSearchParams(search).get("unit"));
  const requestedLessonId = Number(new URLSearchParams(search).get("lesson"));
  const { token, user } = useAuth();
  const contentRevision = useContentRevision("COURSES", "ACCESS", "CURRICULUM", "LECTURES", "EXERCISES");
  const { requireAuthentication } = useGuestAuthPrompt();
  const isAdmin = user?.vaiTro === "ADMIN_QUAN_LY" || user?.vaiTro === "ADMIN_NOI_DUNG";
  const isGuest = !token || isAdmin;
  const [view, setView] = useState({ token: undefined, courseId: null,
    course: null, error: "" });
  const [selectedUnit, setSelectedUnit] = useState(0);
  const { course, error } = view.token === token && view.courseId === courseId ?
    view : { course: null, error: "" };
  useEffect(() => {
    let live = true;
    const request = !isGuest ? placementApi.course(courseId, token) : publicCourseApi.detail(courseId);
    request.then(data => { if (live) { setView({ token,
      courseId, course: data, error: "" }); const target = data.units?.findIndex(unit => unit.id === requestedUnitId);
      if (target >= 0) setSelectedUnit(target); } })
      .catch(e => { if (live) setView({ token, courseId, course: null,
        error: e.status === 403 ? "Khóa học đang khóa. Hãy chọn khóa học hoặc combo tại trang Khóa học để mở quyền học." :
          e.data?.message || "Không thể tải khóa học." }); });
    return () => { live = false; };
  }, [courseId, token, isGuest, requestedUnitId, contentRevision]);
  const info = courseStage[course?.stage];
  const hasContent = hasRoadmapContent(course);
  const allActivities = course?.units?.flatMap(unit => unit.lessons.flatMap(lesson => lesson.contents)) || [];
  const nextActivity = allActivities.find(item => !item.locked && item.progressStatus !== "HOAN_THANH") || allActivities.find(item => !item.locked);
  const activeUnit = course?.units?.[selectedUnit];
  const returnedLessonIsVisible = activeUnit?.lessons.some(lesson => lesson.id === requestedLessonId);
  const exactCourseProgress = course?.totalUnits ? 100 * course.completedUnits / course.totalUnits : 0;
  return <SiteLayout><div className="site-container course-page">
    <Link className="course-back" to="/courses">Tất cả khóa học</Link>
    {error && <div className="course-locked-state" role="alert"><h1>Khóa học đang khóa</h1>
      <p>{error}</p><Link className="primary-button" to="/courses">Xem khóa học và combo</Link></div>}
    {!course && !error && <p>Đang tải khóa học…</p>}
    {course && <>
      {isGuest ? <div className="course-detail-hero">
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
            {!isAdmin && isGuest && course.retailPrice > 0 && <button type="button" className="primary-button course-detail-purchase"
              onClick={() => requestGuestCoursePurchase(requireAuthentication)}>Mua khóa học</button>}</div>
        </div>
       </div> : <header className="course-learning-header course-learning-feature">
        <div><span className="course-eyebrow">LỘ TRÌNH HỌC TẬP · {course.units.length} UNIT</span>
          <h1>{course.name}</h1><p>{course.description || "Học theo từng bài và theo dõi tiến độ của bạn."}</p>
           <span className="course-access-status">{course.accessStatus === "ACTIVE" ? "Đang có quyền học" : "Quyền học cần được kiểm tra"}</span>
           <div className="course-overall-progress"><div><strong>Tiến độ khóa học</strong><span>{course.completedUnits}/{course.totalUnits} hoạt động · {course.totalUnits ? Math.round(exactCourseProgress) : 0}%</span></div>
             <progress max={course.totalUnits || 1} value={course.completedUnits || 0} aria-label="Tiến độ khóa học" /></div></div>
      </header>}
       {!isGuest && <>{nextActivity && <section className="course-next-activity"><span className="course-eyebrow">BƯỚC HỌC TIẾP THEO</span>
         <h2>Học tiếp từ đây</h2><p>{nextActivity.title}</p><span>{nextActivity.progressStatus === "DANG_LAM" ? "Đang học" : nextActivity.progressStatus === "HOAN_THANH" ? "Đã hoàn thành" : "Sẵn sàng bắt đầu"}</span>
         <Link className="primary-button" to={`/courses/${courseId}/steps/${nextActivity.id}`}>{nextActivity.progressStatus === "HOAN_THANH" ? "Xem lại bài học" : "Tiếp tục học"} →</Link></section>}
       <details className="learner-disclosure course-outline-details" open={requestedUnitId > 0 || requestedLessonId > 0}><summary><span><small>CHƯƠNG TRÌNH HỌC</small><strong>Nội dung và tiến độ từng Unit</strong><em>{course.units.length} Unit · {course.completedUnits}/{course.totalUnits} hoạt động đã hoàn thành</em></span><DisclosureToggle /></summary><div className="learner-disclosure-body"><div className="course-learning-layout">
        <aside className="course-unit-sidebar" aria-label="Danh sách Unit"><h2>Các Unit</h2>{course.units.map((unit, index) => {
          const steps = unit.lessons.flatMap(l => l.contents);
          return <button key={unit.id} className={selectedUnit === index ? "is-active" : ""} onClick={() => setSelectedUnit(index)}>
            <small>UNIT {String(index + 1).padStart(2, "0")} · {unit.totalUnits ? `${unit.progress}%` : "Chưa có nội dung"}</small><strong>{unit.name}</strong><small>{steps.length ? `${unit.completedUnits}/${unit.totalUnits} phần đã hoàn thành` : "Chưa có nội dung"}</small>
            <progress max={unit.totalUnits || 1} value={unit.completedUnits}/></button>; })}</aside>
        <section className="course-curriculum"><div className="course-unit-feature"><div><span className="course-eyebrow">UNIT {String(selectedUnit + 1).padStart(2, "0")}</span>
          <h2>{activeUnit?.name || "Nội dung khóa học"}</h2><p>{activeUnit?.description || `${activeUnit?.lessons.length || 0} bài học trong Unit này`}</p></div>
           <div className="course-unit-feature-progress"><strong>{activeUnit?.totalUnits ? `${activeUnit.progress}%` : "—"}</strong><small>{activeUnit?.totalUnits ? `${activeUnit.completedUnits}/${activeUnit.totalUnits} phần` : "Chưa có nội dung"}</small></div></div>
          <div className="course-curriculum-heading"><h2>Các bài học</h2>{hasContent && <p>{activeUnit?.lessons.length || 0} bài · {activeUnit?.totalUnits || 0} phần học</p>}</div>
          {!hasContent && <div className="course-empty-state" role="status">
            <h3>Khóa học chưa có nội dung.</h3>
            <p>Khóa học chưa có nội dung. Nội dung sẽ được cập nhật sau.</p>
           </div>}
          {activeUnit?.lessons.map((lesson, lessonIndex) => <details className="course-learning-lesson" key={lesson.id} open={returnedLessonIsVisible ? lesson.id === requestedLessonId : lessonIndex === 0}>
            <summary><span className="course-lesson-number">{String(lessonIndex + 1).padStart(2, "0")}</span><span className="course-lesson-title"><small>{lesson.progress === 100 ? "ĐÃ HOÀN THÀNH" : "BÀI HỌC"}</small><strong>{lesson.name}</strong></span><span className="course-lesson-progress"><strong>{lesson.progress}%</strong><small>{lesson.completedUnits}/{lesson.totalUnits} phần</small></span><svg className="course-lesson-chevron" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m5 7.5 5 5 5-5" /></svg></summary>
            <div className="course-lesson-track"><span style={{ width: `${lesson.progress}%` }}/></div>
            <ul>{lesson.contents.map((content, contentIndex) => <li key={content.id} className={content.progressStatus === "HOAN_THANH" ? "is-complete" : ""}><span className="course-activity-icon" aria-hidden="true">{content.type === "VOCABULARY" ? "Aa" : content.type === "LECTURE" ? "▶" : "✎"}</span>
              <span><small>{content.type === "EXERCISE" ? "BÀI TẬP" : "NỘI DUNG HỌC"} {String(contentIndex + 1).padStart(2, "0")}</small><strong>{content.title}</strong><small>{content.progressStatus === "HOAN_THANH" ? "✓ Đã hoàn thành" : content.progressStatus === "DANG_LAM" ? `Đang học · ${content.progress}%` : "Chưa học"}</small></span>
              {content.locked ? <span>Chưa hỗ trợ</span> : <Link to={`/courses/${courseId}/steps/${content.id}`}>{content.progressStatus === "HOAN_THANH" ? "Xem lại" : content.progressStatus === "DANG_LAM" ? "Tiếp tục" : "Bắt đầu"}</Link>}</li>)}</ul>
          </details>)}
        </section>
       </div></div></details></>}
      {isGuest && <section className="course-section course-public-overview"><h2>Thông tin khóa học</h2>
        <p>Giai đoạn thể hiện trình độ đầu vào phù hợp; mục tiêu điểm là định hướng riêng của khóa học.</p>
        <p>Nội dung bài học và tài liệu chỉ hiển thị sau khi tài khoản có quyền truy cập hợp lệ.</p>
      </section>}
    </>}
  </div></SiteLayout>;
}
