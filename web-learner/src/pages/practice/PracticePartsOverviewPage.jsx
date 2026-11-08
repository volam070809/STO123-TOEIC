import { useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { partLabels, skillGroups } from "./practicePartLabels";
import "../../styles/exam.css";
import "../../styles/vocabulary.css";
import "../../styles/practice-parts.css";
import "../../styles/learner-journey.css";
import "../../styles/practice-parts-studio.css";

export default function PracticePartsOverviewPage() {
  const { requireAuthentication } = useGuestAuthPrompt();
  const [part, setPart] = useState(5);

  return <SiteLayout className="vocab-page"><div className="site-container practice-page practice-parts">
    <div className="page-heading practice-hub-heading"><span><Link to="/practice">Luyện tập</Link> / Theo Part</span>
      <h1><span className="practice-heading-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12 6.5c-2.2-1.7-5.1-2.2-8-1.5v12c2.9-.7 5.8-.2 8 1.5 2.2-1.7 5.1-2.2 8-1.5V5c-2.9-.7-5.8-.2-8 1.5Z"/><path d="M12 6.5v12"/></svg></span>Hôm nay bạn muốn luyện gì?</h1>
      <p>Khám phá bảy Part TOEIC. Đăng nhập để xem bài luyện phù hợp và lưu tiến bộ của bạn.</p>
    </div>
    <div className="practice-parts-hub">
      <div className="practice-parts-main">
        <section className="practice-part-select" aria-labelledby="practice-select-title"><div className="practice-select-heading"><div><span className="practice-kicker">CHỌN NỘI DUNG</span><h2 id="practice-select-title">Bảy Part, hai kỹ năng</h2></div><p>Chạm vào một Part để tìm hiểu bài luyện.</p></div>
          {skillGroups.map(group => <div className="practice-skill-group" key={group.label}><div className="practice-skill-heading"><span className="practice-skill-icon" aria-hidden="true">{group.icon}</span><h3>{group.label}</h3><small>{group.range}</small></div>
            <div className="practice-part-picker" role="group" aria-label={`Chọn Part ${group.label}`}>
              {partLabels.slice(group.start - 1, group.end).map((name, index) => {
                const number = group.start + index;
                return <button type="button" key={number} aria-pressed={part === number} onClick={() => setPart(number)}>
                  <span className="practice-tile-number">{String(number).padStart(2, "0")}</span><span className="practice-tile-copy"><strong>Part {number}</strong><small>{name}</small></span>
                  {part === number && <span className="practice-tile-check" aria-hidden="true">✓</span>}</button>;
              })}
            </div></div>)}
        </section>
        <section className="practice-config-panel" aria-labelledby="practice-config-title"><span className="practice-kicker">BÀI LUYỆN CỦA BẠN</span>
          <h2 id="practice-config-title">Part {part} <span>· {partLabels[part - 1]}</span></h2>
          <div className="practice-performance" aria-label={`Kết quả Part ${part}`}>
            <p className="practice-performance-empty">Đăng nhập để xem kết quả <span>Tiến bộ luyện tập được lưu theo tài khoản của bạn.</span></p>
          </div>
          <div className="practice-config-fields"><label htmlFor="guest-practice-difficulty">Độ khó</label>
            <select id="guest-practice-difficulty" disabled><option>Đăng nhập để xem mức độ khả dụng</option></select></div>
          <fieldset className="practice-part-presets"><legend>Độ dài bài luyện</legend>
            <p className="practice-no-presets">Đăng nhập để xem các bài luyện đang có.</p>
          </fieldset>
          <div className="practice-selection-action"><button className="primary-button" type="button" aria-haspopup="dialog"
            onClick={() => requireAuthentication(`/practice/parts?part=${part}`)}>Bắt đầu luyện tập <span aria-hidden="true">→</span></button></div>
        </section>
      </div>
      <details className="practice-progress-details"><summary><span><small>NHÌN LẠI QUÁ TRÌNH HỌC</small><strong>Tiến bộ của bạn</strong><em>Xem thống kê từng Part sau khi đăng nhập</em></span><DisclosureToggle /></summary>
        <div className="practice-progress-body"><div className="practice-progress-empty"><strong>Tiến bộ gắn với tài khoản của bạn</strong><p>Đăng nhập và hoàn thành một bài luyện để xem kết quả ở đây.</p></div></div>
      </details>
    </div>
  </div></SiteLayout>;
}
