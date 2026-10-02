import { PrivateImage, PrivateAudio } from "./ExamMedia";
import DocumentRenderer from "./DocumentRenderer";

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
  const questions = group?.questions ?? [independentQuestion];
  const base = `/api/attempts/${attemptId}/groups/${group?.groupId}`;
  return <div className="exam-content" key={group?.groupId ?? independentQuestion?.attemptQuestionId}>
    {group && <div className="exam-context">
      {group.context && <p className="exam-preserve-lines">{group.context}</p>}
      {group.documents?.map(document =>
        <DocumentRenderer key={document.order} document={document} token={token} />)}
      {group.hasImage && <PrivateImage endpoint={base + "/image"} token={token} alt="Hình minh họa câu hỏi" />}
      {group.hasAudio && !review && <PrivateAudio key={base} endpoint={base + "/audio"} token={token} mode={mode} />}
    </div>}
    {questions.map(question => <Question key={question.attemptQuestionId} question={question} review={review}
      onAnswer={onAnswer} onFlag={onFlag} onRetry={onRetry} saveState={saveStates[question.attemptQuestionId]}
      submissionPending={submissionPending} />)}
  </div>;
}
