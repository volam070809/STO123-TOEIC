import { PrivateImage } from "./ExamMedia";

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

function ImageDocument({ document, token }) {
  return document.imageUrl && <PrivateImage endpoint={document.imageUrl} token={token} alt="Hình tài liệu" />;
}

export default function DocumentRenderer({ document, token }) {
  const { type, content } = document ?? {};
  const structured = content && typeof content === "object" && !Array.isArray(content);
  const valid = type === "IMAGE" ? Boolean(document.imageUrl) :
    type === "TEXT" ? typeof content === "string" :
    type === "EMAIL" ? structured && typeof content.body === "string" :
    type === "TABLE" ? structured && Array.isArray(content.headers) && Array.isArray(content.rows) :
    type === "CHAT" ? structured && Array.isArray(content.messages) :
    type === "FORM" ? structured && Array.isArray(content.fields) : false;
  return <article className="exam-document">
    <h3>Tài liệu {document?.order}</h3>
    {!valid ? unavailable : <>
      {type === "TEXT" && <TextDocument content={content} />}
      {type === "EMAIL" && <EmailDocument content={content} />}
      {type === "TABLE" && <TableDocument content={content} />}
      {type === "CHAT" && <ChatDocument content={content} />}
      {type === "FORM" && <FormDocument content={content} />}
      {type === "IMAGE" && <ImageDocument document={document} token={token} />}
      {type !== "IMAGE" && document.imageUrl &&
        <ImageDocument document={document} token={token} />}
    </>}
  </article>;
}
