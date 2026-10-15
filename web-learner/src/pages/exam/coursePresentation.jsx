import { useState } from "react";

export const courseStage = {
  1: { name: "Foundation", range: "Dưới 450", description: "Củng cố nền tảng Listening và Reading." },
  2: { name: "Development", range: "450 - 699", description: "Phát triển kỹ năng và cải thiện các Part còn yếu." },
  3: { name: "Advanced", range: "700+", description: "Tối ưu chiến thuật và nâng cao độ chính xác." },
};

const stagePhotos = {
  1: "/images/home/foundation.jpg",
  2: "/images/home/listening.jpg",
  3: "/images/home/students.jpg",
};

export function CourseCover({ course }) {
  const [failedSource, setFailedSource] = useState(null);
  const stage = courseStage[course.stage];
  const stagePhoto = stagePhotos[course.stage];
  const source = course.coverImageUrl && failedSource !== course.coverImageUrl ?
    course.coverImageUrl : failedSource !== stagePhoto ? stagePhoto : null;
  return <div className={`course-cover course-cover-${course.stage}`}>
    {source ? <img src={source} alt="" onError={() => setFailedSource(source)} /> :
      <div className="course-cover-fallback" aria-hidden="true"><span>STO123</span><strong>{stage?.name || "TOEIC"}</strong><small>TOEIC LEARNING</small></div>}
  </div>;
}
