import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { sectionParts, sectionQuestionCount } from "./toeicStructure";
import "./toeicStructure.css";

const sections = [
  { id: "listening", label: "Listening", vietnamese: "Phần nghe", minutes: "Khoảng 45 phút",
    introduction: "Nghe tiếng Anh trong các tình huống giao tiếp và chọn đáp án theo nội dung đã nghe." },
  { id: "reading", label: "Reading", vietnamese: "Phần đọc", minutes: "75 phút",
    introduction: "Đọc câu, đoạn văn và bộ tài liệu; tự phân bổ thời gian trong phần đọc." },
];

export default function ToeicStructurePage() {
  return <SiteLayout><div className="site-container toeic-structure">
    <div className="toeic-structure-hero">
      <div className="toeic-structure-intro">
        <Link className="toeic-structure-back" to="/">Trang chủ</Link>
        <h1>Cấu trúc đề thi TOEIC Listening &amp; Reading</h1>
        <p>Bài thi gồm hai phần Nghe và Đọc, với bảy dạng câu hỏi. Xem từng Part để biết bạn sẽ nghe, đọc và chọn đáp án như thế nào.</p>
        <nav className="toeic-structure-jump" aria-label="Chuyển đến phần thi">
          <a href="#listening">Xem Listening</a><a href="#reading">Xem Reading</a>
        </nav>
      </div>
      <img src="/images/home/exam-sheet.webp" alt="Thí sinh tô đáp án trên phiếu trắc nghiệm" width="700" height="467" />
    </div>

    <div className="toeic-structure-overview" aria-label="Tổng quan bài thi">
      <div><strong>200</strong><span>câu hỏi</span></div>
      <div><strong>120 phút</strong><span>thời gian làm bài</span></div>
      <div><strong>7 Part</strong><span>4 Part nghe · 3 Part đọc</span></div>
    </div>

    {sections.map(section => <section className="toeic-section" id={section.id} key={section.id}
      aria-labelledby={`${section.id}-heading`}>
      <div className="toeic-section-heading">
        <div><p>{section.vietnamese}</p><h2 id={`${section.id}-heading`}>{section.label}</h2>
          <span>{section.introduction}</span></div>
        <div className="toeic-section-facts"><strong>{sectionQuestionCount(section.id)} câu</strong><span>{section.minutes}</span></div>
      </div>
      <div className="toeic-part-distribution" role="img"
        aria-label={`Phân bổ câu hỏi ${section.label}: ${sectionParts(section.id).map(part => `Part ${part.number} ${part.questions} câu`).join(", ")}`}>
        {sectionParts(section.id).map(part => <span key={part.number}
          style={{ flexGrow: part.questions }} aria-hidden="true" />)}
      </div>
      <div className="toeic-part-legend" aria-hidden="true">{sectionParts(section.id).map(part =>
        <span key={part.number}>Part {part.number} · {part.questions} câu</span>)}</div>
      <div className="toeic-part-grid">{sectionParts(section.id).map(part => <article className="toeic-part-card" key={part.number}>
        <div className="toeic-part-card-heading"><span>Part {part.number}</span><strong>{part.questions} câu</strong></div>
        <h3>{part.english}</h3><p className="toeic-part-vietnamese">{part.vietnamese}</p>
        <p>{part.task}</p><p className="toeic-part-material">{part.material}</p>
      </article>)}</div>
    </section>)}

    <div className="toeic-structure-finish">
      <div><h2>Thời gian được tính thế nào?</h2>
        <p>120 phút là thời gian làm bài: phần Listening kéo dài khoảng 45 phút theo audio, tiếp theo là 75 phút cho Reading. Thời gian làm thủ tục trước bài thi không nằm trong 120 phút này.</p>
        <p>Cấu trúc trên theo <a href="https://www.ets.org/toeic/about/listening-reading.html" target="_blank" rel="noreferrer">thông tin TOEIC Listening &amp; Reading của ETS</a> và khớp số câu chuẩn đang cấu hình trong STO123.</p></div>
      <div className="toeic-structure-actions"><Link className="outline-button" to="/">Về trang chủ</Link>
        <Link className="primary-button" to="/mock-test">Thi thử TOEIC</Link></div>
    </div>
  </div></SiteLayout>;
}
