import SiteHeader from "../components/layout/SiteHeader";
import SiteFooter from "../components/layout/SiteFooter";
import { useHelpPageContext } from "../components/layout/HelpContextProvider";
import { useLocation } from "react-router-dom";

export default function SiteLayout({ children, className = "", aiContext = null, examActive = false }) {
  useHelpPageContext({ learningContext: aiContext, examActive });
  const { pathname } = useLocation();
  const family = pathname.startsWith("/exam/") ? "exam" :
    pathname.startsWith("/courses") ? "courses" :
    pathname.startsWith("/vocabulary") ? "vocabulary" :
    pathname.startsWith("/practice") ? "practice" :
    pathname.startsWith("/mock-test") || pathname.startsWith("/placement") ? "assessment" :
    pathname.startsWith("/toeic/structure") ? "structure" :
    pathname === "/" ? "home" : "account";
  return <div className="site-shell" data-page-family={family}>
    <a className="site-skip-link" href="#site-main-content">Chuyển đến nội dung chính</a>
    <SiteHeader /><main id="site-main-content" className={"site-main " + className}>{children}</main><SiteFooter />
  </div>;
}

