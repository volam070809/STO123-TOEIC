export default function DocumentPreview({ doc, index }) {
  let content = doc.content;
  if (doc.type !== "TEXT" && doc.type !== "IMAGE") {
    try { content = JSON.parse(doc.content); } catch { content = null; }
  }
  return <div className="exam-author-doc"><strong>Tài liệu {index + 1}: {doc.type}</strong>
    {doc.type === "TEXT" && <p className="exam-preserve-lines">{content}</p>}
    {doc.type === "IMAGE" && <p>Hình ảnh: {doc.image}</p>}
    {doc.type === "EMAIL" && content && <><p>Người gửi: {content.from} · Người nhận: {content.to}</p><p>Ngày: {content.date} · Chủ đề: {content.subject}</p><p className="exam-preserve-lines">{content.body}</p></>}
    {doc.type === "TABLE" && content && <><p>{content.title}</p><div className="exam-table-scroll"><table><thead><tr>{content.headers?.map((header, at) => <th key={at}>{header}</th>)}</tr></thead><tbody>{content.rows?.map((row, at) => <tr key={at}>{row.map((cell, col) => <td key={col}>{cell}</td>)}</tr>)}</tbody></table></div></>}
    {doc.type === "CHAT" && content && content.messages?.map((message, at) => <p key={at}><strong>{message.sender}</strong> {message.time}: {message.text}</p>)}
    {doc.type === "FORM" && content && <><p>{content.title}</p>{content.fields?.map((field, at) => <p key={at}>{field.label}: {field.value}</p>)}</>}
    {content === null && <p>Không đọc được nội dung tài liệu.</p>}
  </div>;
}

