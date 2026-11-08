export default function AdminQuestionStatisticsPanel({ data }) {
  if (!data) return null;
  return <div className="admin-question-statistics">
    {data.banks.map(bank => <section className="admin-panel admin-question-stat-bank" key={bank.bank}>
      <div className="admin-section-heading"><div><h2>{bank.bank === "DE_THI" ? "Ngân hàng đề thi" : "Ngân hàng luyện tập"}</h2>
        <p>Mỗi câu hỏi nguồn chỉ tính một lần, dù được sử dụng trong nhiều đề.</p></div>
        <div className="admin-question-stat-totals"><span>Tổng câu hỏi <strong>{bank.totalQuestions}</strong></span><span>Tổng nhóm <strong>{bank.totalGroups}</strong></span></div></div>
      <div className="admin-question-part-grid">{bank.parts.map(part => <div key={part.part} className="admin-question-part">
        <strong>Part {part.part}</strong><span>{part.questions} câu</span><small>{part.groups} nhóm</small>
      </div>)}</div>
    </section>)}
    <section className="admin-panel admin-question-stat-exams"><div className="admin-section-heading"><div><h2>Câu hỏi theo đề thi</h2>
      <p>Đề đã xuất bản dùng số câu của phiên bản hiện hành; đề nháp dùng các câu đang liên kết.</p></div></div>
      {data.exams.length ? <div className="admin-question-exam-list">{data.exams.map(exam => <div key={exam.examId}>
        <span><strong>{exam.name}</strong><small>Đề #{exam.examId} · {exam.status === "OPEN" ? "Đang mở" : "Chưa mở"}</small></span>
        <b>{exam.questions} câu</b></div>)}</div> : <p>Chưa có đề thi được soạn.</p>}
    </section>
  </div>;
}
