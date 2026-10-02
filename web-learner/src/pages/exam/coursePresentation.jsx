import { useState } from "react";

export const courseStage = {
  1: { name: "Foundation", range: "Dưới 450", description: "Củng cố nền tảng Listening và Reading." },
  2: { name: "Development", range: "450 - 699", description: "Phát triển kỹ năng và cải thiện các Part còn yếu." },
  3: { name: "Advanced", range: "700+", description: "Tối ưu chiến thuật và nâng cao độ chính xác." },
};

export function CourseCover({ course }) {
  const [failed, setFailed] = useState(false);
  const stage = courseStage[course.stage];
  return <div className={`course-cover course-cover-${course.stage}`}>
    {course.coverImageUrl && !failed ? <img src={course.coverImageUrl} alt="" onError={() => setFailed(true)} /> :
      <div className="course-cover-fallback" aria-hidden="true"><span>STO123</span><strong>{stage?.name || "TOEIC"}</strong><small>TOEIC LEARNING</small></div>}
  </div>;
}
