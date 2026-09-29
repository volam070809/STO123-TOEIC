import SiteHeader from "../components/layout/SiteHeader";
import SiteFooter from "../components/layout/SiteFooter";

export default function SiteLayout({ children, className = "" }) {
  return <div className="site-shell"><SiteHeader /><main className={"site-main " + className}>{children}</main><SiteFooter /></div>;
}

