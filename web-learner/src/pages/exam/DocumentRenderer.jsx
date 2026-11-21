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

function BlockDocument({ type, content, token, ImageComponent }) {
  return <div className={`exam-layout-document exam-layout-${type.toLowerCase()} exam-block-document`}>
    {content.blocks.map((block, index) => <div className={`exam-block exam-block-${block.kind.toLowerCase()}`} key={block.id}>
      {block.kind === "HEADING" && <h4 className="exam-preserve-lines">{block.text}</h4>}
      {block.kind === "PARAGRAPH" && <p className="exam-preserve-lines">{block.text}</p>}
      {block.kind === "BULLET" && <ul><li className="exam-preserve-lines">{block.text}</li></ul>}
      {block.kind === "NUMBERED" && <ol start={content.blocks.slice(0, index).filter(item =>
        item.kind === "NUMBERED").length + 1}><li className="exam-preserve-lines">{block.text}</li></ol>}
      {block.kind === "META" && <dl className="exam-layout-meta"><div><dt>{block.label}</dt><dd className="exam-preserve-lines">{block.value}</dd></div></dl>}
      {block.kind === "TABLE" && <TableDocument content={block} />}
      {block.kind === "IMAGE" && <div className="exam-block-image" style={{ width: `${block.width || 100}%`,
        marginLeft: block.align === "right" ? "auto" : block.align === "center" ? "auto" : 0,
        marginRight: block.align === "left" ? "auto" : block.align === "center" ? "auto" : 0 }}>
        <ImageDocument document={{ imageUrl: block.imageUrl }} token={token}
          ImageComponent={ImageComponent} />{block.caption && <p className="exam-block-caption">{block.caption}</p>}</div>}
      {block.kind === "SEPARATOR" && <hr />}
    </div>)}
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
  const blocks = structured && content.version === 2 && Array.isArray(content.blocks);
  const valid = blocks ? true : type === "IMAGE" ? Boolean(document.imageUrl) :
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
      {blocks && <BlockDocument type={type} content={content} token={token} ImageComponent={ImageComponent} />}
      {!blocks && type === "TEXT" && <TextDocument content={content} />}
      {!blocks && type === "EMAIL" && <EmailDocument content={content} />}
      {!blocks && type === "TABLE" && <TableDocument content={content} />}
      {!blocks && type === "CHAT" && <ChatDocument content={content} />}
      {!blocks && type === "FORM" && <FormDocument content={content} />}
      {!blocks && isLayoutType(type) && <LayoutDocument type={type} content={content} />}
      {!blocks && type === "IMAGE" && <ImageDocument document={document} token={token} ImageComponent={ImageComponent} />}
      {!blocks && type !== "IMAGE" && document.imageUrl &&
        <ImageDocument document={document} token={token} ImageComponent={ImageComponent} />}
    </>}
  </article>;
}
