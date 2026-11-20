export const layoutTemplates = {
  LETTER: { label: "Thư", meta: ["From", "To", "Date"], paragraphs: true, footer: true },
  ARTICLE: { label: "Bài báo", subtitle: true, paragraphs: true, footer: true },
  NOTICE: { label: "Thông báo", meta: ["Date"], paragraphs: true, items: true, footer: true },
  POLICY: { label: "Chính sách", paragraphs: true, items: true, footer: true },
  INVOICE: { label: "Hóa đơn", meta: ["Invoice no.", "Date", "Bill to"], table: true, footer: true },
  WEB: { label: "Trang web", subtitle: true, paragraphs: true, items: true, footer: true },
  AD: { label: "Quảng cáo", subtitle: true, paragraphs: true, items: true, footer: true },
  SCHEDULE: { label: "Lịch trình", meta: ["Date", "Location"], table: true, footer: true },
  SURVEY: { label: "Khảo sát", paragraphs: true, items: true, footer: true }
};

export const documentTypes = {
  TEXT: "Văn bản", EMAIL: "Email", ...Object.fromEntries(Object.entries(layoutTemplates)
    .map(([key, value]) => [key, value.label])), TABLE: "Bảng biểu", CHAT: "Tin nhắn",
  FORM: "Biểu mẫu", IMAGE: "Hình ảnh"
};

export const isLayoutType = type => Object.hasOwn(layoutTemplates, type);
export const moveItem = (items, index, direction) => {
  const target = index + direction;
  if (target < 0 || target >= items.length) return items;
  const next = [...items];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
};

export const newDocument = (type = "TEXT") => ({ type, content: "", imagePath: "",
  from: "", to: "", date: "", subject: "", body: "", title: "", subtitle: "", footer: "",
  metadata: (layoutTemplates[type]?.meta || []).map(label => ({ label, value: "" })),
  paragraphs: layoutTemplates[type]?.paragraphs ? [""] : [],
  items: layoutTemplates[type]?.items ? [""] : [],
  headers: type === "INVOICE" ? ["Item", "Quantity", "Amount"] :
    type === "SCHEDULE" ? ["Time", "Activity"] : ["Cột 1", "Cột 2"],
  rows: type === "INVOICE" ? [["", "", ""]] : [["", ""]],
  messages: [{ sender: "", time: "", text: "" }],
  fields: [{ label: "", value: "" }] });

export function fromDocument(doc) {
  const base = { ...newDocument(doc.type), type: doc.type, imagePath: doc.image || doc.imagePath || "" };
  if (doc.type === "TEXT") return { ...base, content: doc.content || "" };
  if (doc.type === "IMAGE") return base;
  try {
    const parsed = JSON.parse(doc.content);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
      throw new Error("Invalid document");
    for (const key of ["metadata", "paragraphs", "items", "headers", "rows", "messages", "fields"])
      if (key in parsed && !Array.isArray(parsed[key])) throw new Error("Invalid document structure");
    if (parsed.rows?.some(row => !Array.isArray(row)) ||
      parsed.rows?.some(row => row.some(cell => typeof cell !== "string")) ||
      ["paragraphs", "items", "headers"].some(key => parsed[key]?.some(item => typeof item !== "string")) ||
      parsed.metadata?.some(item => !item || typeof item !== "object") ||
      parsed.messages?.some(item => !item || typeof item !== "object") ||
      parsed.fields?.some(item => !item || typeof item !== "object"))
      throw new Error("Invalid document structure");
    return { ...base, ...parsed, _rawContent: doc.content, _original: parsed };
  } catch {
    return { ...base, _parseError: true, _rawContent: doc.content };
  }
}

export function documentPayload(item) {
  if (item.type === "TEXT") return { type: item.type, content: item.content, imagePath: item.imagePath || null };
  if (item.type === "IMAGE") return { type: item.type, content: null, imagePath: item.imagePath || null };
  const values = item.type === "EMAIL" ? {
    from: item.from, to: item.to, date: item.date, subject: item.subject, body: item.body
  } : item.type === "TABLE" ? {
    title: item.title, headers: item.headers, rows: item.rows
  } : item.type === "CHAT" ? { messages: item.messages } : item.type === "FORM" ?
    { title: item.title, fields: item.fields } : isLayoutType(item.type) ? {
      title: item.title, subtitle: item.subtitle, metadata: item.metadata,
      paragraphs: item.paragraphs, items: item.items, headers: item.headers,
      rows: item.rows, footer: item.footer
    } : {};
  const merged = { ...item._original, version: 1, ...values };
  const content = item._rawContent && JSON.stringify(merged) === JSON.stringify(item._original) ?
    item._rawContent : JSON.stringify(merged);
  return { type: item.type, content, imagePath: item.imagePath || null };
}

export function documentIssues(item, index) {
  const name = `Tài liệu ${index + 1}`;
  if (item._parseError) return [`${name}: nội dung cũ không đọc được.`];
  if (item.type === "IMAGE") return item.imagePath ? [] : [`${name}: cần tải hình ảnh.`];
  if (item.type === "TEXT") return item.content?.trim() ? [] : [`${name}: cần nhập nội dung.`];
  if (item.type === "EMAIL") return item.body?.trim() ? [] : [`${name}: cần nhập nội dung email.`];
  if (item.type === "TABLE" || layoutTemplates[item.type]?.table) {
    if (!item.headers?.every(value => value.trim()) || !item.rows?.length ||
      !item.rows.some(row => row.some(value => value.trim())))
      return [`${name}: cần nhập tên cột và ít nhất một ô dữ liệu.`];
  }
  if (item.type === "CHAT" && !item.messages?.every(message => message.text.trim()))
    return [`${name}: cần nhập nội dung từng tin nhắn.`];
  if (item.type === "FORM" && !item.fields?.every(field => field.label.trim()))
    return [`${name}: cần nhập nhãn từng trường.`];
  if (isLayoutType(item.type) && !item.title?.trim() &&
    !item.paragraphs?.some(value => value.trim()) && !item.items?.some(value => value.trim()))
    return [`${name}: cần nhập tiêu đề hoặc nội dung.`];
  return [];
}

export function partSevenIssues(documents, questions) {
  const issues = documents.flatMap(documentIssues);
  if (documents.length < 1 || documents.length > 3) issues.push("Part 7 cần 1–3 tài liệu.");
  if (questions.length < 2 || questions.length > 5) issues.push("Part 7 cần 2–5 câu hỏi.");
  questions.forEach((question, index) => {
    const label = `Câu ${index + 1}`;
    if (!question.text?.trim()) issues.push(`${label}: cần nhập nội dung tiếng Anh.`);
    for (const key of ["a", "b", "c", "d"])
      if (!question[key]?.trim()) issues.push(`${label}: cần nhập đáp án ${key.toUpperCase()}.`);
    if (!question.explanation?.trim()) issues.push(`${label}: cần nhập giải thích.`);
  });
  return issues;
}
