import { useEffect, useRef, useState } from "react";
import { courseImageLibrary, courseImageSource } from "../../services/courseImage";
import { courseStatuses } from "./adminFormat";
import { validateCourse } from "./adminCourseFormData";

const maxImageBytes = 5 * 1024 * 1024;
const supportedImageTypes = new Set(["image/jpeg", "image/png", "image/webp"]);
const supportedImageSuffix = /\.(jpe?g|png|webp)$/i;

export default function AdminCourseForm({ mode, initial, submitting, serverError, serverErrors, onSubmit }) {
  const creating = mode === "create";
  const fileInput = useRef(null);
  const [values, setValues] = useState({
    name: initial?.name ?? "",
    stage: initial?.stage?.toString() ?? "",
    maxTargetScore: initial?.maxTargetScore?.toString() ?? "",
    retailPrice: initial?.retailPrice?.toString() ?? "",
    description: initial?.description ?? "",
    status: initial?.status ?? "DANG_MO",
    coverImageUrl: initial?.coverImageUrl ?? "",
  });
  const [clientErrors, setClientErrors] = useState({});
  const [pickerMode, setPickerMode] = useState("upload");
  const [imageFile, setImageFile] = useState(null);
  const [localPreview, setLocalPreview] = useState("");
  const [failedPreview, setFailedPreview] = useState("");
  const errors = { ...serverErrors, ...clientErrors };
  const savedPreview = courseImageSource(values.coverImageUrl);
  const preview = localPreview || savedPreview;
  const previewVisible = preview && preview !== failedPreview;

  useEffect(() => () => {
    if (localPreview) URL.revokeObjectURL(localPreview);
  }, [localPreview]);

  function change(field, value) {
    setValues(current => ({ ...current, [field]: value }));
    setClientErrors(current => ({ ...current, [field]: "" }));
  }

  function chooseFile(event) {
    const file = event.target.files?.[0] ?? null;
    if (!file) return;
    if (!supportedImageTypes.has(file.type) || !supportedImageSuffix.test(file.name)) {
      setClientErrors(current => ({ ...current,
        coverImageUrl: "Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP." }));
      event.target.value = "";
      return;
    }
    if (file.size <= 0 || file.size > maxImageBytes) {
      setClientErrors(current => ({ ...current,
        coverImageUrl: "Ảnh phải có dung lượng từ 1 byte đến 5 MB." }));
      event.target.value = "";
      return;
    }
    setImageFile(file);
    setLocalPreview(URL.createObjectURL(file));
    setFailedPreview("");
    setValues(current => ({ ...current, coverImageUrl: "" }));
    setClientErrors(current => ({ ...current, coverImageUrl: "" }));
  }

  function chooseLibraryImage(path) {
    setImageFile(null);
    setLocalPreview("");
    setFailedPreview("");
    if (fileInput.current) fileInput.current.value = "";
    change("coverImageUrl", path);
  }

  function removeImage() {
    setImageFile(null);
    setLocalPreview("");
    setFailedPreview("");
    if (fileInput.current) fileInput.current.value = "";
    change("coverImageUrl", "");
  }

  function submit(event) {
    event.preventDefault();
    const nextErrors = validateCourse(values);
    setClientErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;
    onSubmit({
      name: values.name.trim(),
      stage: Number(values.stage),
      maxTargetScore: Number(values.maxTargetScore),
      retailPrice: values.retailPrice === "" ? null : Number(values.retailPrice),
      description: values.description.trim() || null,
      status: values.status,
      coverImageUrl: values.coverImageUrl.trim() || null,
    }, imageFile);
  }

  return <form className="admin-form" onSubmit={submit} noValidate>
    <div className="admin-form-grid">
      <Field id="course-name" label="Tên khóa học" required error={errors.name} wide>
        <input id="course-name" value={values.name} maxLength={128} onChange={event => change("name", event.target.value)} />
      </Field>
      <Field id="course-stage" label="Giai đoạn" required error={errors.stage} hint="Mỗi khóa học thuộc một trong ba giai đoạn; một giai đoạn có thể có nhiều khóa học.">
        <select id="course-stage" value={values.stage} onChange={event => change("stage", event.target.value)}>
          <option value="">Chọn giai đoạn</option><option value="1">Giai đoạn 1</option>
          <option value="2">Giai đoạn 2</option><option value="3">Giai đoạn 3</option>
        </select>
      </Field>
      <Field id="course-score" label="Điểm mục tiêu tối đa" required error={errors.maxTargetScore} hint="Giá trị hợp lệ từ 10 đến 990.">
        <input id="course-score" type="number" min="10" max="990" step="1" value={values.maxTargetScore}
          onChange={event => change("maxTargetScore", event.target.value)} />
      </Field>
      <Field id="course-price" label="Giá bán lẻ" error={errors.retailPrice} hint="Để trống nếu khóa học chưa bán riêng; giá dùng đơn vị VND và không có phần thập phân.">
        <input id="course-price" type="number" min="1" max="9999999999" step="1" value={values.retailPrice}
          onChange={event => change("retailPrice", event.target.value)} />
      </Field>
      <Field id="course-status" label="Trạng thái" required error={errors.status}>
        <select id="course-status" value={values.status} onChange={event => change("status", event.target.value)}>
          {Object.entries(courseStatuses).map(([value, label]) => <option value={value} key={value}>{label}</option>)}
        </select>
      </Field>

      <fieldset className={`admin-course-image-picker admin-field-wide ${errors.coverImageUrl ? "has-error" : ""}`}>
        <legend>Ảnh đại diện khóa học</legend>
        <p className="admin-image-picker-help">Tải ảnh JPEG, PNG hoặc WebP tối đa 5 MB, hoặc chọn một ảnh có sẵn của STO123.</p>
        <div className="admin-image-picker-tabs" role="group" aria-label="Nguồn ảnh đại diện">
          <button type="button" className={pickerMode === "upload" ? "is-active" : ""}
            aria-pressed={pickerMode === "upload"} onClick={() => setPickerMode("upload")}>Tải ảnh từ máy</button>
          <button type="button" className={pickerMode === "library" ? "is-active" : ""}
            aria-pressed={pickerMode === "library"} onClick={() => setPickerMode("library")}>Chọn ảnh có sẵn</button>
        </div>

        <div className="admin-image-picker-layout">
          <div className="admin-image-picker-control">
            {pickerMode === "upload" ? <div className="admin-image-upload">
              <label htmlFor="course-image-upload">Chọn tệp ảnh</label>
              <input ref={fileInput} id="course-image-upload" type="file" accept="image/jpeg,image/png,image/webp"
                onChange={chooseFile} />
              <small>{imageFile ? `${imageFile.name} · ${formatFileSize(imageFile.size)}` : "Chưa chọn tệp mới."}</small>
              <small>Ảnh chỉ được tải lên khi bạn lưu biểu mẫu.</small>
            </div> : <div className="admin-image-library" aria-label="Thư viện ảnh có sẵn">
              {courseImageLibrary.map(image => <button type="button" key={image.path}
                className={!imageFile && values.coverImageUrl === image.path ? "is-selected" : ""}
                aria-pressed={!imageFile && values.coverImageUrl === image.path}
                onClick={() => chooseLibraryImage(image.path)}>
                <img src={image.path} alt="" /><span>{image.label}</span>
              </button>)}
            </div>}
          </div>

          <div className="admin-image-preview-panel">
            <span className="admin-image-preview-label">Xem trước</span>
            <div className="admin-image-preview">
              {previewVisible ? <img src={preview} alt="Ảnh đại diện khóa học đang chọn"
                onError={() => setFailedPreview(preview)} /> :
                <span>{preview ? "Không thể hiển thị ảnh hiện tại." : "Chưa chọn ảnh đại diện."}</span>}
            </div>
            {(imageFile || values.coverImageUrl) && <button className="admin-image-remove" type="button" onClick={removeImage}>
              Bỏ chọn ảnh
            </button>}
          </div>
        </div>
        {errors.coverImageUrl && <small className="admin-field-error" role="alert">{errors.coverImageUrl}</small>}
      </fieldset>

      <Field id="course-description" label="Mô tả" error={errors.description} wide>
        <textarea id="course-description" value={values.description} maxLength={512} rows="5"
          onChange={event => change("description", event.target.value)} />
      </Field>
    </div>
    <div className="admin-form-note"><strong>Phạm vi thay đổi</strong><p>Biểu mẫu chỉ cập nhật thông tin khóa học. Unit, bài học và liên kết gói học không bị thay đổi.</p></div>
    {serverError && <div className="admin-feedback is-error" role="alert">{serverError}</div>}
    <div className="admin-form-actions"><button className="admin-button" type="submit" disabled={submitting}>
      {submitting ? "Đang lưu..." : creating ? "Tạo khóa học" : "Lưu thay đổi"}</button></div>
  </form>;
}

function Field({ id, label, required, error, hint, wide, children }) {
  return <div className={`admin-field ${error ? "has-error" : ""} ${wide ? "admin-field-wide" : ""}`}>
    <label htmlFor={id}>{label}{required && <span aria-hidden="true"> *</span>}</label>{children}
    {error ? <small className="admin-field-error">{error}</small> : hint && <small>{hint}</small>}
  </div>;
}

function formatFileSize(bytes) {
  return bytes < 1024 * 1024 ? `${Math.ceil(bytes / 1024)} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
