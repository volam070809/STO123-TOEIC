import { useState } from "react";
import { PrivateImage, PrivateAudio } from "./ExamMedia";
import DocumentRenderer from "./DocumentRenderer";

function materialLayout(group, imageSize) {
  if (!group) return "";
  const documents = group.documents || [];
  const tables = documents.filter(document => document.type === "TABLE");
  const textLength = (group.context?.length || 0) + documents.reduce((length, document) => {
    if (document.type === "TEXT") return length + (typeof document.content === "string" ? document.content.length : 0);
    if (document.type === "EMAIL") return length + (document.content?.body?.length || 0);
    if (document.type === "CHAT") return length + (Array.isArray(document.content?.messages) ?
      document.content.messages.reduce((sum, message) => sum + (message.text?.length || 0), 0) : 0);
    return length;
  }, 0);
  const longText = textLength > 500 || documents.some(document =>
    (document.type === "EMAIL" && (document.content?.body?.length || 0) > 450) ||
    (document.type === "TEXT" && typeof document.content === "string" && document.content.length > 500));
  const veryWideTable = tables.some(document => {
    const content = document.content;
    return (Array.isArray(content?.headers) && content.headers.length >= 5) ||
      (Array.isArray(content?.rows) && content.rows.some(row => Array.isArray(row) &&
        row.some(cell => String(cell ?? "").length > 120)));
  });
  if (longText || veryWideTable || tables.length > 1) return " is-stacked";
  const smallImage = imageSize && imageSize.width <= 420 && imageSize.height <= 420;
  if (tables.length || (group.hasImage && !smallImage) || documents.some(document =>
    document.type === "IMAGE" || document.imageUrl || document.type === "FORM")) return " is-wide";
  return "";
}

function Question({ question, review, onAnswer, onFlag, onRetry, saveState, submissionPending }) {
  const labels = [["A", question.a], ["B", question.b], ["C", question.c], ["D", question.d]];
  return <section className="exam-question" id={"question-" + question.attemptQuestionId} tabIndex={-1}>
    <div className="exam-question-title"><h3>Câu {question.order}</h3>
      {!review && <button type="button" className="outline-button" disabled={submissionPending} onClick={() => onFlag(question)}>
        {question.flagged ? "Bỏ đánh dấu" : "Đánh dấu"}</button>}</div>
    {question.text && <p className="exam-preserve-lines">{question.text}</p>}
    <div className="exam-options">{labels.filter(([letter, value]) => value != null ||
      (!review && (question.part === 1 || question.part === 2) && (question.part !== 2 || letter !== "D"))).map(([letter, value]) =>
      <label key={letter} className={`exam-option ${review && question.correctOption === letter ? "review-correct-option" : ""} ${review && question.selectedOption === letter && question.correctOption !== letter ? "review-selected-wrong" : ""}`}><input type="radio" name={"answer-" + question.attemptQuestionId}
        value={letter} checked={question.selectedOption === letter} disabled={review || submissionPending}
        onChange={() => onAnswer(question, letter)} />
        <span><strong>{letter}.</strong> {!review && question.part <= 2 ? "" : value}
          {review && question.correctOption === letter && question.selectedOption === letter &&
            <em className="review-option-tag">Bạn chọn · Đáp án đúng</em>}
          {review && question.selectedOption === letter && question.correctOption !== letter &&
            <em className="review-option-tag">Bạn chọn</em>}
          {review && question.correctOption === letter && question.selectedOption !== letter &&
            <em className="review-option-tag">Đáp án đúng</em>}</span></label>)}</div>
    {!review && saveState === "saving" && <small role="status">Đang lưu...</small>}
    {!review && saveState === "saved" && <small className="exam-save-success" role="status">Đã lưu</small>}
    {!review && saveState === "error" && <div className="exam-save-error" role="alert">
      <small className="exam-error">Lỗi lưu.</small>
      <button type="button" className="outline-button" disabled={submissionPending}
        onClick={() => onRetry(question)}>Thử lưu lại</button>
    </div>}
    {review && <div className={"exam-review-status " + question.status?.toLowerCase()}>
      <strong>{question.status === "CORRECT" ? "Đúng" : question.status === "INCORRECT" ? "Sai" : "Chưa trả lời"}</strong>
      {!question.selectedOption && <p>Bạn chưa trả lời câu này.</p>}
      <p className="exam-preserve-lines">{question.explanation}</p></div>}
  </section>;
}

export default function ExamContent({ group, independentQuestion, attemptId, token, review = false,
  mode = "MOCK", onAnswer, onFlag, onRetry, saveStates = {}, submissionPending = false }) {
  const [sourceOpen, setSourceOpen] = useState(false);
  const [imageSize, setImageSize] = useState(null);
  const questions = group?.questions ?? [independentQuestion];
  const base = `/api/attempts/${attemptId}/groups/${group?.groupId}`;
  return <div className={`exam-content${group ? " exam-content-grouped" + materialLayout(group, imageSize) : ""}`}>
    {group && <div className="exam-source">
      <button className="outline-button exam-source-toggle" type="button" aria-expanded={sourceOpen}
        onClick={() => setSourceOpen(value => !value)}>{sourceOpen ? "Thu gọn đề" : "Xem đề"}</button>
      <div className={`exam-context${sourceOpen ? " is-open" : ""}`}>
      {group.context && <p className="exam-preserve-lines">{group.context}</p>}
      {group.documents?.map(document =>
        <DocumentRenderer key={document.order} document={document} token={token} />)}
      {group.hasImage && <PrivateImage endpoint={base + "/image"} token={token} alt="Hình minh họa câu hỏi"
        onDimensions={(width, height) => setImageSize({ width, height })} />}
      {group.hasAudio && !review && <PrivateAudio key={base} endpoint={base + "/audio"} token={token} mode={mode} />}
      </div>
    </div>}
    <div className="exam-group-questions">{questions.map(question => <Question key={question.attemptQuestionId} question={question} review={review}
      onAnswer={onAnswer} onFlag={onFlag} onRetry={onRetry} saveState={saveStates[question.attemptQuestionId]}
      submissionPending={submissionPending} />)}</div>
  </div>;
}
