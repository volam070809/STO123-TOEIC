import { useEffect, useState } from "react";
import { API_BASE_URL } from "../../services/api";
import { alignTranslation } from "./partSevenTranslationData";
import DocumentPreview from "./ExamDocumentPreview";
import { documentPayload } from "./partSevenDocumentData";

function SourceImage({ path, token, bank }) {
  const [url, setUrl] = useState("");
  useEffect(() => {
    if (!path) return undefined;
    let active = true; let objectUrl = "";
    fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ path, bank })}`,
      { headers: { Authorization: `Bearer ${token}` } }).then(response => {
      if (!response.ok) throw new Error("Không tải được hình tài liệu.");
      return response.blob();
    }).then(blob => { objectUrl = URL.createObjectURL(blob);
      if (active) setUrl(objectUrl); else URL.revokeObjectURL(objectUrl); })
      .catch(() => { if (active) setUrl(""); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [path, token, bank]);
  return url ? <img className="exam-media-preview" src={url} alt="Hình tài liệu gốc" /> : null;
}

export default function PartSevenTranslationForm({ original, value, index, onChange, token, bank }) {
  const [isOpen, setIsOpen] = useState(index === 0);
  const translation = alignTranslation(original, value);
  const content = translation.content;
  const change = next => onChange({ ...translation, content: next });
  const originalDoc = documentPayload(original);
  return <details className="exam-author-doc exam-translation-doc" open={isOpen}
    onToggle={event => setIsOpen(event.currentTarget.open)}>
    <summary>Bản dịch tài liệu {index + 1} · {original.type}</summary>
    <div className="exam-author-doc-body">
      <div className="exam-translation-preview"><DocumentPreview doc={{ ...originalDoc, image: originalDoc.imagePath }} index={index} title="Bản gốc" /></div>
      {original.type === "IMAGE" && <SourceImage key={original.imagePath} path={original.imagePath} token={token} bank={bank} />}
      {original.type === "TEXT" && <label>Bản dịch văn bản<textarea rows={5} value={content}
        onChange={event => change(event.target.value)} /></label>}
      {original.type === "IMAGE" && <label>Chú thích tiếng Việt cho hình ảnh<textarea rows={2} value={content}
        onChange={event => change(event.target.value)} /></label>}
      {original.type === "EMAIL" && <><div className="exam-author-two">
        {[["from", "Người gửi"], ["to", "Người nhận"], ["date", "Ngày"], ["subject", "Chủ đề"]].map(([key, label]) =>
          <label key={key}>{label}<input value={content[key]} onChange={event => change({ ...content, [key]: event.target.value })} /></label>)}
      </div><label>Nội dung email<textarea rows={4} value={content.body} onChange={event => change({ ...content, body: event.target.value })} /></label></>}
      {original.type === "TABLE" && <><label>Tiêu đề bảng<input value={content.title}
        onChange={event => change({ ...content, title: event.target.value })} /></label>
        <div className="exam-table-scroll"><table className="exam-translation-table"><thead><tr>{original.headers.map((_, col) =>
          <th key={col}><label>Cột {col + 1}<input value={content.headers[col]} onChange={event => change({ ...content,
            headers: content.headers.map((item, i) => i === col ? event.target.value : item) })} /></label></th>)}</tr></thead>
          <tbody>{original.rows.map((row, at) => <tr key={at}>{row.map((_, col) => <td key={col}>
            <input aria-label={`Dòng ${at + 1}, cột ${col + 1}`} value={content.rows[at][col]}
              onChange={event => change({ ...content, rows: content.rows.map((cells, i) =>
                i === at ? cells.map((cell, j) => j === col ? event.target.value : cell) : cells) })} />
          </td>)}</tr>)}</tbody></table></div></>}
      {original.type === "CHAT" && original.messages.map((message, at) => <div className="exam-doc-entry" key={at}>
        <strong>Tin nhắn {at + 1} · {message.sender} {message.time}</strong>
        <div className="exam-author-two">{[["sender", "Người nói"], ["time", "Thời gian"]].map(([key, label]) =>
          <label key={key}>{label}<input value={content.messages[at][key]} onChange={event => change({ ...content,
            messages: content.messages.map((item, i) => i === at ? { ...item, [key]: event.target.value } : item) })} /></label>)}</div>
        <label>Nội dung tiếng Việt<textarea rows={2} value={content.messages[at].text}
          onChange={event => change({ ...content, messages: content.messages.map((item, i) =>
            i === at ? { ...item, text: event.target.value } : item) })} /></label>
      </div>)}
      {original.type === "FORM" && <><label>Tiêu đề biểu mẫu<input value={content.title}
        onChange={event => change({ ...content, title: event.target.value })} /></label>
        {original.fields.map((field, at) => <div className="exam-doc-entry" key={at}>
          <strong>Trường {at + 1} · {field.label}</strong><div className="exam-author-two">
            {[["label", "Nhãn"], ["value", "Giá trị"]].map(([key, label]) => <label key={key}>{label}
              <input value={content.fields[at][key]} onChange={event => change({ ...content,
                fields: content.fields.map((item, i) => i === at ? { ...item, [key]: event.target.value } : item) })} />
            </label>)}</div></div>)}</>}
      <DocumentPreview doc={{ type: original.type, content: original.type === "TEXT" || original.type === "IMAGE" ? content : JSON.stringify(content) }}
        index={index} title="Xem trước bản dịch" translation />
    </div>
  </details>;
}
