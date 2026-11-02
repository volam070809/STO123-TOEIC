export const newDocument = () => ({ type: "TEXT", content: "", imagePath: "",
  from: "", to: "", date: "", subject: "", body: "", title: "",
  headers: ["Cột 1", "Cột 2"], rows: [["", ""]],
  messages: [{ sender: "", time: "", text: "" }],
  fields: [{ label: "", value: "" }] });

export function fromDocument(doc) {
  const base = { ...newDocument(), type: doc.type, imagePath: doc.image || "" };
  if (doc.type === "TEXT") return { ...base, content: doc.content || "" };
  if (doc.type === "IMAGE") return base;
  try {
    const parsed = JSON.parse(doc.content);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
      throw new Error("Invalid document");
    return { ...base, ...parsed, _rawContent: doc.content, _original: parsed };
  } catch {
    return { ...base, _parseError: true, _rawContent: doc.content };
  }
}

export function documentPayload(item) {
  if (item.type === "TEXT") return { type: item.type, content: item.content, imagePath: null };
  if (item.type === "IMAGE") return { type: item.type, content: null, imagePath: item.imagePath };
  const values = item.type === "EMAIL" ? {
    from: item.from, to: item.to, date: item.date, subject: item.subject, body: item.body
  } : item.type === "TABLE" ? {
    title: item.title, headers: item.headers, rows: item.rows
  } : item.type === "CHAT" ? { messages: item.messages } :
    { title: item.title, fields: item.fields };
  const merged = { ...item._original, version: 1, ...values };
  const content = item._rawContent && JSON.stringify(merged) === JSON.stringify(item._original) ?
    item._rawContent : JSON.stringify(merged);
  return { type: item.type, content, imagePath: null };
}
