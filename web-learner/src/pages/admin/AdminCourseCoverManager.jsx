import { useEffect, useRef, useState } from "react";
import { courseImageSource } from "../../services/courseImage";
import { adminCourseApi } from "../../services/adminCourseApi";
import { courseImageUpdatePayload } from "./adminCourseFormData";
import "../../styles/admin-course-cover.css";

const maxBytes = 5 * 1024 * 1024;
const types = new Set(["image/jpeg", "image/png", "image/webp"]);
const suffix = /\.(jpe?g|png|webp)$/i;

export default function AdminCourseCoverManager({ course, token, onSaved }) {
  const input = useRef(null);
  const [file, setFile] = useState(null);
  const [preview, setPreview] = useState("");
  const [failedSource, setFailedSource] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const savedSource = courseImageSource(course.coverImageUrl);
  const imageSource = preview || savedSource;
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  function choose(event) {
    const next = event.target.files?.[0];
    if (!next) return;
    if (!types.has(next.type) || !suffix.test(next.name) || next.size < 1 || next.size > maxBytes) {
      setError("Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, dung lượng tối đa 5 MB.");
      event.target.value = "";
      return;
    }
    setFile(next);
    setPreview(URL.createObjectURL(next));
    setFailedSource("");
    setError("");
    setConfirmRemove(false);
  }

  function clearSelection() {
    setFile(null);
    setPreview("");
    setError("");
    if (input.current) input.current.value = "";
  }

  async function saveImage(remove = false) {
    if (busy || (!remove && !file)) return;
    setBusy(true);
    setError("");
    try {
      await adminCourseApi.update(course.courseId,
        courseImageUpdatePayload(course, remove ? null : course.coverImageUrl), token, remove ? null : file);
      clearSelection();
      setConfirmRemove(false);
      onSaved(remove ? "Đã xóa ảnh bìa khóa học." : "Đã cập nhật ảnh bìa khóa học.");
    } catch (failure) {
      setError(failure.data?.errors?.coverImageUrl || failure.data?.message ||
        "Không thể cập nhật ảnh bìa. Vui lòng thử lại.");
    } finally { setBusy(false); }
  }

  return <section className="admin-panel admin-course-cover-manager" aria-labelledby="admin-course-cover-title">
    <div className="admin-section-heading"><div><h2 id="admin-course-cover-title">Ảnh bìa khóa học</h2>
      <p>Ảnh được dùng cho thẻ khóa học và trang giới thiệu của học viên.</p></div></div>
    <div className="admin-course-cover-layout"><div className="admin-course-cover-preview">
      {imageSource && failedSource !== imageSource ? <img src={imageSource} alt={`Ảnh bìa ${course.name}`}
        onError={() => setFailedSource(imageSource)} /> : <span>{imageSource ? "Không thể hiển thị ảnh này." : "Khóa học chưa có ảnh bìa."}</span>}
    </div><div className="admin-course-cover-controls">
      <strong>{file ? "Ảnh mới đã chọn" : course.coverImageUrl ? "Ảnh đang sử dụng" : "Chưa có ảnh bìa"}</strong>
      {file && <span>{file.name}</span>}
      <p>Chọn ảnh JPEG, PNG hoặc WebP; tối đa 5 MB. Ảnh mới chỉ thay ảnh hiện tại sau khi lưu thành công.</p>
      <label htmlFor="admin-course-cover-file" className="admin-button admin-button-secondary">{course.coverImageUrl ? "Chọn ảnh thay thế" : "Thêm ảnh bìa"}</label>
      <input ref={input} id="admin-course-cover-file" type="file" accept="image/jpeg,image/png,image/webp"
        disabled={busy} onChange={choose} />
      <div className="admin-course-cover-actions">{file && <><button className="admin-button" type="button" disabled={busy}
        onClick={() => saveImage()}>{busy ? "Đang lưu ảnh…" : "Lưu ảnh"}</button>
        <button className="admin-button admin-button-secondary" type="button" disabled={busy} onClick={clearSelection}>Hủy chọn</button></>}
        {course.coverImageUrl && !file && <button className="admin-button admin-button-danger" type="button" disabled={busy}
          onClick={() => setConfirmRemove(true)}>Xóa ảnh bìa</button>}</div>
      {confirmRemove && <div className="admin-course-cover-confirm" role="group" aria-label="Xác nhận xóa ảnh bìa">
        <p>Xóa ảnh bìa của khóa học này? Thẻ khóa học sẽ hiển thị ảnh dự phòng.</p>
        <button className="admin-button admin-button-danger" type="button" disabled={busy}
          onClick={() => saveImage(true)}>{busy ? "Đang xóa…" : "Xác nhận xóa"}</button>
        <button className="admin-button admin-button-secondary" type="button" disabled={busy}
          onClick={() => setConfirmRemove(false)}>Hủy</button></div>}
      {error && <p className="admin-feedback is-error" role="alert">{error}</p>}
    </div></div>
  </section>;
}
