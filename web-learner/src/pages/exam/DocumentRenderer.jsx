import { useEffect, useState } from "react";
import { PrivateImage } from "./ExamMedia";
import { isLayoutType } from "../admin/partSevenDocumentData";

const unavailable = <p className="exam-media-error">Không thể hiển thị tài liệu này.</p>;

function TextDocument({ content }) {
  return <p className="exam-preserve-lines exam-document-body">{content}</p>;
}

function EmailDocument({ content }) {
  return <div className="exam-email">
    <dl className="exam-email-meta">
      {["to", "from", "date", "subject"].map(field =>
        content[field] && <div key={field}><dt>{field[0].toUpperCase() + field.slice(1)}</dt>
          <dd>{content[field]}</dd></div>)}
    </dl>
    <p className="exam-preserve-lines exam-document-body">{content.body}</p>
  </div>;
}

function TableDocument({ content }) {
  return <div className="exam-table-document">
    {content.title && <h4>{content.title}</h4>}
    <div className="exam-table-scroll" role="region" aria-label={content.title || "Bảng tài liệu"} tabIndex={0}>
      <table><thead><tr>{content.headers.map((header, index) =>
        <th key={index} scope="col">{header}</th>)}</tr></thead>
        <tbody>{content.rows.map((row, rowIndex) => <tr key={rowIndex}>
          {row.map((cell, cellIndex) => <td key={cellIndex}>{cell}</td>)}
        </tr>)}</tbody></table>
    </div>
  </div>;
}

function ChatDocument({ content }) {
  return <ol className="exam-chat">{content.messages.map((message, index) =>
    <li key={index}><div className="exam-chat-meta"><strong>{message.sender}</strong>
      {message.time && <time>{message.time}</time>}</div>
      <p className="exam-preserve-lines">{message.text}</p></li>)}</ol>;
}

function FormDocument({ content }) {
  return <div className="exam-form-document">
    {content.title && <h4>{content.title}</h4>}
    <dl>{content.fields.map((field, index) =>
      <div key={index}><dt>{field.label}</dt><dd className="exam-preserve-lines">{field.value}</dd></div>)}</dl>
  </div>;
}

function LayoutDocument({ type, content }) {
  return <div className={`exam-layout-document exam-layout-${type.toLowerCase()}`}>
    <header>{content.title && <h4>{content.title}</h4>}
      {content.subtitle && <p className="exam-layout-subtitle">{content.subtitle}</p>}</header>
    {content.metadata?.some(item => item.label || item.value) && <dl className="exam-layout-meta">
      {content.metadata.map((item, index) => <div key={index}><dt>{item.label}</dt><dd>{item.value}</dd></div>)}
    </dl>}
    {content.paragraphs?.map((paragraph, index) => paragraph &&
      <p className="exam-preserve-lines" key={index}>{paragraph}</p>)}
    {content.items?.some(Boolean) && <ol className="exam-layout-items">{content.items.map((item, index) =>
      item && <li className="exam-preserve-lines" key={index}>{item}</li>)}</ol>}
    {(type === "INVOICE" || type === "SCHEDULE") && <TableDocument content={{ ...content, title: "" }} />}
    {content.footer && <footer className="exam-preserve-lines">{content.footer}</footer>}
  </div>;
}

function ImageDocument({ document, token, ImageComponent }) {
  const [zoomSrc, setZoomSrc] = useState("");
  useEffect(() => {
    if (!zoomSrc) return undefined;
    const onKeyDown = event => { if (event.key === "Escape") setZoomSrc(""); };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [zoomSrc]);
  return document.imageUrl && <>
    <div className="exam-document-zoom">
      <ImageComponent endpoint={document.imageUrl} token={token} alt="Hình tài liệu" />
      <button type="button" aria-label="Phóng to hình tài liệu"
        onClick={event => { const image = event.currentTarget.parentElement.querySelector("img");
          if (image?.src) setZoomSrc(image.src); }}>Chạm để phóng to</button>
    </div>
    {zoomSrc && <div className="exam-image-lightbox" role="presentation" onMouseDown={() => setZoomSrc("")}>
      <div className="exam-image-lightbox-content" role="dialog" aria-modal="true" aria-label="Hình tài liệu phóng to"
        onMouseDown={event => event.stopPropagation()}>
        <button type="button" onClick={() => setZoomSrc("")} aria-label="Đóng hình phóng to">Đóng ✕</button>
        <img src={zoomSrc} alt="Hình tài liệu phóng to" />
      </div>
    </div>}
  </>;
}

export default function DocumentRenderer({ document, token, ImageComponent = PrivateImage, heading }) {
  const { type, content } = document ?? {};
  const structured = content && typeof content === "object" && !Array.isArray(content);
  const valid = type === "IMAGE" ? Boolean(document.imageUrl) :
    type === "TEXT" ? typeof content === "string" :
    type === "EMAIL" ? structured && typeof content.body === "string" :
    type === "TABLE" ? structured && Array.isArray(content.headers) && Array.isArray(content.rows) :
    type === "CHAT" ? structured && Array.isArray(content.messages) :
    type === "FORM" ? structured && Array.isArray(content.fields) :
    isLayoutType(type) ? structured && Array.isArray(content.metadata) &&
      Array.isArray(content.paragraphs) && Array.isArray(content.items) : false;
  return <article className="exam-document">
    <h3>{heading || `Tài liệu ${document?.order}`}</h3>
    {!valid ? unavailable : <>
      {type === "TEXT" && <TextDocument content={content} />}
      {type === "EMAIL" && <EmailDocument content={content} />}
      {type === "TABLE" && <TableDocument content={content} />}
      {type === "CHAT" && <ChatDocument content={content} />}
      {type === "FORM" && <FormDocument content={content} />}
      {isLayoutType(type) && <LayoutDocument type={type} content={content} />}
      {type === "IMAGE" && <ImageDocument document={document} token={token} ImageComponent={ImageComponent} />}
      {type !== "IMAGE" && document.imageUrl &&
        <ImageDocument document={document} token={token} ImageComponent={ImageComponent} />}
    </>}
  </article>;
}
