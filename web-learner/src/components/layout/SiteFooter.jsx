import { Link } from "react-router-dom";
import { BrandLogo } from "./SiteHeader";

export default function SiteFooter() {
  return <footer className="site-footer"><div className="site-footer-inner">
    <div className="site-footer-columns">
      <div className="site-footer-brand"><Link className="site-footer-logo" to="/"><BrandLogo /></Link><p>Học đúng trọng tâm · Tiến bộ mỗi ngày</p></div>
      <div><strong>Khóa học</strong><span>TOEIC 450+</span><span>TOEIC 650+</span><span>TOEIC 800+</span></div>
      <div><strong>Luyện tập</strong><Link to="/vocabulary" state={{ vocabularyRoot: true }}>Từ vựng</Link><Link to="/practice/vocabulary">Luyện từ vựng</Link><span>Ngữ pháp</span><span>Đề thi</span></div>
      <div><strong>Hỗ trợ</strong><span>Câu hỏi thường gặp</span><span>Liên hệ</span><span>Điều khoản</span></div>
    </div>
    <div className="site-footer-bottom"><small>© 2026 STO123 · Nền tảng luyện thi TOEIC của nhóm.</small></div>
  </div></footer>;
}

