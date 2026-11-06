import { useState } from "react";

import { courseImageSource } from "../../services/courseImage";
import { courseStage } from "./coursePresentationData";

export function CourseCover({ course }) {
  const [failedSource, setFailedSource] = useState(null);
  const stage = courseStage[course.stage];
  const savedSource = courseImageSource(course.coverImageUrl);
  const source = savedSource && failedSource !== savedSource ? savedSource : null;
  return <div className={`course-cover course-cover-${course.stage}`}>
    {source ? <img src={source} alt={`Ảnh bìa khóa học ${course.name}`} onError={() => setFailedSource(source)} /> :
      <div className="course-cover-fallback" aria-label="Ảnh bìa khóa học chưa có"><span>STO123</span><strong>{stage?.name || "TOEIC"}</strong><small>Ảnh bìa đang cập nhật</small></div>}
  </div>;
}
