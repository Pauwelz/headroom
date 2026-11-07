// Passkey support check and the registration ceremony. The server options and the credential travel as JSON text.
export function isSupported() {
    return !!window.PublicKeyCredential
        && typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function'
        && typeof PublicKeyCredential.prototype.toJSON === 'function';
}

// Returns the credential as JSON text, or null when the Person closes the browser prompt.
export async function create(optionsJson) {
    try {
        const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson));
        const credential = await navigator.credentials.create({ publicKey });
        return JSON.stringify(credential.toJSON());
    } catch (e) {
        if (e.name === 'NotAllowedError' || e.name === 'AbortError') return null;
        throw e;
    }
}

// Returns true once when the offer cookie is present, and deletes it so a reload does not offer again.
export function takeOfferMarker(name) {
    if (!document.cookie.split('; ').some(c => c.startsWith(name + '='))) return false;
    document.cookie = `${name}=; Max-Age=0; Path=/; Secure; SameSite=Lax`;
    return true;
}
