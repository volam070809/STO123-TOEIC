import { Link } from "react-router-dom";
import { BrandLogo } from "./SiteHeader";
import { useAuth } from "../../contexts/AuthState";
import ContactSupportWidget from "./ContactSupportWidget";

const copyrightYear = new Date().getFullYear();

export default function SiteFooter() {
  const { user } = useAuth();
  return <footer className="site-footer"><div className="site-footer-inner">
    <div className="site-footer-columns">
      <div className="site-footer-brand"><Link className="site-footer-logo" to="/"><BrandLogo /></Link><p>Học đúng trọng tâm · Tiến bộ mỗi ngày</p><small>Khám phá, luyện tập và theo dõi hành trình TOEIC trên cùng một nền tảng.</small></div>
      <div><strong>Khám phá</strong><Link to="/courses">Khóa học</Link><Link to="/placement">Kiểm tra đầu vào</Link><Link to="/toeic/structure">Cấu trúc TOEIC</Link></div>
      <div><strong>Học và luyện</strong><Link to="/vocabulary" state={{ vocabularyRoot: true }}>Từ vựng</Link><Link to="/practice/parts">Luyện theo Part</Link><Link to="/mock-test">Thi thử TOEIC</Link></div>
      <div><strong>Tài khoản</strong>{user ? <><Link to="/profile">Hồ sơ của tôi</Link><Link to="/courses">Khóa học của tôi</Link></> : <><Link to="/login">Đăng nhập</Link><Link to="/register">Đăng ký</Link></>}</div>
      <div className="site-footer-contact"><strong>Liên hệ</strong><ContactSupportWidget embedded showTitle={false} /></div>
    </div>
    <div className="site-footer-bottom"><small>© {copyrightYear} STO123 · Hành trình TOEIC của bạn.</small><span>Học đúng trọng tâm, tiến bộ mỗi ngày.</span></div>
  </div></footer>;
}

