import { useEffect, useState } from "react";
import { paymentApi } from "../services/paymentApi";
import { startPaymentPolling } from "../services/paymentFlow";

export function usePaymentStatus(paymentId, token) {
  const [refreshKey, setRefreshKey] = useState(0);
  const [state, setState] = useState({ kind: "loading", polling: false });

  useEffect(() => {
    if (!paymentId || !token) return;
    return startPaymentPolling({ paymentId, load: () => paymentApi.status(paymentId, token),
      onView: view => setState({ ...view, paymentId, token }) });
  }, [paymentId, token, refreshKey]);

  const view = state.paymentId === paymentId && state.token === token ? state :
    { kind: "loading", polling: false };
  return { view, refresh: () => setRefreshKey(key => key + 1) };
}
