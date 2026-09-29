let scriptPromise;
let initializedClientId;
let activeCredentialHandler;

function loadGoogleIdentity() {
  if (window.google?.accounts?.id) return Promise.resolve(window.google.accounts.id);
  if (!scriptPromise) {
    scriptPromise = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      const fail = () => {
        clearTimeout(timer);
        script.remove();
        scriptPromise = null;
        reject(new Error("Google Identity Services is unavailable."));
      };
      const timer = setTimeout(fail, 15_000);
      script.src = "https://accounts.google.com/gsi/client";
      script.async = true;
      script.dataset.sto123Google = "true";
      script.onload = () => {
        clearTimeout(timer);
        if (window.google?.accounts?.id) resolve(window.google.accounts.id);
        else fail();
      };
      script.onerror = fail;
      document.head.appendChild(script);
    });
  }
  return scriptPromise;
}

export async function renderGoogleSignIn(container, clientId, onCredential) {
  const identity = await loadGoogleIdentity();
  activeCredentialHandler = onCredential;
  if (initializedClientId !== clientId) {
    identity.initialize({
      client_id: clientId,
      callback: response => activeCredentialHandler?.(response.credential),
      auto_select: false,
    });
    initializedClientId = clientId;
  }
  container.replaceChildren();
  identity.renderButton(container, {
    type: "standard",
    theme: "outline",
    size: "large",
    text: "signin_with",
    shape: "rectangular",
    width: Math.min(container.clientWidth || 320, 480),
    locale: "vi",
  });
}

export function clearGoogleSignInHandler(handler) {
  if (activeCredentialHandler === handler) activeCredentialHandler = null;
}

export function disableGoogleAutoSelect() {
  window.google?.accounts?.id?.disableAutoSelect();
}
