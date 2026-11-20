import { useState } from "react";
import { documentTypes, isLayoutType, layoutTemplates, moveItem, newDocument } from "./partSevenDocumentData";

function RowActions({ index, count, onMove, onRemove }) {
  return <span className="exam-doc-row-actions">
    <button type="button" aria-label={`Đưa mục ${index + 1} lên`} disabled={index === 0} onClick={() => onMove(-1)}>↑</button>
    <button type="button" aria-label={`Đưa mục ${index + 1} xuống`} disabled={index === count - 1} onClick={() => onMove(1)}>↓</button>
    <button type="button" disabled={count <= 1} onClick={onRemove}>Xóa</button>
  </span>;
}

export default function PartSevenDocumentForm({ value, index, onChange, onRemove, onMove,
  canRemove, count, fileField }) {
  const [isOpen, setIsOpen] = useState(index === 0);
  const change = (key, next, mutation) => onChange({ ...value, [key]: next }, mutation);
  const update = (key, at, next) => change(key, value[key].map((item, position) => position === at ? next : item));
  const move = (key, at, direction) => change(key, moveItem(value[key], at, direction),
    { key, type: "move", index: at, direction });
  const list = (key, label, multiline = false) => <div className="exam-doc-list">
    {value[key].map((entry, at) => <div className="exam-doc-entry" key={at}>
      <div className="exam-drawer-head"><strong>{label} {at + 1}</strong><RowActions index={at} count={value[key].length}
        onMove={direction => move(key, at, direction)} onRemove={() => change(key,
          value[key].filter((_, i) => i !== at), { key, type: "delete", index: at })} /></div>
      <label>{label} {at + 1}{multiline ? <textarea rows={3} value={entry} onChange={event => update(key, at, event.target.value)} /> :
        <input value={entry} onChange={event => update(key, at, event.target.value)} />}</label>
    </div>)}
    <button type="button" onClick={() => change(key, [...value[key], ""], { key, type: "add" })}>Thêm {label.toLowerCase()}</button>
  </div>;
  const table = <div className="exam-doc-table-editor">
    <div className="exam-author-two">{value.headers.map((header, at) => <div key={at} className="exam-doc-entry">
      <div className="exam-drawer-head"><strong>Cột {at + 1}</strong><RowActions index={at} count={value.headers.length}
        onMove={direction => onChange({ ...value, headers: moveItem(value.headers, at, direction),
          rows: value.rows.map(row => moveItem(row, at, direction)) }, { key: "headers", type: "move", index: at, direction })}
        onRemove={() => onChange({ ...value, headers: value.headers.filter((_, i) => i !== at),
          rows: value.rows.map(row => row.filter((_, i) => i !== at)) }, { key: "headers", type: "delete", index: at })} /></div>
      <label>Tên cột<input value={header} onChange={event => update("headers", at, event.target.value)} /></label>
    </div>)}</div>
    <button type="button" disabled={value.headers.length >= 8} onClick={() => onChange({ ...value,
      headers: [...value.headers, ""], rows: value.rows.map(row => [...row, ""]) },
      { key: "headers", type: "add" })}>Thêm cột</button>
    {value.rows.map((row, at) => <div className="exam-doc-entry" key={at}>
      <div className="exam-drawer-head"><strong>Dòng {at + 1}</strong><RowActions index={at} count={value.rows.length}
        onMove={direction => move("rows", at, direction)}
        onRemove={() => change("rows", value.rows.filter((_, i) => i !== at),
          { key: "rows", type: "delete", index: at })} /></div>
      <div className="exam-author-two">{row.map((cell, col) => <label key={col}>{value.headers[col] || `Cột ${col + 1}`}
        <input value={cell} onChange={event => update("rows", at, row.map((item, i) => i === col ? event.target.value : item))} />
      </label>)}</div>
    </div>)}
    <button type="button" onClick={() => change("rows", [...value.rows, value.headers.map(() => "")],
      { key: "rows", type: "add" })}>Thêm dòng</button>
  </div>;
  const layout = layoutTemplates[value.type];
  return <details className="exam-author-doc" open={isOpen} onToggle={event => setIsOpen(event.currentTarget.open)}>
    <summary><strong>Tài liệu {index + 1} · {documentTypes[value.type] || value.type}</strong></summary>
    <div className="exam-author-doc-body">
      <div className="exam-doc-toolbar"><button type="button" disabled={index === 0} onClick={() => onMove(-1)}>↑ Lên</button>
        <button type="button" disabled={index === count - 1} onClick={() => onMove(1)}>↓ Xuống</button>
        <button className="exam-doc-remove" type="button" disabled={!canRemove} onClick={onRemove}>Xóa tài liệu</button></div>
      {value._parseError && <p role="alert" className="exam-alert exam-alert-error">Tài liệu cũ không đọc được. Chọn mẫu khác để tạo lại trước khi lưu.</p>}
      <label>Loại tài liệu<select value={value.type} onChange={event => onChange(newDocument(event.target.value))}>
        {Object.entries(documentTypes).map(([type, label]) => <option key={type} value={type}>{label}</option>)}
      </select></label>
      {value.type === "TEXT" && <label>Nội dung<textarea value={value.content} rows={5}
        onChange={event => change("content", event.target.value)} /></label>}
      {value.type === "EMAIL" && <><div className="exam-author-two">{[["from", "Người gửi"], ["to", "Người nhận"],
        ["date", "Ngày"], ["subject", "Chủ đề"]].map(([key, label]) => <label key={key}>{label}
          <input value={value[key]} onChange={event => change(key, event.target.value)} /></label>)}</div>
        <label>Nội dung email<textarea rows={5} value={value.body} onChange={event => change("body", event.target.value)} /></label></>}
      {(value.type === "TABLE" || isLayoutType(value.type)) && <>
        <label>Tiêu đề<input value={value.title} onChange={event => change("title", event.target.value)} /></label>
        {layout?.subtitle && <label>Tiêu đề phụ<input value={value.subtitle}
          onChange={event => change("subtitle", event.target.value)} /></label>}
        {layout?.meta && <div className="exam-author-two">{value.metadata.map((item, at) => <div className="exam-doc-entry" key={at}>
          <div className="exam-drawer-head"><strong>Thông tin {at + 1}</strong><button type="button"
            onClick={() => change("metadata", value.metadata.filter((_, i) => i !== at),
              { key: "metadata", type: "delete", index: at })}>Xóa</button></div>
          <label>Nhãn<input value={item.label} onChange={event => update("metadata", at, { ...item, label: event.target.value })} /></label>
          <label>Giá trị<input value={item.value} onChange={event => update("metadata", at, { ...item, value: event.target.value })} /></label>
        </div>)}<button type="button" onClick={() => change("metadata", [...value.metadata, { label: "", value: "" }],
          { key: "metadata", type: "add" })}>Thêm thông tin</button></div>}
        {layout?.paragraphs && list("paragraphs", "Đoạn văn", true)}
        {layout?.items && list("items", value.type === "SURVEY" ? "Câu khảo sát" : "Mục")}
        {(value.type === "TABLE" || layout?.table) && table}
        {layout?.footer && <label>Ghi chú hoặc chữ ký<textarea rows={2} value={value.footer}
          onChange={event => change("footer", event.target.value)} /></label>}
      </>}
      {value.type === "CHAT" && <>{value.messages.map((message, at) => <div className="exam-doc-entry" key={at}>
        <div className="exam-drawer-head"><strong>Tin nhắn {at + 1}</strong><RowActions index={at} count={value.messages.length}
          onMove={direction => move("messages", at, direction)}
          onRemove={() => change("messages", value.messages.filter((_, i) => i !== at),
            { key: "messages", type: "delete", index: at })} /></div>
        <div className="exam-author-two"><label>Người nói<input value={message.sender}
          onChange={event => update("messages", at, { ...message, sender: event.target.value })} /></label>
          <label>Thời gian<input value={message.time}
            onChange={event => update("messages", at, { ...message, time: event.target.value })} /></label></div>
        <label>Nội dung<textarea rows={2} value={message.text}
          onChange={event => update("messages", at, { ...message, text: event.target.value })} /></label>
      </div>)}<button type="button" onClick={() => change("messages", [...value.messages, { sender: "", time: "", text: "" }],
        { key: "messages", type: "add" })}>Thêm tin nhắn</button></>}
      {value.type === "FORM" && <><label>Tiêu đề biểu mẫu<input value={value.title}
        onChange={event => change("title", event.target.value)} /></label>
        {value.fields.map((field, at) => <div className="exam-doc-entry" key={at}>
          <div className="exam-drawer-head"><strong>Trường {at + 1}</strong><RowActions index={at} count={value.fields.length}
            onMove={direction => move("fields", at, direction)}
            onRemove={() => change("fields", value.fields.filter((_, i) => i !== at),
              { key: "fields", type: "delete", index: at })} /></div>
          <div className="exam-author-two"><label>Nhãn<input value={field.label}
            onChange={event => update("fields", at, { ...field, label: event.target.value })} /></label>
            <label>Giá trị<input value={field.value}
              onChange={event => update("fields", at, { ...field, value: event.target.value })} /></label></div>
        </div>)}<button type="button" onClick={() => change("fields", [...value.fields, { label: "", value: "" }],
          { key: "fields", type: "add" })}>Thêm trường</button></>}
      {fileField("Hình tài liệu (có thể kèm văn bản hoặc bảng)", value.imagePath,
        path => change("imagePath", path), ".jpg,.jpeg,.png,.webp,image/*", `doc-${index}`)}
    </div>
  </details>;
}
