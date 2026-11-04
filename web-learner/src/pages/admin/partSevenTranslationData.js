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
