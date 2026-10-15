import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import "../../styles/practice-choose.css";

function VocabularyIcon() {
  return <svg viewBox="0 0 96 96" fill="none" aria-hidden="true" focusable="false">
    <rect x="17" y="16" width="48" height="64" rx="9" fill="currentColor" opacity=".14" />
    <path d="M28 16h39a8 8 0 0 1 8 8v54H34a8 8 0 0 1-8-8V24a8 8 0 0 1 8-8Z" fill="white" stroke="currentColor" strokeWidth="3" />
    <path d="M27 69c0-5 4-9 9-9h39M38 31h24M38 41h18M38 51h12" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    <path d="m62 35 4 3 4-3V18h-8v17Z" fill="currentColor" />
    <path d="m53 69 5 4 9-10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
  </svg>;
}

function ToeicIcon() {
  return <svg viewBox="0 0 96 96" fill="none" aria-hidden="true" focusable="false">
    <rect x="17" y="20" width="62" height="57" rx="11" fill="white" stroke="currentColor" strokeWidth="3" />
    <path d="M30 20v-6M66 20v-6M18 35h60" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    <path d="m31 49 4 4 7-8M31 65l4 4 7-8" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
    <path d="M51 49h16M51 65h16" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    <circle cx="73" cy="70" r="13" fill="currentColor" />
    <path d="m68 70 4 4 7-8" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
  </svg>;
}

const options = [
  {
    to: "/practice/vocabulary",
    className: "practice-choice-vocabulary",
    eyebrow: "GHI NHỚ TỪ VỰNG",
    title: "Luyện tập từ vựng",
    description: "Ôn lại từ theo chủ đề, kiểm tra khả năng ghi nhớ và xem kết quả sau mỗi lượt luyện.",
    details: ["Theo chủ đề", "Kiểm tra kiến thức"],
    action: "Luyện từ vựng",
    icon: <VocabularyIcon />,
    image: "/images/vocabulary/office.jpg",
  },
  {
    to: "/practice/parts",
    className: "practice-choice-toeic",
    eyebrow: "CHINH PHỤC 7 PART",
    title: "Luyện tập TOEIC theo Part",
    description: "Chọn một trong 7 Part TOEIC, mức độ khó và số câu phù hợp để luyện theo nhịp độ của bạn.",
    details: ["7 Part TOEIC", "3 mức độ khó"],
    action: "Luyện theo Part",
    icon: <ToeicIcon />,
    image: "/images/home/listening.jpg",
  },
];

export default function PracticeChoosePage() {
  return <SiteLayout className="practice-choice-page">
    <div className="site-container practice-choice-container">
      <header className="practice-choice-heading">
        <span className="practice-choice-kicker">TRUNG TÂM LUYỆN TẬP</span>
        <h1>Chọn hình thức luyện tập</h1>
        <p>Chọn cách luyện phù hợp với mục tiêu hôm nay. Bạn có thể quay lại đây để đổi hình thức bất cứ lúc nào.</p>
      </header>
      <div className="practice-choice-grid" aria-label="Hình thức luyện tập">
        {options.map(option => <Link key={option.to} to={option.to}
          className={`practice-choice-card ${option.className}`}>
          <div className="practice-choice-media">
            <img src={option.image} alt="" width="520" height="293" loading="lazy" />
            <span className="practice-choice-art">{option.icon}</span>
          </div>
          <div className="practice-choice-content">
            <span className="practice-choice-eyebrow">{option.eyebrow}</span>
            <h2>{option.title}</h2>
            <p>{option.description}</p>
            <span className="practice-choice-details">{option.details.map(detail =>
              <span key={detail}>{detail}</span>)}</span>
            <span className="practice-choice-action">{option.action}
              <svg viewBox="0 0 20 20" fill="none" aria-hidden="true" focusable="false">
                <path d="M4 10h11m-5-5 5 5-5 5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </span>
          </div>
        </Link>)}
      </div>
    </div>
  </SiteLayout>;
}
