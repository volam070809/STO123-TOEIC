import { API_BASE_URL } from "./api";

export const courseImageLibrary = [
  { path: "/images/home/foundation.jpg", label: "Nền tảng TOEIC" },
  { path: "/images/home/listening.jpg", label: "Luyện nghe TOEIC" },
  { path: "/images/home/students.jpg", label: "Học viên TOEIC" },
  { path: "/images/home/courses-card.webp", label: "Khóa học TOEIC" },
  { path: "/images/home/learner-study.webp", label: "Học tập tập trung" },
  { path: "/images/home/study-notes.webp", label: "Ghi chú học tập" },
  { path: "/images/home/hero-study.webp", label: "Không gian học tập" },
];

export function courseImageSource(path) {
  if (!path) return "";
  return path.startsWith("/api/public/course-images/") ? `${API_BASE_URL || ""}${path}` : path;
}
