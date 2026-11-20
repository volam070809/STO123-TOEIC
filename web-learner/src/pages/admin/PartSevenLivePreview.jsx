import { createContext, useContext, useEffect, useState } from "react";
import { API_BASE_URL } from "../../services/api";
import DocumentRenderer from "../exam/DocumentRenderer";
import { documentPayload } from "./partSevenDocumentData";

const PreviewContext = createContext(null);

function AdminPreviewImage({ endpoint, alt }) {
  const { bank, token } = useContext(PreviewContext);
  const [url, setUrl] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    if (!endpoint) return undefined;
    const controller = new AbortController();
    let objectUrl = "";
    fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ path: endpoint, bank })}`,
      { headers: { Authorization: `Bearer ${token}` }, signal: controller.signal })
      .then(response => { if (!response.ok) throw new Error("Image unavailable"); return response.blob(); })
      .then(blob => { if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } })
      .catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, bank, token]);
  if (error) return <p className="exam-media-error">Không thể tải hình từ Blob.</p>;
  return url ? <img className="exam-image" src={url} alt={alt} /> : <p>Đang tải hình ảnh…</p>;
}

export default function PartSevenLivePreview({ documents, questions, text, token, bank, issues = [] }) {
  return <section className="exam-part7-live-preview" aria-label="Xem trước Part 7">
    <div className="exam-drawer-head"><div><h3>Xem trước giao diện học viên</h3>
      <small>Nội dung cập nhật khi nhập; đáp án đúng và giải thích chỉ hiển thị sau khi nộp bài.</small></div></div>
    {issues.length > 0 && <div className="exam-alert exam-alert-error" role="status"><strong>Cần hoàn thiện trước khi lưu:</strong>
      <ul>{issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul></div>}
    <PreviewContext.Provider value={{ bank, token }}>
      <div className="exam-content exam-content-grouped exam-part7-content">
        <div className="exam-source"><div className="exam-context is-open">
          {text && <p className="exam-preserve-lines">{text}</p>}
          {documents.map((item, index) => {
            const doc = documentPayload(item);
            let content = doc.content;
            if (doc.type !== "TEXT" && doc.type !== "IMAGE") {
              try { content = JSON.parse(doc.content); } catch { content = null; }
            }
            return <DocumentRenderer key={index} document={{ type: doc.type, order: index + 1,
              content, imageUrl: doc.imagePath }} ImageComponent={AdminPreviewImage} />;
          })}
        </div></div>
        <div className="exam-group-questions">{questions.map((question, index) => <section className="exam-question" key={index}>
          <div className="exam-question-title"><h3>Câu {index + 1}</h3></div>
          <p className="exam-preserve-lines">{question.text || "Nội dung câu hỏi sẽ hiển thị ở đây"}</p>
          <div className="exam-options">{["a", "b", "c", "d"].map(letter => <div className="exam-option" key={letter}>
            <input type="radio" disabled aria-label={`Đáp án ${letter.toUpperCase()}`} />
            <span><strong>{letter.toUpperCase()}.</strong> {question[letter]}</span>
          </div>)}</div>
        </section>)}</div>
      </div>
    </PreviewContext.Provider>
  </section>;
}
