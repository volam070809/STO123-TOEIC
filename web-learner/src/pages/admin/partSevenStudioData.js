import { fromDocument, moveItem } from "./partSevenDocumentData";

export const studioTemplates = [
  ["TEXT", "Văn bản", "Cơ bản"], ["EMAIL", "Email", "Thư tín"],
  ["LETTER", "Thư", "Thư tín"], ["MEMO", "Bản ghi nhớ", "Thư tín"],
  ["CHAT", "Tin nhắn", "Thư tín"], ["ARTICLE", "Bài báo", "Ấn phẩm"],
  ["REPORT", "Báo cáo", "Ấn phẩm"], ["REVIEW", "Bài đánh giá", "Ấn phẩm"],
  ["BROCHURE", "Tờ gấp", "Ấn phẩm"], ["CATALOG", "Danh mục", "Ấn phẩm"],
  ["WEB", "Trang web", "Số"], ["AD", "Quảng cáo", "Ấn phẩm"],
  ["POSTER", "Áp phích", "Ấn phẩm"], ["NOTICE", "Thông báo", "Công việc"],
  ["POLICY", "Chính sách", "Công việc"], ["AGENDA", "Chương trình họp", "Công việc"],
  ["SCHEDULE", "Lịch trình", "Công việc"], ["TABLE", "Bảng biểu", "Công việc"],
  ["INVOICE", "Hóa đơn", "Giao dịch"], ["RECEIPT", "Biên lai", "Giao dịch"],
  ["ORDER", "Đơn đặt hàng", "Giao dịch"], ["MENU", "Thực đơn", "Giao dịch"],
  ["FORM", "Biểu mẫu", "Biểu mẫu"], ["SURVEY", "Khảo sát", "Biểu mẫu"],
  ["IMAGE", "Hình ảnh", "Hình ảnh"]
].map(([type, label, category]) => ({ type, label, category }));

