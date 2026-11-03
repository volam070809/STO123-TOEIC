import SiteHeader from "../components/layout/SiteHeader";
import SiteFooter from "../components/layout/SiteFooter";
import ContactSupportWidget from "../components/layout/ContactSupportWidget";
import AiToeicChatWidget from "../components/layout/AiToeicChatWidget";

export default function SiteLayout({ children, className = "", aiContext = null }) {
  return <div className="site-shell"><SiteHeader /><main className={"site-main " + className}>{children}</main><SiteFooter /><ContactSupportWidget /><AiToeicChatWidget learningContext={aiContext} /></div>;
}

