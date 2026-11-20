import DocumentRenderer from "../exam/DocumentRenderer";

export default function DocumentPreview({ doc, index, title, translation = false }) {
  let content = doc.content;
  if (doc.type !== "TEXT" && doc.type !== "IMAGE") {
    try { content = JSON.parse(doc.content); } catch { content = null; }
  }
  if (doc.type === "IMAGE") return <div className="exam-author-doc"><article className="exam-document">
    <h3>{title || `Tài liệu ${index + 1}`}</h3>
    <p>{translation ? content || "Chưa có chú thích tiếng Việt." :
      `Hình ảnh: ${doc.image || doc.imagePath || "Chưa tải ảnh"}`}</p>
  </article></div>;
  return <div className="exam-author-doc"><DocumentRenderer document={{ type: doc.type, order: index + 1, content }}
    heading={title || `Tài liệu ${index + 1}`} /></div>;
}