export const newId = () => globalThis.crypto?.randomUUID?.() ||
  `b${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;
export const makeBlock = (kind, values = {}) => ({ id: newId(), kind,
  ...(kind === "TABLE" ? { headers: ["", ""], rows: [["", ""]] } :
    kind === "META" ? { label: "", value: "" } :
    kind === "IMAGE" ? { path: "", caption: "", align: "center", width: 100 } :
    kind === "SEPARATOR" ? {} : { text: "" }), ...values });
export function reorderItem(items, from, to) {
  if (from < 0 || to < 0 || from >= items.length || to >= items.length) return items;
  const result = [...items];
  result.splice(to, 0, result.splice(from, 1)[0]);
  return result;
}

export function newStudioDocument(type = "EMAIL") {
  const m = label => ["META", { label }];
  const blueprints = {
    TEXT: [["PARAGRAPH"]],
    EMAIL: [m("From"), m("To"), m("Date"), m("Subject"), ["PARAGRAPH"]],
    LETTER: [m("From"), m("To"), m("Date"), ["PARAGRAPH"], ["PARAGRAPH"], m("Signature")],
    MEMO: [["HEADING"], m("To"), m("From"), m("Date"), m("Subject"), ["PARAGRAPH"]],
    CHAT: [m("Sender · Time"), ["PARAGRAPH"], m("Sender · Time"), ["PARAGRAPH"]],
    ARTICLE: [["HEADING"], m("By"), ["PARAGRAPH"], ["PARAGRAPH"]],
    REPORT: [["HEADING"], m("Prepared by"), m("Date"), ["PARAGRAPH"], ["TABLE"]],
    REVIEW: [["HEADING"], m("Rating"), ["PARAGRAPH"]],
    BROCHURE: [["HEADING"], ["PARAGRAPH"], ["BULLET"], ["BULLET"]],
    CATALOG: [["HEADING"], ["TABLE"], ["PARAGRAPH"]],
    WEB: [["HEADING"], m("URL"), ["PARAGRAPH"], ["BULLET"]],
    AD: [["HEADING"], ["PARAGRAPH"], ["BULLET"]],
    POSTER: [["HEADING"], ["PARAGRAPH"]],
    NOTICE: [["HEADING"], m("Date"), ["PARAGRAPH"], ["BULLET"]],
    POLICY: [["HEADING"], ["PARAGRAPH"], ["NUMBERED"], ["NUMBERED"]],
    AGENDA: [["HEADING"], m("Date"), m("Location"), ["NUMBERED"], ["TABLE"]],
    SCHEDULE: [["HEADING"], m("Date"), m("Location"), ["TABLE"]],
    TABLE: [["HEADING"], ["TABLE"]],
    INVOICE: [["HEADING"], m("Invoice no."), m("Bill to"), m("Date"), ["TABLE"], m("Total")],
    RECEIPT: [["HEADING"], m("Receipt no."), m("Date"), ["TABLE"], m("Total")],
    ORDER: [["HEADING"], m("Order no."), m("Ship to"), ["TABLE"], m("Total")],
    MENU: [["HEADING"], ["TABLE"], ["PARAGRAPH"]],
    FORM: [["HEADING"], m("Name"), m("Department"), m("Date"), ["PARAGRAPH"]],
    SURVEY: [["HEADING"], ["PARAGRAPH"], ["NUMBERED"], ["NUMBERED"]],
    IMAGE: [["IMAGE"]]
  };
  return { id: newId(), type, blocks: (blueprints[type] || blueprints.TEXT).map(([kind, values]) =>
    makeBlock(kind, values)) };
}

function appendText(blocks, kind, text, origin) {
  if (text) blocks.push(makeBlock(kind, { text, _legacy: origin }));
}
export function importStudioDocument(source) {
  if (Array.isArray(source?.blocks)) return { ...source, id: source.id || newId() };
  const raw = source?.content;
  if (typeof raw === "string") {
    try {
      const parsed = JSON.parse(raw);
      if (parsed.version === 2 && Array.isArray(parsed.blocks))
        return { id: newId(), type: source.type, blocks: parsed.blocks,
          legacyV1: parsed.legacyV1 || null };
    } catch { /* Legacy TEXT content is a plain string. */ }
  }
  const legacy = source?._original || source?.body !== undefined || source?.metadata || source?.messages ||
    source?.fields ? source : fromDocument(source || { type: "TEXT", content: "" });
  if (legacy._parseError) throw new Error("Tài liệu cũ không đọc được; chưa thể chuyển đổi an toàn.");
  const blocks = [];
  appendText(blocks, "HEADING", legacy.title, "title");
  appendText(blocks, "PARAGRAPH", legacy.subtitle, "subtitle");
  if (legacy.type === "EMAIL") {
    for (const [key, label] of [["from", "From"], ["to", "To"], ["date", "Date"], ["subject", "Subject"]])
      if (legacy[key]) blocks.push(makeBlock("META", { label, value: legacy[key], _legacy: key }));
    appendText(blocks, "PARAGRAPH", legacy.body, "body");
  }
  if (legacy.type === "TEXT") appendText(blocks, "PARAGRAPH", legacy.content, "content");
  for (const [index, item] of (legacy.metadata || []).entries())
    blocks.push(makeBlock("META", { ...item, _legacy: `metadata.${index}` }));
  for (const [index, value] of (legacy.paragraphs || []).entries())
    appendText(blocks, "PARAGRAPH", value, `paragraphs.${index}`);
  for (const [index, value] of (legacy.items || []).entries())
    appendText(blocks, "BULLET", value, `items.${index}`);
  if (legacy.headers?.length && (legacy.type === "TABLE" || legacy.type === "INVOICE" || legacy.type === "SCHEDULE"))
    blocks.push(makeBlock("TABLE", { headers: legacy.headers, rows: legacy.rows || [legacy.headers.map(() => "")],
      _legacy: "table" }));
  for (const [index, message] of (legacy.messages || []).entries()) {
    if (message.sender || message.time) blocks.push(makeBlock("META", { label: message.sender || "Time",
      value: message.time || "", _legacy: `messages.${index}.meta` }));
    appendText(blocks, "PARAGRAPH", message.text, `messages.${index}.text`);
  }
  for (const [index, field] of (legacy.fields || []).entries())
    blocks.push(makeBlock("META", { ...field, _legacy: `fields.${index}` }));
  appendText(blocks, "PARAGRAPH", legacy.footer, "footer");
  if (legacy.imagePath) blocks.push(makeBlock("IMAGE", { path: legacy.imagePath, _legacy: "image" }));
  return { id: newId(), type: legacy.type, blocks: blocks.length ? blocks : [makeBlock("PARAGRAPH")],
    legacyV1: legacy._rawContent || (typeof source.content === "string" && source.content.startsWith("{") ?
      source.content : null) };
}

export function serializeStudioDocument(doc) {
  return { type: doc.type, content: JSON.stringify({ version: 2,
    blocks: doc.blocks.map(block => { const safe = { ...block }; delete safe._legacy; return safe; }),
    ...(doc.legacyV1 && { legacyV1: doc.legacyV1 }) }), imagePath: null };
}

export function studioIssues(documents, questions) {
  const issues = [];
  if (documents.length < 1 || documents.length > 3) issues.push("Cần 1–3 tài liệu.");
  documents.forEach((doc, index) => {
    if (doc.type === "EMAIL" && !doc.blocks.some(block => block.kind === "PARAGRAPH" && block.text?.trim()))
      issues.push(`Tài liệu ${index + 1}: email cần nội dung thư.`);
    if (["TABLE", "INVOICE", "RECEIPT", "ORDER", "SCHEDULE", "MENU", "CATALOG"].includes(doc.type) &&
      !doc.blocks.some(block => block.kind === "TABLE" && block.headers.every(header => header.trim()) &&
        block.rows.some(row => row.some(cell => cell.trim()))))
      issues.push(`Tài liệu ${index + 1}: cần bảng có tên cột và ít nhất một ô dữ liệu.`);
    if (doc.type === "IMAGE" && !doc.blocks.some(block => block.kind === "IMAGE" && block.path))
      issues.push(`Tài liệu ${index + 1}: cần tải ảnh.`);
    if (!doc.blocks.length || !doc.blocks.some(block =>
      block.kind === "IMAGE" ? !!block.path : block.kind === "TABLE" ?
        block.rows.some(row => row.some(cell => cell.trim())) :
        block.kind === "META" ? !!block.value?.trim() : !!block.text?.trim()))
      issues.push(`Tài liệu ${index + 1}: cần nội dung hoặc ảnh.`);
    doc.blocks.forEach((block, at) => {
      if (block.kind === "IMAGE" && !block.path) issues.push(`Tài liệu ${index + 1}, ảnh ${at + 1}: chưa tải ảnh.`);
      if (block.kind === "TABLE" && (!block.headers.every(value => value.trim()) ||
        !block.rows.length || block.rows.some(row => row.length !== block.headers.length)))
        issues.push(`Tài liệu ${index + 1}, bảng ${at + 1}: cần tên cột và dòng hợp lệ.`);
    });
  });
  if (questions.length < 2 || questions.length > 5) issues.push("Cần 2–5 câu hỏi.");
  questions.forEach((question, index) => {
    if (!question.text?.trim()) issues.push(`Câu ${index + 1}: thiếu nội dung.`);
    for (const key of ["a", "b", "c", "d"])
      if (!question[key]?.trim()) issues.push(`Câu ${index + 1}: thiếu đáp án ${key.toUpperCase()}.`);
    if (!question.explanation?.trim()) issues.push(`Câu ${index + 1}: thiếu giải thích.`);
  });
  return issues;
}

export function translationBlock(block, old) {
  const base = { id: block.id, kind: block.kind };
  if (["HEADING", "PARAGRAPH", "BULLET", "NUMBERED"].includes(block.kind)) return { ...base, text: old?.text || "" };
  if (block.kind === "META") return { ...base, label: old?.label || block.label, value: old?.value || "" };
  if (block.kind === "IMAGE") return { ...base, caption: old?.caption || "" };
  if (block.kind === "TABLE") return { ...base,
    headers: block.headers.map((_, i) => old?.headers?.[i] || ""),
    rows: block.rows.map((row, i) => row.map((_, j) => old?.rows?.[i]?.[j] || "")) };
  return base;
}
export const alignStudioTranslation = (doc, prior = {}) => ({ type: doc.type,
  blocks: doc.blocks.map(block => translationBlock(block, prior.blocks?.find(item => item.id === block.id))) });

export function parseStudioTranslation(value, documents) {
  if (value?.startsWith("P7DOCV2:")) {
    try {
      const parsed = JSON.parse(value.slice(8));
      return { legacyText: parsed.legacyText || "", documents: documents.map((doc, i) =>
        alignStudioTranslation(doc, parsed.documents?.[i])) };
    } catch { throw new Error("Bản dịch hiện tại không đọc được; chưa thể chỉnh sửa an toàn."); }
  }
  if (value?.startsWith("P7DOCV1:")) {
    try {
      const parsed = JSON.parse(value.slice(8));
      return { legacyText: parsed.legacyText || "", documents: documents.map((doc, index) => {
        const stored = parsed.documents?.[index];
        if (!stored) return alignStudioTranslation(doc);
        const content = stored.content;
        return { type: doc.type, blocks: doc.blocks.map(block => {
          const origin = block._legacy;
          if (typeof content === "string") return translationBlock(block,
            block.kind === "IMAGE" ? { caption: content } : { text: content });
          if (!origin || !content) return translationBlock(block);
          if (origin === "table") return translationBlock(block, { headers: content.headers, rows: content.rows });
          if (origin === "image") return translationBlock(block, { caption: "" });
          const [key, position, field] = origin.split(".");
          if (key === "metadata" || key === "fields") return translationBlock(block, content[key]?.[Number(position)]);
          if (key === "messages") {
            const message = content.messages?.[Number(position)];
            return translationBlock(block, field === "meta" ? { label: message?.sender, value: message?.time } :
              { text: message?.text });
          }
          if (key === "paragraphs" || key === "items") return translationBlock(block,
            { text: content[key]?.[Number(position)] });
          if (["from", "to", "date", "subject"].includes(key)) return translationBlock(block,
            { label: block.label, value: content[key] });
          return translationBlock(block, { text: content[key] });
        }) };
      }) };
    } catch { throw new Error("Bản dịch Part 7 cũ không đọc được; chưa thể chỉnh sửa an toàn."); }
  }
  return { legacyText: value || "", documents: documents.map(doc => alignStudioTranslation(doc)) };
}
export function serializeStudioTranslation(value, documents) {
  const aligned = documents.map((doc, i) => alignStudioTranslation(doc, value.documents?.[i]));
  const hasTranslation = aligned.some(doc => doc.blocks.some(block =>
    [block.text, block.value, block.caption].some(text => text?.trim()) ||
    block.headers?.some(text => text.trim()) || block.rows?.some(row => row.some(text => text.trim()))));
  return hasTranslation ? `P7DOCV2:${JSON.stringify({ version: 2, kind: "PART7_TRANSLATIONS",
    legacyText: value.legacyText || null, documents: aligned })}` : value.legacyText || null;
}

export { moveItem };
