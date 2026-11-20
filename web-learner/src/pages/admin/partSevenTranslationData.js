import { isLayoutType, moveItem } from "./partSevenDocumentData.js";

export const translationPrefix = "P7DOCV1:";

export function alignTranslation(original, current) {
  const type = original.type;
  if (type !== current?.type) current = null;
  const content = current?.content;
  if (type === "TEXT" || type === "IMAGE")
    return { type, content: typeof content === "string" ? content : "" };
  if (type === "EMAIL") return { type, content: {
    version: 1, from: content?.from ?? original.from ?? "", to: content?.to ?? original.to ?? "",
    date: content?.date ?? original.date ?? "", subject: content?.subject ?? "",
    body: content?.body ?? ""
  } };
  if (type === "TABLE") return { type, content: {
    version: 1, title: content?.title ?? "",
    headers: original.headers.map((_, i) => content?.headers?.[i] ?? ""),
    rows: original.rows.map((row, i) => row.map((_, j) => content?.rows?.[i]?.[j] ?? ""))
  } };
  if (type === "CHAT") return { type, content: { version: 1,
    messages: original.messages.map((message, i) => ({
      sender: content?.messages?.[i]?.sender ?? message.sender ?? "",
      time: content?.messages?.[i]?.time ?? message.time ?? "",
      text: content?.messages?.[i]?.text ?? ""
    }))
  } };
  if (type === "FORM") return { type, content: { version: 1, title: content?.title ?? "",
    fields: original.fields.map((_, i) => ({ label: content?.fields?.[i]?.label ?? "",
      value: content?.fields?.[i]?.value ?? "" }))
  } };
  if (isLayoutType(type)) return { type, content: {
    version: 1, title: content?.title ?? "", subtitle: content?.subtitle ?? "",
    footer: content?.footer ?? "",
    metadata: (original.metadata || []).map((item, i) => ({
      label: content?.metadata?.[i]?.label ?? item.label ?? "",
      value: content?.metadata?.[i]?.value ?? item.value ?? ""
    })),
    paragraphs: (original.paragraphs || []).map((_, i) => content?.paragraphs?.[i] ?? ""),
    items: (original.items || []).map((_, i) => content?.items?.[i] ?? ""),
    headers: original.headers?.map((_, i) => content?.headers?.[i] ?? "") || [],
    rows: original.rows?.map((row, i) => row.map((_, j) => content?.rows?.[i]?.[j] ?? "")) || []
  } };
  return { type, content: "" };
}

export function parsePart7Translation(value, originals) {
  let legacyText = value || "";
  let stored = [];
  let invalid = false;
  if (value?.startsWith(translationPrefix)) {
    try {
      const parsed = JSON.parse(value.slice(translationPrefix.length));
      if (parsed.version !== 1 || parsed.kind !== "PART7_TRANSLATIONS" ||
          !Array.isArray(parsed.documents)) throw new Error("Invalid translation");
      stored = parsed.documents;
      legacyText = parsed.legacyText || "";
    } catch { invalid = true; }
  }
  return { legacyText, invalid, documents: originals.map((doc, i) =>
    alignTranslation(doc, stored[i])) };
}

export function hasTranslatedContent(item, original) {
  if (!item) return false;
  const { type, content } = item;
  if (type === "TEXT" || type === "IMAGE") return !!content?.trim();
  if (type === "EMAIL") return !!(content.subject?.trim() || content.body?.trim() ||
    (original && ["from", "to", "date"].some(key => content[key] !== (original[key] || ""))));
  if (type === "TABLE") return !!(content.title?.trim() || content.headers.some(x => x.trim()) ||
    content.rows.some(row => row.some(x => x.trim())));
  if (type === "CHAT") return content.messages.some((message, index) => message.text?.trim() ||
    (original && ["sender", "time"].some(key => message[key] !== (original.messages?.[index]?.[key] || ""))));
  if (type === "FORM") return !!(content.title?.trim() || content.fields.some(field =>
    field.label?.trim() || field.value?.trim()));
  if (isLayoutType(type)) return !!(content.title?.trim() || content.subtitle?.trim() || content.footer?.trim() ||
    content.paragraphs?.some(value => value.trim()) || content.items?.some(value => value.trim()) ||
    content.metadata?.some((item, i) => item.label !== original?.metadata?.[i]?.label ||
      item.value !== original?.metadata?.[i]?.value) ||
    (type === "INVOICE" || type === "SCHEDULE") &&
      (content.headers?.some(value => value.trim()) || content.rows?.some(row => row.some(value => value.trim()))));
  return false;
}

export function serializePart7Translation(value, originals) {
  if (value.invalid) throw new Error("Bản dịch cũ không đọc được; hãy kiểm tra trước khi lưu.");
  const documents = originals.map((doc, i) => {
    const aligned = alignTranslation(doc, value.documents[i]);
    return hasTranslatedContent(aligned, doc) ? aligned : null;
  });
  if (documents.every(doc => !doc)) return value.legacyText || null;
  return translationPrefix + JSON.stringify({ version: 1, kind: "PART7_TRANSLATIONS",
    legacyText: value.legacyText || null, documents });
}

export function updateTranslationStructure(state, documentIndex, mutation) {
  const current = state.documents[documentIndex];
  if (!current || typeof current.content !== "object") return state;
  const { key, type, index, direction } = mutation;
  const content = { ...current.content };
  const array = content[key];
  if (!Array.isArray(array)) return state;
  const blank = key === "metadata" || key === "fields" ? { label: "", value: "" } :
    key === "messages" ? { sender: "", time: "", text: "" } :
    key === "rows" ? content.headers.map(() => "") : "";
  content[key] = type === "move" ? moveItem(array, index, direction) :
    type === "delete" ? array.filter((_, at) => at !== index) : [...array, blank];
  if (key === "headers") content.rows = content.rows.map(row => type === "move" ?
    moveItem(row, index, direction) : type === "delete" ? row.filter((_, at) => at !== index) : [...row, ""]);
  return { ...state, documents: state.documents.map((item, at) =>
    at === documentIndex ? { ...item, content } : item) };
}
