import { useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import "../../styles/exam.css";
import "../../styles/vocabulary.css";
import "../../styles/practice-parts.css";

const parts = [
  "Photographs", "Question-Response", "Conversations", "Talks",
  "Incomplete Sentences", "Text Completion", "Reading Comprehension",
];

export default function PracticePartsOverviewPage() {
  const { requireAuthentication } = useGuestAuthPrompt();
  const [part, setPart] = useState(5);
  const [difficulty, setDifficulty] = useState(1);
  const [preset, setPreset] = useState("SHORT");

  return <SiteLayout className="vocab-page"><div className="site-container practice-page practice-parts">
    <div className="page-heading"><span><Link to="/practice">Luyện tập</Link> / TOEIC theo Part</span>
      <h1>Luyện tập TOEIC theo Part</h1>
      <p>Khám phá 7 Part TOEIC, chọn mức độ và quy mô bài luyện phù hợp trước khi đăng nhập để bắt đầu.</p>
      <Link className="outline-button" to="/practice">← Quay lại chọn hình thức</Link></div>
    <div className="practice-parts-hub">
      <section className="vocab-panel practice-selection-panel">
        <h2><span className="practice-section-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="5" y="3" width="14" height="18" rx="2" /><path d="M9 8h6M9 12h6M9 16h3" /></svg></span>Chọn bài luyện</h2>
        <div className="practice-selection-fields">
          <label>Part<select value={part} onChange={event => setPart(Number(event.target.value))}>
            {parts.map((name, index) => <option key={name} value={index + 1}>Part {index + 1} · {name}</option>)}
          </select></label>
          <label>Độ khó<select value={difficulty} onChange={event => setDifficulty(Number(event.target.value))}>
            {[1, 2, 3].map(level => <option key={level} value={level}>Mức {level}</option>)}
          </select></label>
        </div>
        <fieldset className="practice-part-presets"><legend>Mức luyện tập</legend>
          {["SHORT", "MEDIUM", "FULL"].map(value => <label key={value}>
            <input type="radio" name="guest-practice-preset" value={value} checked={preset === value}
              onChange={() => setPreset(value)} />
            <span className="practice-preset-content"><strong>{value}</strong><span>Chọn quy mô bài luyện</span></span>
          </label>)}
        </fieldset>
        <div className="practice-selection-action">
          <p>Đăng nhập để xem số lượng nội dung hiện có và bắt đầu lượt luyện.</p>
          <button className="primary-button" type="button" aria-haspopup="dialog"
            onClick={() => requireAuthentication("/practice/parts")}>Bắt đầu luyện
            <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M5 12h14m-5-5 5 5-5 5" /></svg>
          </button>
        </div>
      </section>
    </div>
  </div></SiteLayout>;
}
