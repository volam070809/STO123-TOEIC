import SiteHeader from "../components/layout/SiteHeader";
import SiteFooter from "../components/layout/SiteFooter";
import UnifiedHelpWidget from "../components/layout/UnifiedHelpWidget";

export default function SiteLayout({ children, className = "", aiContext = null }) {
  return <div className="site-shell"><SiteHeader /><main className={"site-main " + className}>{children}</main><SiteFooter /><UnifiedHelpWidget learningContext={aiContext} /></div>;
}

