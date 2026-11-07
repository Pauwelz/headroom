// Passkey login for the login page. Static and same origin: the page allows no inline script.
if (window.PublicKeyCredential?.parseRequestOptionsFromJSON) {
  const button = document.getElementById("passkey-login");
  const error = document.getElementById("passkey-error");
  const returnUrl = document.querySelector('input[name="ReturnUrl"]').value;
  const failure = "Headroom could not log you in with this passkey. Use a login code instead.";
  let pending;

  const login = async (mediation) => {
    pending?.abort();
    pending = new AbortController();
    error.hidden = true;
    try {
      const options = await (await fetch("/login/passkey/options", { method: "POST" })).json();
      const credential = await navigator.credentials.get({
        publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options),
        mediation,
        signal: pending.signal,
      });
      const response = await fetch("/login/passkey?returnUrl=" + encodeURIComponent(returnUrl), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(credential.toJSON()),
      });
      if (response.ok) return location.assign((await response.json()).redirect);
      error.textContent = failure;
      error.hidden = false;
    } catch (e) {
      // The Person closed the prompt, or a newer request replaced this one: no message.
      if (e.name === "AbortError") return;
      if (e.name !== "NotAllowedError") {
        error.textContent = failure;
        error.hidden = false;
      }
    }
    if (mediation === "optional") startConditional();
  };

  const startConditional = async () => {
    if (await PublicKeyCredential.isConditionalMediationAvailable?.()) login("conditional");
  };

  button.hidden = false;
  button.addEventListener("click", () => login("optional"));
  startConditional();
}
