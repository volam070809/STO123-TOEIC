import { useEffect, useState } from "react";
import { examMediaBlob } from "../../services/examApi";

function PrivateImage({ endpoint, token, alt }) {
  const [url, setUrl] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(endpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); }
    }).catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token]);
  if (error) return <p className="exam-media-error">Không thể tải hình ảnh.</p>;
  return url ? <img className="exam-image" src={url} alt={alt} /> : <p>Đang tải hình ảnh…</p>;
}

function PrivateAudio({ endpoint, token }) {
  const [url, setUrl] = useState("");
  const [error, setError] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    let objectUrl = "";
    examMediaBlob(endpoint, token, controller.signal).then(blob => {
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); }
    }).catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [endpoint, token]);
  if (error) return <p className="exam-media-error">Không thể tải âm thanh.</p>;
  return url ? <audio controls preload="none" src={url}>Trình duyệt không hỗ trợ phát âm thanh.</audio> : <p>Đang tải âm thanh…</p>;
}

function Question({ question, review, onAnswer, onFlag, saving, saveError }) {
  const labels = [["A", question.a], ["B", question.b], ["C", question.c], ["D", question.d]];
  return <section className="exam-question" id={"question-" + question.attemptQuestionId} tabIndex={-1}>
    <div className="exam-question-title"><h3>Câu {question.order}</h3>
      {!review && <button type="button" className="outline-button" onClick={() => onFlag(question)}>
        {question.flagged ? "Bỏ đánh dấu" : "Đánh dấu"}</button>}</div>
    {question.text && <p className="exam-preserve-lines">{question.text}</p>}
    {question.part === 2 && !review ? <p>Nghe câu hỏi và chọn A, B hoặc C.</p> : null}
    <div className="exam-options">{labels.filter(([letter, value]) => value != null || (question.part === 2 && !review && letter !== "D")).map(([letter, value]) =>
      <label key={letter} className="exam-option"><input type="radio" name={"answer-" + question.attemptQuestionId}
        value={letter} checked={question.selectedOption === letter} disabled={review}
        onChange={() => onAnswer(question, letter)} />
        <span><strong>{letter}.</strong> {question.part === 2 && !review ? "" : value}</span></label>)}</div>
    {!review && saving && <small>Lưu câu trả lời…</small>}
    {!review && saveError && <small className="exam-error">Chưa lưu được câu trả lời. Vui lòng chọn lại.</small>}
    {review && <div className={"exam-review-status " + question.status?.toLowerCase()}>
      <strong>{question.status === "CORRECT" ? "Đúng" : question.status === "INCORRECT" ? "Sai" : "Chưa trả lời"}</strong>
      <p>Bạn chọn: {question.selectedOption || "—"} · Đáp án đúng: {question.correctOption}</p>
      <p>{question.explanation}</p></div>}
  </section>;
}

export default function ExamContent({ group, independentQuestion, attemptId, token, review = false,
  onAnswer, onFlag, savingIds = {}, saveErrors = {} }) {
  const questions = group?.questions ?? [independentQuestion];
  const base = `/api/attempts/${attemptId}/groups/${group?.groupId}`;
  return <div className="exam-content" key={group?.groupId ?? independentQuestion?.attemptQuestionId}>
    {group && <div className="exam-context">
      {group.part === 7 ? group.documents?.map(document => <article key={document.order} className="exam-document">
        <h3>Tài liệu {document.order} · {document.type}</h3>
        {document.text && <p className="exam-preserve-lines">{document.text}</p>}
        {document.hasImage && <PrivateImage endpoint={`${base}/documents/${document.order}/image`} token={token} alt="Hình tài liệu bài đọc" />}</article>) : <>
        {group.context && <p className="exam-preserve-lines">{group.context}</p>}
        {group.hasImage && <PrivateImage endpoint={base + "/image"} token={token} alt="Hình minh họa câu hỏi" />}
        {group.hasAudio && <PrivateAudio endpoint={base + "/audio"} token={token} />}
      </>}
    </div>}
    {questions.map(question => <Question key={question.attemptQuestionId} question={question} review={review}
      onAnswer={onAnswer} onFlag={onFlag} saving={savingIds[question.attemptQuestionId]}
      saveError={saveErrors[question.attemptQuestionId]} />)}
  </div>;
}
