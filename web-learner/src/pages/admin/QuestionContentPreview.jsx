import { useEffect, useState } from "react";
import { API_BASE_URL } from "../../services/api";
import DocumentPreview from "./ExamDocumentPreview";
import { fromDocument } from "./partSevenDocumentData";
import { hasTranslatedContent, parsePart7Translation } from "./partSevenTranslationData";

const part6Translation = (value, order) => value?.replace(/\r/g, "").split("\n")
  .find(line => line.trim().startsWith(`[${order}]`))?.trim().slice(3).trim() || "";

function Media({ path, bank, token, audio }) {
  const [url, setUrl] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    if (!path) return undefined;
    let active = true;
    let objectUrl;
    fetch(`${API_BASE_URL}/api/admin/content/media/file?${new URLSearchParams({ path, bank })}`,
      { headers: { Authorization: `Bearer ${token}` } })
      .then(response => { if (!response.ok) throw new Error("Không tải được media."); return response.blob(); })
      .then(blob => { objectUrl = URL.createObjectURL(blob); if (active) setUrl(objectUrl); })
      .catch(() => { if (active) setError("Không tải được media từ Blob."); });
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [path, bank, token]);
  if (!path) return null;
  return <div className="exam-media-field"><small>{path}</small>{error && <p role="alert">{error}</p>}
    {url && (audio ? <audio controls src={url} /> : <img className="exam-media-preview" src={url} alt="Media câu hỏi" />)}</div>;
}

export default function QuestionContentPreview({ unit, token, onEditQuestion }) {
  if (!unit) return null;
  const questions = unit.questions || [];
  const part7Translation = unit.part === 7 ? parsePart7Translation(unit.resource?.translation,
    (unit.documents || []).map(fromDocument)) : null;
  return <div className="exam-content-preview">
    <p><strong>{unit.groupId ? `Nhóm ${unit.groupId}` : `Câu ${questions[0]?.id}`} · Part {unit.part}</strong> · {questions.length} câu</p>
    {unit.resource && <section className="exam-author-question"><h3>Ngữ liệu chung</h3>
      {unit.resource.text && <p style={{ whiteSpace: "pre-wrap" }}>{unit.resource.text}</p>}
      {unit.part === 7 && part7Translation?.legacyText && <p className="exam-preserve-lines"><em>{part7Translation.legacyText}</em></p>}
      {unit.resource.translation && unit.part !== 7 && unit.part !== 6 && unit.part !== 2 && <p><em>{unit.resource.translation}</em></p>}
      {unit.part === 6 && unit.resource.translation && !part6Translation(unit.resource.translation, 1) &&
        <p><em>{unit.resource.translation}</em></p>}
      <Media path={unit.resource.audio} bank={unit.bank || "DE_THI"} token={token} audio />
      <Media path={unit.resource.image} bank={unit.bank || "DE_THI"} token={token} />
    </section>}
    {unit.documents?.map((doc, index) => <div className="exam-document-pair" key={index}><DocumentPreview doc={doc} index={index} title={`Tài liệu ${index + 1} · Bản gốc`} />
      {hasTranslatedContent(part7Translation?.documents[index], fromDocument(doc)) && <DocumentPreview index={index}
        doc={{ type: doc.type, content: typeof part7Translation.documents[index].content === "string" ?
          part7Translation.documents[index].content : JSON.stringify(part7Translation.documents[index].content) }}
        title={`Tài liệu ${index + 1} · Bản dịch tiếng Việt`} translation />}
      {doc.image && <Media path={doc.image} bank={unit.bank || "DE_THI"} token={token} />}</div>)}
    {questions.map((question, index) => <section className="exam-author-question" key={question.id}>
      <h3>Câu {index + 1} · #{question.id}</h3>
      {question.text && <p style={{ whiteSpace: "pre-wrap" }}>{question.text}</p>}
      {unit.part === 2 && unit.resource?.translation && <p><strong>Bản dịch:</strong> {unit.resource.translation}</p>}
      {unit.part === 6 && part6Translation(unit.resource?.translation, index + 1) &&
        <p><strong>Bản dịch câu hỏi:</strong> {part6Translation(unit.resource.translation, index + 1)}</p>}
      {["a", "b", "c", "d"].filter(letter => question[letter]).map(letter =>
        <p key={letter} className={question.correct?.toLowerCase() === letter ? "exam-answer-correct" : ""}>
          <strong>{letter.toUpperCase()}.</strong> {question[letter]}
          {question.correct?.toLowerCase() === letter && <strong> ✓ Đáp án đúng</strong>}
        </p>)}
      {question.difficulty != null && <small>Độ khó: {question.difficulty === 1 ? "Dễ" : question.difficulty === 3 ? "Khó" : "Trung bình"}</small>}
      {question.explanation && <p><strong>Giải thích:</strong> {question.explanation}</p>}
      {onEditQuestion && <button type="button" onClick={() => onEditQuestion(question.id)}>Sửa câu này</button>}
    </section>)}
  </div>;
}
