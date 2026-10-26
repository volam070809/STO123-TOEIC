import { useState } from "react";

import { courseImageSource } from "../../services/courseImage";
import { courseStage } from "./coursePresentationData";

const stagePhotos = {
  1: "/images/home/foundation.jpg",
  2: "/images/home/listening.jpg",
  3: "/images/home/students.jpg",
};

export function CourseCover({ course }) {
  const [failedSource, setFailedSource] = useState(null);
  const stage = courseStage[course.stage];
  const stagePhoto = stagePhotos[course.stage];
  const savedSource = courseImageSource(course.coverImageUrl);
  const source = savedSource && failedSource !== savedSource ?
    savedSource : failedSource !== stagePhoto ? stagePhoto : null;
  return <div className={`course-cover course-cover-${course.stage}`}>
    {source ? <img src={source} alt="" onError={() => setFailedSource(source)} /> :
      <div className="course-cover-fallback" aria-hidden="true"><span>STO123</span><strong>{stage?.name || "TOEIC"}</strong><small>TOEIC LEARNING</small></div>}
  </div>;
}
