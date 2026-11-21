import { hasTranslatedContent, parsePart7Translation } from "../admin/partSevenTranslationData";
import { parseStudioTranslation } from "../admin/partSevenStudioData";
import DocumentRenderer from "./DocumentRenderer";

export default function PartSevenTranslationView({ translation, documents, token, ImageComponent }) {
  if (!translation) return null;
  if (translation.startsWith("P7DOCV2:")) {
    const studioDocuments = (documents || []).map(doc => ({ type: doc.type,
      blocks: Array.isArray(doc.content?.blocks) ? doc.content.blocks : [] }));
    let parsed;
    try { parsed = parseStudioTranslation(translation, studioDocuments); }
    catch { return null; }
    return <section className="exam-translation-view" aria-label="Bản dịch tiếng Việt">
      <h3>Bản dịch tiếng Việt</h3>
      {parsed.legacyText && <p className="exam-preserve-lines">{parsed.legacyText}</p>}
      {parsed.documents.map((item, index) => item.blocks.some(block =>
        block.text?.trim() || block.value?.trim() || block.caption?.trim() ||
        block.headers?.some(value => value.trim()) || block.rows?.some(row => row.some(value => value.trim()))) &&
        <DocumentRenderer key={index} document={{ type: item.type, order: index + 1,
          content: { version: 2, blocks: item.blocks } }} token={token}
          ImageComponent={ImageComponent} heading={`Bản dịch tài liệu ${index + 1}`} />)}
    </section>;
  }
  const originals = (documents || []).map(doc => ({ type: doc.type,
    ...(doc.content && typeof doc.content === "object" ? doc.content : { content: doc.content }) }));
  const parsed = parsePart7Translation(translation, originals);
  if (parsed.invalid) return null;
  return <section className="exam-translation-view" aria-label="Bản dịch tiếng Việt">
    <h3>Bản dịch tiếng Việt</h3>
    {parsed.legacyText && <p className="exam-preserve-lines">{parsed.legacyText}</p>}
    {parsed.documents.map((doc, index) => hasTranslatedContent(doc, originals[index]) &&
      (doc.type === "IMAGE" ? <p key={index}>Chú thích hình {index + 1}: {doc.content}</p> :
        <DocumentRenderer key={index} document={{ order: index + 1, type: doc.type, content: doc.content }}
          token={token} ImageComponent={ImageComponent} heading={`Bản dịch tài liệu ${index + 1}`} />))}
  </section>;
}
