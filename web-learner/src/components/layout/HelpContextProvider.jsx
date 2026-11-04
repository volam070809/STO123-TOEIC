import { createContext, useContext, useLayoutEffect, useRef, useState } from "react";
import UnifiedHelpWidget from "./UnifiedHelpWidget";

const HelpContext = createContext(null);

export function useHelpPageContext(context) {
  const setPageContext = useContext(HelpContext);
  const owner = useRef(Symbol("help-page"));
  const serialized = JSON.stringify(context || null);
  useLayoutEffect(() => {
    if (!setPageContext) return undefined;
    const registration = owner.current;
    setPageContext({ owner: registration, value: JSON.parse(serialized) });
    return () => setPageContext(current => current?.owner === registration ? null : current);
  }, [setPageContext, serialized]);
}

export default function HelpContextProvider({ children }) {
  const [pageContext, setPageContext] = useState(null);
  return <HelpContext.Provider value={setPageContext}>
    {children}
    {!pageContext?.value?.examActive &&
      <UnifiedHelpWidget learningContext={pageContext?.value?.learningContext || null} />}
  </HelpContext.Provider>;
}
