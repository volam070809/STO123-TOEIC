import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/smart-learning.css";

const partNames = ["Mô tả hình ảnh", "Hỏi và đáp", "Hội thoại", "Bài nói ngắn",
  "Hoàn thành câu", "Hoàn thành đoạn văn", "Đọc hiểu"];
const confidenceNames = { CAO: "Đáng tin cậy", TRUNG_BINH: "Khá tin cậy", TAM_THOI: "Tạm thời" };
const trendNames = { TANG: "Đang cải thiện", GIAM: "Cần chú ý", ON_DINH: "Ổn định", CHUA_DU: "Chưa đủ dữ liệu" };
const sourceNames = { PLACEMENT: "Kiểm tra đầu vào", MOCK: "Thi thử", PRACTICE: "Luyện theo Part", ARENA: "Đấu trường" };

export default function SmartLearningPage() {
  const { token } = useAuth();
  const [retry, setRetry] = useState(0);
  const [view, setView] = useState({ token: null, profile: null, error: "" });
  useEffect(() => {
    if (!token) return undefined;
    let active = true;
    apiRequest("/api/smart-learning/profile", { token }).then(profile => {
      if (active) setView({ token, profile, error: "" });
    }).catch(error => {
      if (active) setView({ token, profile: null,
        error: error.message || "Không thể tải phân tích học tập." });
    });
    return () => { active = false; };
  }, [token, retry]);
  const current = view.token === token ? view : { profile: null, error: "" };
  const profile = current.profile;
  const hasEvidence = profile?.parts.some(part => part.evidenceQuestions > 0);
  return <SiteLayout><div className="site-container smart-page">
    <header className="smart-hero"><div><span className="smart-eyebrow">SMART LEARNING 2.0</span>
      <h1>Học đúng Part cần cải thiện</h1><p>Phân tích từ các bài đã hoàn thành của bạn. Mỗi đề xuất cho biết lý do và mức độ tin cậy để bạn tự chọn bước học tiếp theo.</p>
      <div className="smart-hero-actions"><Link className="primary-button" to="/practice/parts">Luyện theo Part</Link>
        <Link className="outline-button" to="/placement">Xem kiểm tra đầu vào</Link></div></div>
      <div className="smart-hero-mark" aria-hidden="true"><span>7</span><small>Part TOEIC</small></div></header>
    {current.error && <div className="smart-error" role="alert"><p>{current.error}</p>
      <button className="outline-button" type="button" onClick={() => setRetry(value => value + 1)}>Thử lại</button></div>}
    {!profile && !current.error && <p className="smart-loading" role="status">Đang phân tích kết quả học tập…</p>}
    {profile && <>
      {!hasEvidence && <section className="smart-empty"><h2>Bắt đầu từ một bài luyện</h2>
        <p>Chưa có kết quả hoàn thành để đánh giá. Bạn có thể luyện một Part hoặc làm bài kiểm tra đầu vào; các nhận định sẽ tự cập nhật sau khi hoàn thành.</p>
        <Link className="primary-button" to="/practice/parts">Chọn Part để luyện</Link></section>}
      <section className="smart-section" aria-labelledby="smart-advice-title"><div className="smart-section-heading"><div>
        <span className="smart-eyebrow">ƯU TIÊN CỦA BẠN</span><h2 id="smart-advice-title">Nên học gì tiếp?</h2>
        <p>Đề xuất giúp chọn trọng tâm, không thay đổi số câu hoặc câu hỏi ngẫu nhiên trong bài luyện.</p></div></div>
        <div className="smart-advice-list">{profile.recommendations.map((advice, index) =>
          <article className="smart-advice" key={advice.part}><span className="smart-advice-number">0{index + 1}</span>
            <div><strong>Part {advice.part} · {partNames[advice.part - 1]}</strong>
              <p>{advice.reason}</p><small>{confidenceNames[advice.confidence]} · {advice.evidenceQuestions} câu đã ghi nhận</small></div>
            <Link className="outline-button" to={advice.actionUrl}>Luyện Part {advice.part} →</Link></article>)}</div></section>
      <section className="smart-section" aria-labelledby="smart-parts-title"><div className="smart-section-heading"><div>
        <span className="smart-eyebrow">HỒ SƠ 7 PART</span><h2 id="smart-parts-title">Năng lực theo từng Part</h2>
        <p>Tỷ lệ đúng dưới đây là ước tính học tập, không phải điểm TOEIC chính thức.</p></div></div>
        <div className="smart-part-grid">{profile.parts.map(part => <article className="smart-part" key={part.part}>
          <div className="smart-part-top"><div><span>PART {part.part}</span><h3>{partNames[part.part - 1]}</h3></div>
            <strong>{part.evidenceQuestions ? `${part.estimatedAccuracy}%` : "—"}</strong></div>
          <div className="smart-meter" aria-hidden="true"><span style={{ width: `${part.evidenceQuestions ? part.estimatedAccuracy : 0}%` }} /></div>
          <div className="smart-part-tags"><span data-confidence={part.confidence}>{confidenceNames[part.confidence]}</span>
            <span data-trend={part.trend}>{trendNames[part.trend]}</span></div>
          <p>{part.explanation}</p>
          {part.previousAccuracy != null && part.recentAccuracy != null &&
            <div className="smart-comparison"><span>Trước: <b>{part.previousAccuracy}%</b></span>
              <span>Gần nhất: <b>{part.recentAccuracy}%</b></span></div>}
          <details><summary>Chứng cứ đã dùng</summary><ul>{part.sources.map(source =>
            <li key={source.source}><span>{sourceNames[source.source] || source.source}</span>
              <strong>{source.questions} câu</strong></li>)}</ul>
            <small>Ảnh hưởng của câu gặp lại và kết quả cũ đã được giảm. Số câu hiệu dụng: {part.effectiveQuestions}.</small></details>
          <Link to={`/practice/parts?part=${part.part}`}>Luyện Part {part.part} →</Link>
        </article>)}</div></section>
      {profile.recommendationOutcomes?.length > 0 && <section className="smart-section smart-history" aria-labelledby="smart-followup-title">
        <div className="smart-section-heading"><div><span className="smart-eyebrow">SAU KHI LÀM THEO ĐỀ XUẤT</span><h2 id="smart-followup-title">Kết quả luyện tập</h2>
          <p>So sánh lượt luyện Part gần nhất trước khi bắt đầu với lượt đã hoàn thành theo đề xuất. Đây là thay đổi quan sát được, không khẳng định nguyên nhân.</p></div></div>
        <div className="smart-history-list">{profile.recommendationOutcomes.map(item => <div className="smart-history-row" key={item.part}>
          <div><strong>Part {item.part} · {partNames[item.part - 1]}</strong>
            <small>{item.completedAt ? new Date(item.completedAt).toLocaleString("vi-VN") : "Chưa có lượt hoàn thành"}</small></div>
          <div><b>{item.after != null ? `${item.before != null ? `${item.before}% → ` : "Lượt đầu: "}${item.after}%` : "Đang chờ kết quả"}</b>
            <small>{item.questions != null ? `${item.questions} câu đã trả lời` : ""}{item.inProgress ? " · Đang luyện lượt mới" : ""}</small></div>
        </div>)}</div></section>}
      <p className="smart-note">Phân tích phản ánh kết quả quan sát được; một đề xuất không tự chứng minh nguyên nhân của mọi thay đổi điểm số.</p>
    </>}
  </div></SiteLayout>;
}
