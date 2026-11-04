import { hasTranslatedContent, parsePart7Translation } from "../admin/partSevenTranslationData";
import DocumentRenderer from "./DocumentRenderer";

export default function PartSevenTranslationView({ translation, documents, token, ImageComponent }) {
  if (!translation) return null;
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
