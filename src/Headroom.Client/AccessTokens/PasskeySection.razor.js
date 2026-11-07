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
