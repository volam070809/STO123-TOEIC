import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import DisclosureToggle from "../../components/layout/DisclosureToggle";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/smart-learning.css";
import "../../styles/learner-journey.css";

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
  const [primary, ...alternatives] = profile?.recommendations || [];
  return <SiteLayout><div className="site-container smart-page smart-journey">
    <header className="smart-journey-heading"><span className="smart-eyebrow">GỢI Ý RIÊNG CHO BẠN</span><h1>Gợi ý ôn luyện</h1>
      <p>Từ kết quả đã hoàn thành, chọn một bước học phù hợp để tiến bộ đều hơn.</p></header>
    {current.error && <div className="smart-error" role="alert"><p>{current.error}</p>
      <button className="outline-button" type="button" onClick={() => setRetry(value => value + 1)}>Thử lại</button></div>}
    {!profile && !current.error && <p className="smart-loading" role="status">Đang phân tích kết quả học tập…</p>}
    {profile && <>
      {!hasEvidence && <section className="smart-empty"><h2>Bắt đầu từ một bài luyện</h2>
        <p>Chưa có kết quả hoàn thành để đánh giá. Bạn có thể luyện một Part hoặc làm bài kiểm tra đầu vào; các nhận định sẽ tự cập nhật sau khi hoàn thành.</p>
        <Link className="primary-button" to="/practice/parts">Chọn Part để luyện</Link></section>}
      {hasEvidence && !primary && <section className="smart-empty"><h2>Chọn một Part để tiếp tục</h2><p>Chưa có đề xuất ưu tiên phù hợp với kết quả hiện tại. Bạn vẫn có thể luyện bất kỳ Part nào.</p>
        <Link className="primary-button" to="/practice/parts">Chọn Part để luyện</Link></section>}
      {hasEvidence && primary && <section className="smart-coach" aria-labelledby="smart-coach-title"><div className="smart-coach-number">{String(primary.part).padStart(2, "0")}</div>
        <div className="smart-coach-content"><span className="smart-eyebrow">NÊN LUYỆN TIẾP</span><h2 id="smart-coach-title">Part {primary.part} · {partNames[primary.part - 1]}</h2>
          <p>{primary.reason}</p><span className="smart-coach-evidence">{confidenceNames[primary.confidence]} · Dựa trên {primary.evidenceQuestions} câu đã ghi nhận</span>
          <Link className="primary-button" to={primary.actionUrl}>Luyện ngay <span aria-hidden="true">→</span></Link></div></section>}
      {hasEvidence && alternatives.length > 0 && <section className="smart-alternatives"><h2>Hoặc tiếp tục với</h2>{alternatives.slice(0, 2).map(advice =>
        <article key={advice.part}><span>PART {advice.part}</span><div><h3>{partNames[advice.part - 1]}</h3><p>{advice.reason}</p></div>
          <Link to={advice.actionUrl}>Luyện Part {advice.part} →</Link></article>)}</section>}
      <details className="learner-disclosure smart-analysis"><summary><span><small>XEM SÂU HƠN</small><strong>Năng lực của bạn theo 7 Part</strong><em>Tỷ lệ đúng ước tính và nguồn dữ liệu học tập</em></span><DisclosureToggle /></summary>
        <div className="learner-disclosure-body"><p className="learner-detail-intro">Các tỷ lệ phản ánh kết quả học tập quan sát được, không phải điểm TOEIC chính thức. Part chưa có dữ liệu sẽ không hiện tỷ lệ.</p>
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
          <details><summary><span className="smart-evidence-closed">Xem chứng cứ</span><span className="smart-evidence-open">Thu gọn chứng cứ</span><svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m5 7.5 5 5 5-5" /></svg></summary><ul>{part.sources.map(source =>
            <li key={source.source}><span>{sourceNames[source.source] || source.source}</span>
              <strong>{source.questions} câu</strong></li>)}</ul>
            <small>Ảnh hưởng của câu gặp lại và kết quả cũ đã được giảm. Số câu hiệu dụng: {part.effectiveQuestions}.</small></details>
          <Link to={`/practice/parts?part=${part.part}`}>Luyện Part {part.part} →</Link>
        </article>)}</div>
      {profile.recommendationOutcomes?.length > 0 && <section className="smart-section smart-history" aria-labelledby="smart-followup-title">
        <div className="smart-section-heading"><div><span className="smart-eyebrow">SAU KHI LÀM THEO ĐỀ XUẤT</span><h2 id="smart-followup-title">Kết quả luyện tập</h2>
          <p>So sánh lượt luyện Part gần nhất trước khi bắt đầu với lượt đã hoàn thành theo đề xuất. Đây là thay đổi quan sát được, không khẳng định nguyên nhân.</p></div></div>
        <div className="smart-history-list">{profile.recommendationOutcomes.map(item => <div className="smart-history-row" key={item.part}>
          <div><strong>Part {item.part} · {partNames[item.part - 1]}</strong>
            <small>{item.completedAt ? new Date(item.completedAt).toLocaleString("vi-VN") : "Chưa có lượt hoàn thành"}</small></div>
          <div><b>{item.after != null ? `${item.before != null ? `${item.before}% → ` : "Lượt đầu: "}${item.after}%` : "Đang chờ kết quả"}</b>
            <small>{item.questions != null ? `${item.questions} câu đã trả lời` : ""}{item.inProgress ? " · Đang luyện lượt mới" : ""}</small></div>
        </div>)}</div></section>}
      <p className="smart-note">Đề xuất dựa trên dữ liệu đã ghi nhận; kết quả mới sẽ được cập nhật sau khi bạn hoàn thành bài.</p></div></details>
    </>}
  </div></SiteLayout>;
}
