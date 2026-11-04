import SiteHeader from "../components/layout/SiteHeader";
import SiteFooter from "../components/layout/SiteFooter";
import { useHelpPageContext } from "../components/layout/HelpContextProvider";

export default function SiteLayout({ children, className = "", aiContext = null, examActive = false }) {
  useHelpPageContext({ learningContext: aiContext, examActive });
  return <div className="site-shell"><SiteHeader /><main className={"site-main " + className}>{children}</main><SiteFooter /></div>;
}

